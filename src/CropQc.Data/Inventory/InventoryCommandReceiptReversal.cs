using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task ReverseReceiptCustodyAsync(CropQcDbContext db, InventoryCommand command,
        List<ReceiptCustodyAcknowledgment> acknowledgments, ImmutableArray<InventoryCommandEffect>.Builder effects,
        DateTimeOffset now, CancellationToken ct)
    {
        Require(command.ReceiptCustody!.Destination == null && !string.IsNullOrWhiteSpace(command.Reason)
            && command.Reason.Length <= 1000, "A correction reason is required; reversal does not select a physical destination.");
        foreach (var item in command.ReceiptCustody.Allocations)
        {
            var placement = command.Kind == InventoryCommandKind.ReverseReceiptPlacement
                ? acknowledgments.SelectMany(x => x.Placements).SingleOrDefault(x => x.Id == item.Id) : null;
            var ack = acknowledgments.SingleOrDefault(x => x.Id == (placement?.AcknowledgmentId ?? item.Id));
            Require(ack != null, "Original acknowledgement was not found on this receipt.");
            var reversal = new ReceiptCustodyReversal
            {
                Acknowledgment = ack!,
                Placement = placement,
                Quantity = item.Quantity,
                OperationKey = command.OperationKey,
                Reason = command.Reason.Trim(),
                ActorId = command.ActorId,
                ReversedAt = now
            };
            var before = ack!.HeldQuantity;
            if (command.Kind == InventoryCommandKind.ReverseReceiptAcknowledgment)
            {
                Require(item.Quantity <= before,
                    "Only receipt-held bins can have acknowledgement reversed. Undo a proven untouched placement first; moved, treated or consumed bins require their explicit current-custody resolution workflow.");
                db.ReceiptCustodyReversals.Add(reversal);
                await db.SaveChangesAsync(ct);
                effects.Add(new($"receipt:{ack.ReceiptId}:{ack.Id}", before, ack.HeldQuantity, item.Quantity, ack.InterCrewTransferId, [], []));
                continue;
            }
            Require(placement != null && item.Quantity <= placement.Quantity
                - ack.Reversals.Where(x => x.PlacementId == placement.Id).Sum(x => x.Quantity), "Reversal exceeds the remaining original placement.");
            var original = placement!.Movement;
            var segment = await db.TreatmentLineageSegments.Include(x => x.Applications)
                .SingleOrDefaultAsync(x => x.Id == original.DestinationSegmentId, ct);
            const string dependent = "Placement has subsequent inventory, treatment or identity dependencies. Resolve those through the explicit room-move, treatment-reversal or consumption-correction workflow; acknowledgement reversal cannot move physical bins back to the dispatch source.";
            Require(segment != null && segment.Disposition == "Current" && segment.CurrentBins >= item.Quantity
                && segment.ReceiptId == original.ReceiptId && segment.IdentityKey == original.IdentityKey
                && segment.TreatmentSignature == original.TreatmentSignatureSnapshot && segment.TreatmentState == original.TreatmentStateSnapshot, dependent);
            var knownReversals = ack.Reversals.Where(x => x.PlacementId != null).Select(x => x.MovementId).ToArray();
            Require(!await db.TreatmentLineageMovements.AnyAsync(x => x.SourceSegmentId == segment!.Id
                && x.SourceRoomId != null && !knownReversals.Contains(x.Id), ct), dependent);
            var source = await db.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.Id == ack.DispatchMovement.SourceSegmentId, ct);
            Require(source.Applications.Select(x => x.RoomTreatmentApplicationId).Order().SequenceEqual(segment!.Applications.Select(x => x.RoomTreatmentApplicationId).Order()), dependent);
            Require(!await db.RoomTreatmentApplications.AnyAsync(x => segment.Applications.Select(a => a.RoomTreatmentApplicationId).Contains(x.Id)
                && x.ReversedAt != null, ct), dependent);
            Require(await db.Rooms.AnyAsync(x => x.Id == segment.RoomId && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct),
                "The current placement room must be active and unsealed before moving bins back to receipt-held custody.");
            var identity = CanonicalIdentityMap.Historical(segment);
            var evidence = (await new InventoryEvidenceLoader(db).LoadAsync(new(segment.WarehouseId, [segment.RoomId]), now, ct))
                .Positions.SingleOrDefault(x => x.Identity.Key == identity.Key);
            Require(evidence != null && InventoryAvailabilityResolver.Resolve(evidence, new()).IsOperable
                && evidence.AuthoritativeQuantity >= item.Quantity && evidence.Projections.Any(x => x.Id == segment.Id), dependent);
            var quantityBefore = evidence!.AuthoritativeQuantity;
            var key = command.OperationKey + ":" + placement.Id;
            var debit = Ledger(command, identity, segment.WarehouseId, segment.RoomId, -item.Quantity, quantityBefore, key, now);
            debit.InterCrewTransferId = ack.InterCrewTransferId; debit.AdjustmentType = "ReceiptPlacementReversal";
            var movement = Move(command, identity, new(segment, item.Quantity), null, segment.RoomId, null, key, now, "ReceiptPlacementReversal");
            movement.InterCrewTransferId = ack.InterCrewTransferId; movement.ReversesTreatmentLineageMovementId = original.Id;
            segment.CurrentBins -= item.Quantity; segment.ConcurrencyVersion++; segment.UpdatedAt = now;
            if (segment.CurrentBins == 0) CanonicalProjectionFactory.Retire(segment, command.OperationKey, now);
            reversal.InventoryAdjustment = debit; reversal.Movement = movement;
            db.ReceiptCustodyReversals.Add(reversal);
            await db.SaveChangesAsync(ct);
            Require(await PhysicalAsync(db, identity, segment.WarehouseId, segment.RoomId, ct) == quantityBefore - item.Quantity,
                "Placement reversal did not conserve room quantity.");
            effects.Add(new($"receipt:{ack.ReceiptId}:placement:{placement.Id}", quantityBefore, quantityBefore - item.Quantity,
                item.Quantity, ack.InterCrewTransferId, [debit.Id], [movement.Id]));
        }
    }
}
