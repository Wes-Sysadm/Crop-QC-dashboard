namespace CropQc.Data.Entities;

public sealed class InventoryIdentityCorrection
{
    public Guid Id { get; set; }
    public required string OperationKey { get; set; }
    public int SourceCropYear { get; set; }
    public int? SourceGrowerLotId { get; set; }
    public GrowerLot? SourceGrowerLot { get; set; }
    public int SourceFruitProfileId { get; set; }
    public FruitProfile SourceFruitProfile { get; set; } = null!;
    public int TargetCropYear { get; set; }
    public int TargetGrowerLotId { get; set; }
    public GrowerLot TargetGrowerLot { get; set; } = null!;
    public int TargetFruitProfileId { get; set; }
    public FruitProfile TargetFruitProfile { get; set; } = null!;
    public long? CorrectedReceiptId { get; set; }
    public Receipt? CorrectedReceipt { get; set; }
    public Guid? ReceiptInventoryOverrideId { get; set; }
    public ReceiptInventoryOverride? ReceiptInventoryOverride { get; set; }
    public required string Reason { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public required string SourceIdentitySnapshotJson { get; set; }
    public required string TargetIdentitySnapshotJson { get; set; }
    public int ExpectedAdjustmentCount { get; set; }
    public int ExpectedTreatmentMovementCount { get; set; }
    public bool IsComplete { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
    public ICollection<TreatmentLineageMovement> TreatmentLineageMovements { get; } = new List<TreatmentLineageMovement>();
}
