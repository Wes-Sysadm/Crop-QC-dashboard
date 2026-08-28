namespace CropQc.Data.Entities;

public static class OutsideWarehouseTransferAdjustmentTypes
{
    public const string Transfer = "OutsideWarehouseTransfer";
    public const string Reversal = "OutsideWarehouseTransferReversal";
}

public static class TransferCustodyGroups
{
    public const string WpDh = "WP_DH";
    public const string Ebs = "EBS";

    public static bool IsValid(string? value) => value is WpDh or Ebs;
    public static string Label(string value) => value == WpDh ? "WP / DH" : "EBS";
    public static bool ContainsWarehouse(string group, string? warehouseCode) => group switch
    {
        WpDh => warehouseCode is not null && (warehouseCode.Equals("WP", StringComparison.OrdinalIgnoreCase) || warehouseCode.Equals("DH", StringComparison.OrdinalIgnoreCase)),
        Ebs => warehouseCode is not null && warehouseCode.Equals("EBS", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

public static class InterCrewTransferStatuses
{
    public const string InTransit = "InTransit";
    public const string Received = "Received";
    public const string ReceivedNeedsReview = "ReceivedNeedsReview";
    public const string Reversed = "Reversed";
}

public static class InterCrewTransferAdjustmentTypes
{
    public const string Dispatch = "InterCrewTransferDispatch";
    public const string Receive = "InterCrewTransferReceive";
    public const string ReversalDestination = "InterCrewTransferReversalDestination";
    public const string ReversalSource = "InterCrewTransferReversalSource";
}

public sealed class InterCrewTransfer
{
    public long Id { get; set; }
    public required string OperationKey { get; set; }
    public int SourceWarehouseId { get; set; }
    public Warehouse SourceWarehouse { get; set; } = null!;
    public int SourceRoomId { get; set; }
    public Room SourceRoom { get; set; } = null!;
    public required string DestinationCustodyGroup { get; set; }
    public int? DestinationWarehouseId { get; set; }
    public Warehouse? DestinationWarehouse { get; set; }
    public int? DestinationRoomId { get; set; }
    public Room? DestinationRoom { get; set; }
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
    public int BinsLoaded { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public string? TruckLoadBolNumber { get; set; }
    public string? Notes { get; set; }
    public int LoadedByUserId { get; set; }
    public User LoadedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public required string Status { get; set; }
    public string? ReceiveOperationKey { get; set; }
    public int? BinsReceived { get; set; }
    public int? VarianceBins { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public int? ReceivedByUserId { get; set; }
    public User? ReceivedByUser { get; set; }
    public string? ReceivingNote { get; set; }
    public string? ReviewOperationKey { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public string? ReversalOperationKey { get; set; }
    public string? ReversalReason { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public int? ReversedByUserId { get; set; }
    public User? ReversedByUser { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
    public ICollection<TreatmentLineageMovement> TreatmentLineageMovements { get; } = new List<TreatmentLineageMovement>();
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
