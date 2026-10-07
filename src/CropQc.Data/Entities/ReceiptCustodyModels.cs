namespace CropQc.Data.Entities;

/// <summary>Immutable acknowledgement of an exact original dispatch allocation.
/// Quantity remains receipt-held until placements consume it. It is never a new inventory origin.</summary>
public sealed class ReceiptCustodyAcknowledgment
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public long InterCrewTransferId { get; set; }
    public InterCrewTransfer InterCrewTransfer { get; set; } = null!;
    public long DispatchMovementId { get; set; }
    public TreatmentLineageMovement DispatchMovement { get; set; } = null!;
    public int Quantity { get; set; }
    public required string OperationKey { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
    public ICollection<ReceiptCustodyPlacement> Placements { get; } = new List<ReceiptCustodyPlacement>();
}

/// <summary>Immutable relinquishment of receipt-held custody to an exact room ledger/movement pair.</summary>
public sealed class ReceiptCustodyPlacement
{
    public long Id { get; set; }
    public long AcknowledgmentId { get; set; }
    public ReceiptCustodyAcknowledgment Acknowledgment { get; set; } = null!;
    public int Quantity { get; set; }
    public required string OperationKey { get; set; }
    public long InventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment InventoryAdjustment { get; set; } = null!;
    public long MovementId { get; set; }
    public TreatmentLineageMovement Movement { get; set; } = null!;
    public DateTimeOffset PlacedAt { get; set; }
}
