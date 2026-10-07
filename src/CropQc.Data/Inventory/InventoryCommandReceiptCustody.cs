using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ChangeReceiptCustodyAsync(CropQcDbContext db,
        CanonicalProjectionFactory factory, InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.ReceiptCustody is { TransferId: > 0, ReceiptId: > 0 }, "Exact receipt custody intent is required.");
        var intent = c.ReceiptCustody!;
        Require(!intent.Allocations.IsDefaultOrEmpty && intent.Allocations.Length <= 100
            && intent.Allocations.All(x => x.Id > 0 && x.Quantity > 0)
            && intent.Allocations.Select(x => x.Id).Distinct().Count() == intent.Allocations.Length,
            "Select each proven allocation once with a positive quantity.");
        var transfer = await db.InterCrewTransfers.Include(x => x.SourceWarehouse).SingleAsync(x => x.Id == intent.TransferId, ct);
        var receipt = await db.Receipts.Include(x => x.Warehouse).Include(x => x.VarietyLines).SingleAsync(x => x.Id == intent.ReceiptId, ct);
        Require(transfer.ConcurrencyVersion == intent.TransferVersion && receipt.ConcurrencyVersion == intent.ReceiptVersion,
            "Receipt or transfer changed; reload custody.", InventoryCommandStatus.Stale);
        Require(transfer.RequiresTruckReceipt && transfer.Status == InterCrewTransferStatuses.InTransit
            && transfer.ReceivingReceiptId == receipt.Id && receipt.IsTransferReceipt && !receipt.IsDeleted
            && receipt.TransferCompletedAt == null && receipt.ReceiptType == "Truck receipt"
            && !string.IsNullOrWhiteSpace(receipt.CompuTechReceiptId)
            && TruckReceiptRoutes.RequiresReceiptForGroup(transfer.SourceWarehouse.Code, transfer.DestinationCustodyGroup)
            && TruckReceiptRoutes.Group(receipt.Warehouse.Code) == transfer.DestinationCustodyGroup,
            "An open matched Truck Receipt on the exact configured route is required.");
        var moves = await db.TreatmentLineageMovements.Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Include(x => x.DestinationSegment).Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
        var ledger = await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
        var acks = await db.ReceiptCustodyAcknowledgments.Include(x => x.Placements)
            .Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
        var dispatch = moves.Where(x => x.MovementType == "InterCrewDispatch" && x.ReversesTreatmentLineageMovementId == null).ToArray();
        var segments = moves.SelectMany(x => new[] { x.SourceSegment, x.DestinationSegment }).Where(x => x != null).DistinctBy(x => x!.Id).ToDictionary(x => x!.Id, x => x!);
        var placements = acks.SelectMany(x => x.Placements).ToArray();
        Require(dispatch.Length > 0 && dispatch.All(x => x.SourceSegment != null && x.BinCount > 0)
            && acks.All(x => dispatch.Any(d => d.Id == x.DispatchMovementId))
            && dispatch.All(d => acks.Where(x => x.DispatchMovementId == d.Id).Sum(x => x.Quantity)
                <= d.BinCount - moves.Where(r => r.ReversesTreatmentLineageMovementId == d.Id).Sum(r => r.BinCount))
            && acks.All(x => x.ReceiptId == receipt.Id && x.Quantity > 0 && x.Placements.All(p => p.Quantity > 0)
                && x.Placements.Sum(p => p.Quantity) <= x.Quantity)
            && InventoryPhysicalFlow.Matches(ledger, moves, segments)
            && moves.Where(x => x.MovementType == "InterCrewReceive").All(x => placements.Any(p => p.MovementId == x.Id && p.Quantity == x.BinCount))
            && placements.All(p => ledger.Any(l => l.Id == p.InventoryAdjustmentId && l.ChangeAmount == p.Quantity)
                && moves.Any(m => m.Id == p.MovementId && m.BinCount == p.Quantity))
            && dispatch.Sum(x => x.BinCount - moves.Where(r => r.ReversesTreatmentLineageMovementId == x.Id).Sum(r => r.BinCount)) == transfer.BinsLoaded
            && ledger.Where(x => x.RoomId == transfer.SourceRoomId && x.WarehouseId == transfer.SourceWarehouseId).Sum(x => x.ChangeAmount) == -transfer.BinsLoaded
            && (transfer.BinsReceived ?? 0) == acks.Sum(x => x.Quantity),
            "Original dispatch, acknowledged custody and placement evidence do not conserve quantity.");
        Require(receipt.VarietyLines.Count > 0 && receipt.VarietyLines.All(x => x.BinCount > 0)
            && receipt.VarietyLines.Sum(x => x.BinCount) == receipt.BinCount,
            "Receiving quantities must match the receipt's canonical variety lines.");
        var map = await CanonicalIdentityMap.LoadAsync(db, dispatch.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).ToArray(), ct);
        InventoryIdentity Identity(TreatmentLineageMovement movement) => map.Resolve(CanonicalIdentityMap.Historical(movement.SourceSegment!), movement.ReceiptId).Current;
        Require(dispatch.All(x => Identity(x).CropYear == receipt.CropYear), "Receipt crop year does not match dispatched fruit.");
        Require(receipt.VarietyLines.All(v => v.BinCount <= dispatch.Where(x => Identity(x).FruitProfileId == v.FruitProfileId)
            .Sum(x => x.BinCount - moves.Where(r => r.ReversesTreatmentLineageMovementId == x.Id).Sum(r => r.BinCount))),
            "Over-receipt cannot be acknowledged against this load; reconcile the observed overage separately.");
        var beforeAcknowledged = acks.Sum(x => x.Quantity);
        var beforePlaced = placements.Sum(x => x.Quantity);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        if (c.Kind == InventoryCommandKind.AcknowledgeTransfer)
        {
            Require(intent.Destination == null, "Acknowledgement establishes receipt-held custody; room placement is a separate operation.");
            foreach (var item in intent.Allocations)
            {
                var movement = dispatch.SingleOrDefault(x => x.Id == item.Id);
                Require(movement != null, "Selected original dispatch allocation was not found.");
                var available = movement!.BinCount - moves.Where(x => x.ReversesTreatmentLineageMovementId == item.Id).Sum(x => x.BinCount)
                    - acks.Where(x => x.DispatchMovementId == item.Id).Sum(x => x.Quantity);
                Require(item.Quantity <= available, "Acknowledgement exceeds the unresolved original dispatch allocation.");
                var acknowledgment = new ReceiptCustodyAcknowledgment
                {
                    Receipt = receipt,
                    InterCrewTransfer = transfer,
                    DispatchMovementId = item.Id,
                    Quantity = item.Quantity,
                    OperationKey = c.OperationKey,
                    ActorId = c.ActorId,
                    AcknowledgedAt = now
                };
                acks.Add(acknowledgment); db.ReceiptCustodyAcknowledgments.Add(acknowledgment);
                effects.Add(new($"transit:{transfer.Id}:{item.Id}", available, available - item.Quantity, item.Quantity, transfer.Id, [], []));
            }
            Require(acks.GroupBy(x => Identity(dispatch.Single(d => d.Id == x.DispatchMovementId)).FruitProfileId)
                .All(g => g.Sum(x => x.Quantity) <= receipt.VarietyLines.Where(x => x.FruitProfileId == g.Key).Sum(x => x.BinCount)),
                "Acknowledged allocations exceed the observed receiving quantity for a variety.");
        }
        else
        {
            var destination = intent.Destination;
            Require(destination != null && destination.WarehouseId == receipt.WarehouseId && destination.RoomId == receipt.RoomId
                && await db.Rooms.AnyAsync(x => x.Id == destination.RoomId && x.WarehouseId == destination.WarehouseId
                    && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Select the receipt's active unsealed destination room.");
            foreach (var item in intent.Allocations)
            {
                var ack = acks.SingleOrDefault(x => x.Id == item.Id);
                Require(ack != null && item.Quantity <= ack.Quantity - ack.Placements.Sum(x => x.Quantity), "Placement exceeds receipt-held custody.");
                var original = dispatch.Single(x => x.Id == ack!.DispatchMovementId);
                var source = original.SourceSegment!;
                var identity = Identity(original);
                var appIds = source.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray();
                var applications = await db.RoomTreatmentApplications.Where(x => appIds.Contains(x.Id))
                    .Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId, x.RoomId)).ToArrayAsync(ct);
                var treatment = InventoryEffectiveTreatment.Read(original.TreatmentSignatureSnapshot, original.TreatmentStateSnapshot, appIds, applications);
                Require(treatment.State is "Untreated" or "Confirmed", "Dispatch treatment must be proven before placement.");
                var evidence = (await new InventoryEvidenceLoader(db).LoadAsync(new(destination!.WarehouseId, [destination.RoomId]), now, ct))
                    .Positions.SingleOrDefault(x => x.Identity.Key == identity.Key);
                var before = evidence?.AuthoritativeQuantity ?? 0;
                if (evidence != null)
                {
                    var result = InventoryAvailabilityResolver.Resolve(evidence, new());
                    Require(result.IsOperable, "Destination inventory needs review before placement.");
                    await NormalizePositionAsync(db, factory, c, evidence, result, now, attempt, ct);
                }
                var target = await factory.CurrentAsync(identity, destination.WarehouseId, destination.RoomId, treatment.Signature,
                    treatment.State, original.ReceiptId, treatment.ApplicationIds, now, ct);
                Credit(target, item.Quantity, now);
                var key = c.OperationKey + ":" + item.Id;
                var credit = Ledger(c, identity, destination.WarehouseId, destination.RoomId, item.Quantity, before, key, now);
                credit.InterCrewTransfer = transfer; credit.AdjustmentType = InterCrewTransferAdjustmentTypes.Receive;
                var move = Move(c, identity, new(source, item.Quantity, treatment), target, null, destination.RoomId, key, now, "InterCrewReceive");
                move.InterCrewTransfer = transfer;
                var placement = new ReceiptCustodyPlacement
                {
                    Acknowledgment = ack!,
                    Quantity = item.Quantity,
                    OperationKey = c.OperationKey,
                    InventoryAdjustment = credit,
                    Movement = move,
                    PlacedAt = now
                };
                db.ReceiptCustodyPlacements.Add(placement);
                await db.SaveChangesAsync(ct);
                Require(await PhysicalAsync(db, identity, destination.WarehouseId, destination.RoomId, ct) == before + item.Quantity,
                    "Receipt placement did not conserve room quantity.");
                effects.Add(new($"receipt:{receipt.Id}:{item.Id}", ack!.Quantity - ack.Placements.Sum(x => x.Quantity) + item.Quantity,
                    ack.Quantity - ack.Placements.Sum(x => x.Quantity), item.Quantity, transfer.Id, [credit.Id], [move.Id]));
            }
            transfer.DestinationWarehouseId = destination!.WarehouseId; transfer.DestinationRoomId = destination.RoomId;
        }
        transfer.BinsReceived = acks.Sum(x => x.Quantity);
        transfer.VarianceBins = transfer.BinsReceived - transfer.BinsLoaded;
        transfer.ConcurrencyVersion++; receipt.ConcurrencyVersion++; receipt.UpdatedAt = now;
        var placed = acks.Sum(x => x.Placements.Sum(p => p.Quantity));
        Require(placed <= transfer.BinsReceived && transfer.BinsReceived <= transfer.BinsLoaded, "Receipt custody conservation failed.");
        if (placed == transfer.BinsLoaded && transfer.BinsReceived == transfer.BinsLoaded)
        {
            Require(receipt.BinCount == transfer.BinsLoaded, "Observed receipt quantity still contains an unresolved discrepancy.");
            transfer.Status = InterCrewTransferStatuses.Received;
            transfer.ReceivedAt = now; transfer.ReceivedByUserId = c.ActorId; transfer.ReceiveOperationKey = c.OperationKey;
            receipt.TransferCompletedAt = now;
        }
        AddAudit(db, c, "CanonicalReceiptCustody", receipt.Id.ToString(), new { Acknowledged = beforeAcknowledged, Placed = beforePlaced },
            new { Acknowledged = transfer.BinsReceived, Placed = placed, Unresolved = transfer.BinsLoaded - transfer.BinsReceived, transfer.Status }, now);
        await db.SaveChangesAsync(ct);
        return effects.ToImmutable();
    }
}
