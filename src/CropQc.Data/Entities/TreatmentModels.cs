namespace CropQc.Data.Entities;

public static class TreatmentLineageStates
{
    public const string Untreated = "Untreated";
    public const string Confirmed = "Confirmed";
    public const string Unknown = "Unknown";
}

public static class TreatmentLineageMovementTypes
{
    public const string Transfer = "Transfer";
    public const string TransferReversal = "TransferReversal";
    public const string InventoryLoss = "InventoryLoss";
    public const string InventoryLossReversal = "InventoryLossReversal";
    public const string BinsRun = "BinsRun";
    public const string BinsRunReversal = "BinsRunReversal";
    public const string ManualTrueUp = "ManualTrueUp";
    public const string ProcessorShipment = "ProcessorShipment";
    public const string ProcessorShipmentReversal = "ProcessorShipmentReversal";
    public const string OutsideWarehouseTransfer = "OutsideWarehouseTransfer";
    public const string OutsideWarehouseTransferReversal = "OutsideWarehouseTransferReversal";
    public const string InterCrewDispatch = "InterCrewDispatch";
    public const string InterCrewReceive = "InterCrewReceive";
    public const string InterCrewReversal = "InterCrewReversal";
}

public static class TreatmentApplicationLevels
{
    public const string Room = "Room";
    public const string Receiving = "Receiving";

    public static bool IsValid(string? value) =>
        string.Equals(value, Room, StringComparison.Ordinal)
        || string.Equals(value, Receiving, StringComparison.Ordinal);
}

public sealed class TreatmentChemical
{
    public int Id { get; set; }
    public required string ProductName { get; set; }
    public string? CommonName { get; set; }
    public required string Crop { get; set; }
    public string ApplicationLevel { get; set; } = TreatmentApplicationLevels.Room;
    public decimal Volume { get; set; }
    public required string Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public required string Currency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<RoomTreatmentApplication> Applications { get; } = new List<RoomTreatmentApplication>();
}

public sealed class RoomTreatmentApplication
{
    public long Id { get; set; }
    public required string OperationKey { get; set; }
    public int TreatmentChemicalId { get; set; }
    public TreatmentChemical TreatmentChemical { get; set; } = null!;
    public string ApplicationLevel { get; set; } = TreatmentApplicationLevels.Room;
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public DateTimeOffset AppliedAt { get; set; }
    public int AppliedByUserId { get; set; }
    public User AppliedByUser { get; set; } = null!;
    public string? Notes { get; set; }
    public int TotalBinsSnapshot { get; set; }
    public required string ProductNameSnapshot { get; set; }
    public string? CommonNameSnapshot { get; set; }
    public required string CropSnapshot { get; set; }
    public decimal VolumeSnapshot { get; set; }
    public required string UnitSnapshot { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public required string CurrencySnapshot { get; set; }
    public decimal EstimatedCostSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset? ReversedAt { get; set; }
    public int? ReversedByUserId { get; set; }
    public User? ReversedByUser { get; set; }
    public string? ReversalReason { get; set; }
    public ICollection<RoomTreatmentApplicationSource> Sources { get; } = new List<RoomTreatmentApplicationSource>();
    public ICollection<TreatmentLineageSegmentApplication> SegmentApplications { get; } = new List<TreatmentLineageSegmentApplication>();
    public ICollection<RoomTreatmentApplicationAttachment> Attachments { get; } = new List<RoomTreatmentApplicationAttachment>();
}

public sealed class RoomTreatmentApplicationAttachment
{
    public long Id { get; set; }
    public long RoomTreatmentApplicationId { get; set; }
    public RoomTreatmentApplication RoomTreatmentApplication { get; set; } = null!;
    public required string OperationKey { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public required string StorageProvider { get; set; }
    public string? DriveId { get; set; }
    public required string FileId { get; set; }
    public string? FolderId { get; set; }
    public required string StoragePath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
    public string? DeleteReason { get; set; }
}

public sealed class RoomTreatmentApplicationSource
{
    public long Id { get; set; }
    public long RoomTreatmentApplicationId { get; set; }
    public RoomTreatmentApplication RoomTreatmentApplication { get; set; } = null!;
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public required string IdentityKey { get; set; }
    public string? GrowerNumberSnapshot { get; set; }
    public required string GrowerNameSnapshot { get; set; }
    public required string LotNumberSnapshot { get; set; }
    public required string VarietyCodeSnapshot { get; set; }
    public required string ProductionTypeSnapshot { get; set; }
    public bool? IsOrganicSnapshot { get; set; }
    public string? InventoryStatusSnapshot { get; set; }
    public int BinsTreated { get; set; }
    public required string PriorTreatmentSignature { get; set; }
    public required string ResultTreatmentSignature { get; set; }
}

public sealed class TreatmentLineageSegment
{
    public long Id { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public required string IdentityKey { get; set; }
    public string? GrowerNumberSnapshot { get; set; }
    public required string GrowerNameSnapshot { get; set; }
    public required string LotNumberSnapshot { get; set; }
    public required string VarietyCodeSnapshot { get; set; }
    public required string ProductionTypeSnapshot { get; set; }
    public bool? IsOrganicSnapshot { get; set; }
    public string? InventoryStatusSnapshot { get; set; }
    public required string TreatmentState { get; set; }
    public required string TreatmentSignature { get; set; }
    public int CurrentBins { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public ICollection<TreatmentLineageSegmentApplication> Applications { get; } = new List<TreatmentLineageSegmentApplication>();
}

public sealed class TreatmentLineageSegmentApplication
{
    public long TreatmentLineageSegmentId { get; set; }
    public TreatmentLineageSegment TreatmentLineageSegment { get; set; } = null!;
    public long RoomTreatmentApplicationId { get; set; }
    public RoomTreatmentApplication RoomTreatmentApplication { get; set; } = null!;
    public int Sequence { get; set; }
}

public sealed class TreatmentLineageMovement
{
    public long Id { get; set; }
    public required string OperationKey { get; set; }
    public required string MovementType { get; set; }
    public long? SourceSegmentId { get; set; }
    public TreatmentLineageSegment? SourceSegment { get; set; }
    public long? DestinationSegmentId { get; set; }
    public TreatmentLineageSegment? DestinationSegment { get; set; }
    public int? SourceRoomId { get; set; }
    public Room? SourceRoom { get; set; }
    public int? DestinationRoomId { get; set; }
    public Room? DestinationRoom { get; set; }
    public required string IdentityKey { get; set; }
    public required string TreatmentStateSnapshot { get; set; }
    public required string TreatmentSignatureSnapshot { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public int BinCount { get; set; }
    public long? RoomTransferId { get; set; }
    public RoomTransfer? RoomTransfer { get; set; }
    public long? RoomInventoryLossId { get; set; }
    public RoomInventoryLoss? RoomInventoryLoss { get; set; }
    public long? BinsRunEntryId { get; set; }
    public BinsRunEntry? BinsRunEntry { get; set; }
    public long? ProcessorShipmentLineId { get; set; }
    public ProcessorShipmentLine? ProcessorShipmentLine { get; set; }
    public long? OutsideWarehouseTransferId { get; set; }
    public OutsideWarehouseTransfer? OutsideWarehouseTransfer { get; set; }
    public long? InterCrewTransferId { get; set; }
    public InterCrewTransfer? InterCrewTransfer { get; set; }
    public long? ReversesTreatmentLineageMovementId { get; set; }
    public TreatmentLineageMovement? ReversesTreatmentLineageMovement { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TreatmentLineageMovement> ReversalMovements { get; } = new List<TreatmentLineageMovement>();
}
