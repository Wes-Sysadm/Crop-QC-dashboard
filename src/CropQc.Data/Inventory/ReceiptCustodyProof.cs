using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public static class ReceiptCustodyProof
{
    public static IQueryable<ReceiptCustodyAcknowledgment> WithCustodyEvidence(this IQueryable<ReceiptCustodyAcknowledgment> rows) => rows
        .Include(x => x.Placements).ThenInclude(x => x.Movement)
        .Include(x => x.Placements).ThenInclude(x => x.InventoryAdjustment)
        .Include(x => x.Reversals).ThenInclude(x => x.Movement)
        .Include(x => x.Reversals).ThenInclude(x => x.InventoryAdjustment);

    public static bool Valid(ReceiptCustodyAcknowledgment ack)
    {
        if (ack.Quantity <= 0 || ack.NetQuantity < 0 || ack.PlacedQuantity < 0 || ack.HeldQuantity < 0
            || ack.Placements.Any(x => x.Quantity <= 0)) return false;
        foreach (var reversal in ack.Reversals)
        {
            if (reversal.Quantity <= 0 || reversal.AcknowledgmentId != ack.Id || reversal.ActorId <= 0
                || string.IsNullOrWhiteSpace(reversal.Reason) || string.IsNullOrWhiteSpace(reversal.OperationKey)) return false;
            if (reversal.PlacementId == null)
            {
                if (reversal.InventoryAdjustmentId != null || reversal.MovementId != null) return false;
                continue;
            }
            var placement = ack.Placements.SingleOrDefault(x => x.Id == reversal.PlacementId);
            var ledger = reversal.InventoryAdjustment;
            var move = reversal.Movement;
            if (placement == null || ledger == null || move == null
                || ack.Reversals.Where(x => x.PlacementId == placement.Id).Sum(x => x.Quantity) > placement.Quantity
                || ledger.InterCrewTransferId != ack.InterCrewTransferId || ledger.ChangeAmount != -reversal.Quantity
                || ledger.WarehouseId != placement.InventoryAdjustment.WarehouseId || ledger.RoomId != placement.InventoryAdjustment.RoomId
                || ledger.AdjustmentType != "ReceiptPlacementReversal"
                || move.InterCrewTransferId != ack.InterCrewTransferId || move.BinCount != reversal.Quantity
                || move.MovementType != "ReceiptPlacementReversal" || move.ReversesTreatmentLineageMovementId != placement.MovementId
                || move.SourceSegmentId != placement.Movement.DestinationSegmentId || move.SourceRoomId != placement.Movement.DestinationRoomId
                || move.DestinationRoomId != null || move.DestinationSegmentId != null
                || move.ReceiptId != placement.Movement.ReceiptId || move.IdentityKey != placement.Movement.IdentityKey
                || move.TreatmentSignatureSnapshot != placement.Movement.TreatmentSignatureSnapshot
                || move.TreatmentStateSnapshot != placement.Movement.TreatmentStateSnapshot
                || ledger.CreatedByUserId != reversal.ActorId || move.CreatedByUserId != reversal.ActorId
                || ledger.CreatedAt != reversal.ReversedAt || move.CreatedAt != reversal.ReversedAt) return false;
        }
        return true;
    }
}
