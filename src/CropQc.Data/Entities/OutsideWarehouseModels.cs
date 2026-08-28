namespace CropQc.Data.Entities;

public static class OutsideWarehouseTransferAdjustmentTypes
{
    public const string Transfer = "OutsideWarehouseTransfer";
    public const string Reversal = "OutsideWarehouseTransferReversal";
}

public sealed class OutsideWarehouse
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<OutsideWarehouseTransfer> Transfers { get; } = new List<OutsideWarehouseTransfer>();
}

public sealed class OutsideWarehouseTransfer
{
    public long Id { get; set; }
    public required string OperationKey { get; set; }
    public int OutsideWarehouseId { get; set; }
    public OutsideWarehouse OutsideWarehouse { get; set; } = null!;
    public required string OutsideWarehouseCodeSnapshot { get; set; }
    public required string OutsideWarehouseNameSnapshot { get; set; }
    public string? OutsideWarehouseAddressSnapshot { get; set; }
    public int SourceWarehouseId { get; set; }
    public Warehouse SourceWarehouse { get; set; } = null!;
    public int SourceRoomId { get; set; }
    public Room SourceRoom { get; set; } = null!;
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? SourceInventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment? SourceInventoryAdjustment { get; set; }
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public string? GrowerNumberSnapshot { get; set; }
    public required string GrowerNameSnapshot { get; set; }
    public required string LotNumberSnapshot { get; set; }
    public required string VarietyCodeSnapshot { get; set; }
    public required string ProductionTypeSnapshot { get; set; }
    public bool? IsOrganicSnapshot { get; set; }
    public string? InventoryStatusSnapshot { get; set; }
    public required string TreatmentStateSnapshot { get; set; }
    public required string TreatmentSignatureSnapshot { get; set; }
    public required string TreatmentSummarySnapshot { get; set; }
    public int BinCount { get; set; }
    public DateTimeOffset TransferredAt { get; set; }
    public string? TruckLoadBolNumber { get; set; }
    public string? Notes { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsReversed { get; set; }
    public string? ReversalOperationKey { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public int? ReversedByUserId { get; set; }
    public User? ReversedByUser { get; set; }
    public string? ReverseReason { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
    public ICollection<TreatmentLineageMovement> TreatmentLineageMovements { get; } = new List<TreatmentLineageMovement>();
}
