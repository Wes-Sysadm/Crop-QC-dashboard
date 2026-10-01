using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ReturnTransitAllocationAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, InventoryAvailabilityResult transit, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        var line = c.Lines.Single();
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == c.PhysicalParentId, ct);
        Require(transfer.Status == InterCrewTransferStatuses.InTransit && transfer.BinsReceived == null
            && transfer.ConcurrencyVersion == c.ExpectedTransferVersion, "In-transit load changed; reload before returning bins.", InventoryCommandStatus.Stale);
        Require(line.Source.Location.CustodyRecordId == transfer.Id, "Selected custody does not belong to this load.");
        var all = await db.TreatmentLineageMovements.Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
        var dispatch = all.SingleOrDefault(x => x.Id == c.DispatchMovementId && x.MovementType == "InterCrewDispatch" && x.ReversesTreatmentLineageMovementId == null);
        Require(dispatch?.SourceSegment != null, "Exact original dispatch allocation is missing.");
        var source = dispatch!.SourceSegment!;
        var appIds = source.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray();
        var applications = await db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id))
            .Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId)).ToArrayAsync(ct);
        var treatment = InventoryEffectiveTreatment.Read(source.TreatmentSignature, source.TreatmentState, appIds, applications);
        var remaining = dispatch.BinCount - all.Where(x => x.ReversesTreatmentLineageMovementId == dispatch.Id).Sum(x => x.BinCount);
        Require(line.Quantity <= remaining && line.Quantity <= transfer.BinsLoaded
            && InventoryStatusIdentity.NormalizeLineageKey(dispatch.IdentityKey) == transit.Identity.Key
            && InventoryStatusIdentity.NormalizeLineageKey(source.IdentityKey) == transit.Identity.Key
            && treatment.Signature == line.TreatmentSignature && source.TreatmentSignature == dispatch.TreatmentSignatureSnapshot
            && source.TreatmentState == dispatch.TreatmentStateSnapshot && source.RoomId == transfer.SourceRoomId && source.WarehouseId == transfer.SourceWarehouseId,
            "Dispatch identity, treatment or remaining allocation cannot support this return.");
        Require(await db.Rooms.AnyAsync(x => x.Id == source.RoomId && x.WarehouseId == source.WarehouseId && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct),
            "The return room is unavailable or sealed.");
        var loader = new InventoryEvidenceLoader(db);
        var completeTransit = await new InventoryAvailabilityResolver(loader).ResolveAsync(new(transfer.SourceWarehouseId, [], InventoryCustody.InTransit, transfer.Id),
            new(AllowedCustody: InventoryCustody.InTransit), now, ct);
        Require(completeTransit.Positions.All(x => x.IsOperable) && completeTransit.Positions.Sum(x => x.AuthoritativeQuantity) == transfer.BinsLoaded,
            "Whole-load custody is inconsistent.");
        var evidence = (await loader.LoadAsync(new(source.WarehouseId, [source.RoomId]), now, ct)).Positions.SingleOrDefault(x => x.Identity.Key == transit.Identity.Key);
        Require(evidence != null, "Source room identity is unavailable.");
        var destination = InventoryAvailabilityResolver.Resolve(evidence!, new());
        Require(destination.IsOperable, "Current source inventory cannot be proven for return.");
        await NormalizePositionAsync(db, factory, c, evidence!, destination, now, attempt, ct);
        var target = await factory.CurrentAsync(transit.Identity, source.WarehouseId, source.RoomId, treatment.Signature, treatment.State,
            source.ReceiptId, treatment.ApplicationIds, now, ct);
        Credit(target, line.Quantity, now);
        var credit = Ledger(c, transit.Identity, source.WarehouseId, source.RoomId, line.Quantity, destination.AuthoritativeQuantity, c.OperationKey + ":return", now);
        credit.InterCrewTransfer = transfer; credit.AdjustmentType = "TransitAllocationReturn";
        var reversal = Move(c, transit.Identity, new(source, line.Quantity, treatment), target, null, source.RoomId, c.OperationKey + ":return", now, "InterCrewReversal");
        reversal.InterCrewTransfer = transfer; reversal.ReversesTreatmentLineageMovementId = dispatch.Id;
        var before = new { transfer.BinsLoaded, transfer.ConcurrencyVersion, transfer.Status, transfer.ReceivingReceiptId };
        transfer.BinsLoaded -= line.Quantity; transfer.ConcurrencyVersion++;
        if (transfer.BinsLoaded == 0)
        {
            transfer.Status = InterCrewTransferStatuses.Reversed; transfer.ReversedAt = now; transfer.ReversedByUserId = c.ActorId;
            transfer.ReversalReason = c.Reason; transfer.ReversalOperationKey = c.OperationKey;
            if (transfer.ReceivingReceiptId is long matchedId)
            {
                var receipt = await db.Receipts.SingleAsync(x => x.Id == matchedId, ct);
                Require(c.ReceivingEvidence?.ReceiptId == matchedId && receipt.ConcurrencyVersion == c.ReceivingEvidence.ExpectedVersion
                    && receipt.IsTransferReceipt && !receipt.IsDeleted && receipt.TransferCompletedAt == null,
                    "The matched receipt changed; reload before cancelling this load.", InventoryCommandStatus.Stale);
                AddAudit(db, c, "CanonicalTruckReceiptUnlink", matchedId.ToString(), new { transfer.ReceivingReceiptId, receipt.ConcurrencyVersion },
                    new { ReceiptId = (long?)null, Version = receipt.ConcurrencyVersion + 1 }, now);
                receipt.ConcurrencyVersion++; receipt.UpdatedAt = now; transfer.ReceivingReceiptId = null;
            }
        }
        db.RoomInventoryAdjustments.Add(credit); db.TreatmentLineageMovements.Add(reversal);
        AddAudit(db, c, "CanonicalTransferAllocationReturn", transfer.Id.ToString(), before,
            new { transfer.BinsLoaded, transfer.ConcurrencyVersion, transfer.Status, transfer.ReceivingReceiptId, DispatchMovementId = dispatch.Id, line.Quantity }, now);
        await Stage("Movement", db, attempt, ct);
        await db.SaveChangesAsync(ct);
        var roomAfter = (await new InventoryAvailabilityResolver(loader).ResolveAsync(new(source.WarehouseId, [source.RoomId]), new(), now, ct)).Positions
            .Single(x => x.Identity.Key == transit.Identity.Key);
        Require(roomAfter.IsOperable && roomAfter.AuthoritativeQuantity == destination.AuthoritativeQuantity + line.Quantity
            && roomAfter.RawProjectionQuantity == roomAfter.AuthoritativeQuantity, "Returned inventory does not reconcile.");
        var transitAfter = await new InventoryAvailabilityResolver(loader).ResolveAsync(new(source.WarehouseId, [], InventoryCustody.InTransit, transfer.Id),
            new(AllowedCustody: InventoryCustody.InTransit), now, ct);
        if (transfer.BinsLoaded > 0)
            Require(transitAfter.Positions.All(x => x.IsOperable) && transitAfter.Positions.Sum(x => x.AuthoritativeQuantity) == transfer.BinsLoaded,
                "Remaining transit custody does not reconcile.");
        else
        {
            var history = await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
            Require(await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id).SumAsync(x => x.ChangeAmount, ct) == 0
                && history.Where(x => x.MovementType == "InterCrewDispatch" && x.ReversesTreatmentLineageMovementId == null)
                    .All(x => x.BinCount == history.Where(r => r.ReversesTreatmentLineageMovementId == x.Id).Sum(r => r.BinCount)),
                "Cancelled load retains ledger stock or an unreturned dispatch allocation.");
        }
        return [new(transit.PositionKey, transit.AuthoritativeQuantity, transit.AuthoritativeQuantity - line.Quantity, line.Quantity, transfer.Id, [credit.Id], [reversal.Id])];
    }
}
