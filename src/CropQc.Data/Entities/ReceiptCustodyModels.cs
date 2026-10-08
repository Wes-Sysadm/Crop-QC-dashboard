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
    public ICollection<ReceiptCustodyReversal> Reversals { get; } = new List<ReceiptCustodyReversal>();
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int NetQuantity => Quantity - Reversals.Where(x => x.PlacementId == null).Sum(x => x.Quantity);
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int PlacedQuantity => Placements.Sum(x => x.Quantity) - Reversals.Where(x => x.PlacementId != null).Sum(x => x.Quantity);
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int HeldQuantity => NetQuantity - PlacedQuantity;
}

/// <summary>Immutable compensation. A null placement reverses only held acknowledgement;
/// a placement link proves an exact room debit returning that quantity to receipt-held custody.</summary>
public sealed class ReceiptCustodyReversal
{
    public long Id { get; set; }
    public long AcknowledgmentId { get; set; }
    public ReceiptCustodyAcknowledgment Acknowledgment { get; set; } = null!;
    public long? PlacementId { get; set; }
    public ReceiptCustodyPlacement? Placement { get; set; }
    public int Quantity { get; set; }
    public required string OperationKey { get; set; }
    public required string Reason { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset ReversedAt { get; set; }
    public long? InventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment? InventoryAdjustment { get; set; }
    public long? MovementId { get; set; }
    public TreatmentLineageMovement? Movement { get; set; }
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
