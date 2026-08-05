namespace CropQc.Data.Entities;

public sealed class Receipt
{
    public long Id { get; set; }
    public int CropYear { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string CompuTechReceiptId { get; set; }
    public string ReceiptType { get; set; } = "Truck receipt";
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int FruitProfileId { get; set; }
    public FruitProfile FruitProfile { get; set; } = null!;
    public int? GrowerLotId { get; set; }
    public GrowerLot? GrowerLot { get; set; }
    public int? CanonicalOrchardBlockId { get; set; }
    public CanonicalOrchardBlock? CanonicalOrchardBlock { get; set; }
    public string? GrowerNumber { get; set; }
    public string? PoolStart { get; set; }
    public required string GrowerName { get; set; }
    public required string LotCode { get; set; }
    public int BinCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long ConcurrencyVersion { get; set; }
    public bool IsTestData { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public string? DeleteReason { get; set; }
    public ICollection<QcSample> Samples { get; } = new List<QcSample>();
    public ICollection<QcPhoto> Photos { get; } = new List<QcPhoto>();
    public ICollection<QcSummaryEmailLog> SummaryEmailLogs { get; } = new List<QcSummaryEmailLog>();
    public ICollection<RoomDepletion> RoomDepletions { get; } = new List<RoomDepletion>();
    public ICollection<RoomInventoryAdjustment> RoomInventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
    public ICollection<ReceiptInventoryOverride> InventoryOverrides { get; } = new List<ReceiptInventoryOverride>();
}

public static class ReceiptInventoryOverrideActionTypes
{
    public const string QuantityCorrection = "QuantityCorrection";
    public const string InventoryReclassification = "InventoryReclassification";
    public const string VoidReceipt = "VoidReceipt";
}

public sealed class ReceiptInventoryOverride
{
    public Guid Id { get; set; }
    public long ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public required string ActionType { get; set; }
    public int OldReceiptBinCount { get; set; }
    public int NewReceiptBinCount { get; set; }
    public int InventoryDelta { get; set; }
    public int CurrentInventoryBefore { get; set; }
    public int CurrentInventoryAfter { get; set; }
    public int AdministratorUserId { get; set; }
    public User AdministratorUser { get; set; } = null!;
    public required string Reason { get; set; }
    public required string OperationKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool NegativeInventoryAcknowledged { get; set; }
    public string? VoidConfirmationDetails { get; set; }
    public required string BeforeReceiptSnapshotJson { get; set; }
    public required string AfterReceiptSnapshotJson { get; set; }
    public required string AffectedInventorySnapshotJson { get; set; }
    public int ExpectedAdjustmentCount { get; set; }
    public bool IsComplete { get; set; }
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
}

public sealed class RoomDepletion
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int FruitProfileId { get; set; }
    public FruitProfile FruitProfile { get; set; } = null!;
    public required string GrowerName { get; set; }
    public required string LotCode { get; set; }
    public int BinCountDepleted { get; set; }
    public string? Destination { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset DepletedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public int? VoidedByUserId { get; set; }
    public User? VoidedByUser { get; set; }
    public string? VoidReason { get; set; }
}

public sealed class RoomInventoryAdjustment
{
    public long Id { get; set; }
    public int? CropYear { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? RoomDepletionId { get; set; }
    public RoomDepletion? RoomDepletion { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int? GrowerLotId { get; set; }
    public GrowerLot? GrowerLot { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public required string GrowerName { get; set; }
    public required string LotNumber { get; set; }
    public string? PoolStart { get; set; }
    public string? VarietyCode { get; set; }
    public int? OldBinCount { get; set; }
    public int ChangeAmount { get; set; }
    public int NewBinCount { get; set; }
    public required string AdjustmentType { get; set; }
    public string? Source { get; set; }
    public string? SourceRoomCode { get; set; }
    public string? SourceSubLocation { get; set; }
    public string? InventoryStatus { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset AdjustmentAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int InventoryInvariantVersion { get; set; }
    public string? InventoryOperationKey { get; set; }
    public long? RoomTransferId { get; set; }
    public RoomTransfer? RoomTransfer { get; set; }
    public Guid? ReceiptInventoryOverrideId { get; set; }
    public ReceiptInventoryOverride? ReceiptInventoryOverride { get; set; }
    public long? ActualRunId { get; set; }
    public ActualRun? ActualRun { get; set; }
    public long? ActualRunRevisionId { get; set; }
    public ActualRunRevision? ActualRunRevision { get; set; }
}

public sealed class RoomTransfer
{
    public long Id { get; set; }
    public required string OperationKey { get; set; }
    public int SourceWarehouseId { get; set; }
    public Warehouse SourceWarehouse { get; set; } = null!;
    public int SourceRoomId { get; set; }
    public Room SourceRoom { get; set; } = null!;
    public int DestinationWarehouseId { get; set; }
    public Warehouse DestinationWarehouse { get; set; } = null!;
    public int DestinationRoomId { get; set; }
    public Room DestinationRoom { get; set; } = null!;
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public required string GrowerName { get; set; }
    public required string LotNumber { get; set; }
    public string? PoolStart { get; set; }
    public string? VarietyCode { get; set; }
    public string? InventoryStatus { get; set; }
    public int BinCount { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset TransferredAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsReversed { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public int? ReversedByUserId { get; set; }
    public User? ReversedByUser { get; set; }
    public string? ReverseReason { get; set; }
    public long? ReversesRoomTransferId { get; set; }
    public RoomTransfer? ReversesRoomTransfer { get; set; }
    public RoomTransfer? ReversalTransfer { get; set; }
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
}

public sealed class BinsRunEntry
{
    public long Id { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? SourceInventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment? SourceInventoryAdjustment { get; set; }
    public long InventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment InventoryAdjustment { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public GrowerLot? GrowerLot { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public required string GrowerName { get; set; }
    public required string LotNumber { get; set; }
    public string? PoolStart { get; set; }
    public string? VarietyCode { get; set; }
    public string? InventoryStatus { get; set; }
    public int PreviousAvailableBins { get; set; }
    public int BinsRun { get; set; }
    public int NewAvailableBins { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsReconciled { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public int? ReconciledByUserId { get; set; }
    public User? ReconciledByUser { get; set; }
    public bool IsReversed { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public int? ReversedByUserId { get; set; }
    public User? ReversedByUser { get; set; }
    public string? ReverseReason { get; set; }
    public long? ActualRunId { get; set; }
    public ActualRun? ActualRun { get; set; }
    public long? ActualRunRevisionId { get; set; }
    public ActualRunRevision? ActualRunRevision { get; set; }
    public string TransactionType { get; set; } = ActualRunTransactionTypes.Legacy;
    public long? ReversesBinsRunEntryId { get; set; }
    public BinsRunEntry? ReversesBinsRunEntry { get; set; }
    public bool IsOverdrawOverride { get; set; }
    public int? OverrideAvailableBins { get; set; }
    public int? OverrideRequestedBins { get; set; }
    public int? OverrideShortageBins { get; set; }
    public string? OverrideReason { get; set; }
    public int? OverrideApprovedByUserId { get; set; }
    public User? OverrideApprovedByUser { get; set; }
    public DateTimeOffset? OverrideApprovedAt { get; set; }
    public int? ReportingFacilityWarehouseId { get; set; }
    public Warehouse? ReportingFacilityWarehouse { get; set; }
    public string? ReportingFacilityCodeSnapshot { get; set; }
    public string? ReportingFacilityAssignmentSource { get; set; }
    public int? ReportingFacilityAssignedByUserId { get; set; }
    public User? ReportingFacilityAssignedByUser { get; set; }
    public DateTimeOffset? ReportingFacilityAssignedAt { get; set; }
    public string? ProductionTypeSnapshot { get; set; }
    public bool? IsOrganicSnapshot { get; set; }
    public string? GrowerNumberSnapshot { get; set; }
    public int? ReportingCropYearSnapshot { get; set; }
    public int? ReportingFruitProfileIdSnapshot { get; set; }
    public string? ReportingVarietyCodeSnapshot { get; set; }
}

public static class RunFacilityAssignmentSources
{
    public const string Employment = "Employment";
    public const string SharedSelection = "SharedSelection";
    public const string HistoricalBackfill = "HistoricalBackfill";
}

public static class ActualRunStatuses
{
    public const string Active = "Active";
    public const string Canceled = "Canceled";
}

public static class ActualRunRevisionTypes
{
    public const string Create = "Create";
    public const string Edit = "Edit";
    public const string Cancel = "Cancel";
}

public static class ActualRunTransactionTypes
{
    public const string Legacy = "Legacy";
    public const string Depletion = "Depletion";
    public const string Reversal = "Reversal";
}

public static class ActualRunOverrideStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Superseded = "Superseded";
}

public sealed class ActualRun
{
    public long Id { get; set; }
    public long? RunProjectionId { get; set; }
    public RunProjection? RunProjection { get; set; }
    public required string Status { get; set; }
    public int CurrentRevisionNumber { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public DateTimeOffset RunAt { get; set; }
    public string? Notes { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? CanceledByUserId { get; set; }
    public User? CanceledByUser { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? CancellationReason { get; set; }
    public int? RunFacilityWarehouseId { get; set; }
    public Warehouse? RunFacilityWarehouse { get; set; }
    public string? RunFacilityCodeSnapshot { get; set; }
    public string? RunFacilityAssignmentSource { get; set; }
    public int? RunFacilityAssignedByUserId { get; set; }
    public User? RunFacilityAssignedByUser { get; set; }
    public DateTimeOffset? RunFacilityAssignedAt { get; set; }
    public ICollection<ActualRunRevision> Revisions { get; } = new List<ActualRunRevision>();
    public ICollection<BinsRunEntry> Entries { get; } = new List<BinsRunEntry>();
    public ICollection<RunExpectation> Expectations { get; } = new List<RunExpectation>();
    public ICollection<PackoutRun> PackoutRuns { get; } = new List<PackoutRun>();
}

public sealed class ActualRunRevision
{
    public long Id { get; set; }
    public long ActualRunId { get; set; }
    public ActualRun ActualRun { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public required string OperationType { get; set; }
    public required string OperationKey { get; set; }
    public bool IsCurrent { get; set; }
    public string? Reason { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<BinsRunEntry> Entries { get; } = new List<BinsRunEntry>();
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
    public RunExpectation? RunExpectation { get; set; }
}

public sealed class ActualRunOverrideRequest
{
    public long Id { get; set; }
    public long? ActualRunId { get; set; }
    public ActualRun? ActualRun { get; set; }
    public long? RunProjectionId { get; set; }
    public RunProjection? RunProjection { get; set; }
    public required string OperationType { get; set; }
    public required string OperationKey { get; set; }
    public required string Status { get; set; }
    public long? ExpectedConcurrencyVersion { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public string? Notes { get; set; }
    public int RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalReason { get; set; }
    public int? RunFacilityWarehouseId { get; set; }
    public Warehouse? RunFacilityWarehouse { get; set; }
    public string? RunFacilityCodeSnapshot { get; set; }
    public string? RunFacilityAssignmentSource { get; set; }
    public ICollection<ActualRunOverrideRequestLine> Lines { get; } = new List<ActualRunOverrideRequestLine>();
}

public sealed class ActualRunOverrideRequestLine
{
    public long Id { get; set; }
    public long ActualRunOverrideRequestId { get; set; }
    public ActualRunOverrideRequest ActualRunOverrideRequest { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public required string GrowerName { get; set; }
    public required string LotNumber { get; set; }
    public string? PoolStart { get; set; }
    public required string VarietyCode { get; set; }
    public string? InventoryStatus { get; set; }
    public int AvailableBins { get; set; }
    public int RequestedBins { get; set; }
    public int ShortageBins { get; set; }
    public long? RunProjectionSourceId { get; set; }
}

public sealed class QcSample
{
    public long Id { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public int SampleTypeId { get; set; }
    public SampleType SampleType { get; set; } = null!;
    public int SampleSequenceNumber { get; set; } = 1;
    public required string Status { get; set; }
    public required string StarchStatus { get; set; }
    public required string PhotoStatus { get; set; }
    public required string EmailStatus { get; set; }
    public string DefectInspectionStatus { get; set; } = DefectInspectionStatuses.NoDefectsFound;
    public int? TakenByUserId { get; set; }
    public User? TakenByUser { get; set; }
    public int? QcStationId { get; set; }
    public QcStation? QcStation { get; set; }
    public int? ActualSampleSize { get; set; }
    public string? Notes { get; set; }
    public int? FieldSampleFruitProfileId { get; set; }
    public FruitProfile? FieldSampleFruitProfile { get; set; }
    public int? CanonicalOrchardBlockId { get; set; }
    public CanonicalOrchardBlock? CanonicalOrchardBlock { get; set; }
    public string? FieldSampleGrowerName { get; set; }
    public string? FieldSampleGrowerNumber { get; set; }
    public string? FieldSampleOriginalBlockName { get; set; }
    public string? FieldSampleBlockResolution { get; set; }
    public long FieldSampleAutosaveVersion { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsTestData { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public string? DeleteReason { get; set; }
    public ICollection<QcFruitReading> FruitReadings { get; } = new List<QcFruitReading>();
    public ICollection<QcPhoto> Photos { get; } = new List<QcPhoto>();
    public ICollection<QcSummaryEmailLog> SummaryEmailLogs { get; } = new List<QcSummaryEmailLog>();

    public string GetDisplayReceiptId() =>
        Receipt is null
            ? $"FIELD-{Id}"
            : SampleSequenceNumber <= 1
                ? Receipt.CompuTechReceiptId
                : $"{Receipt.CompuTechReceiptId}({SampleSequenceNumber})";
}

public static class DefectInspectionStatuses
{
    public const string NoDefectsFound = "No defects found";
    public const string DefectsFound = "Defects found";

    public static string FromDefectCount(int count) =>
        count > 0 ? DefectsFound : NoDefectsFound;
}

public sealed class QcFruitReading
{
    public long Id { get; set; }
    public long QcSampleId { get; set; }
    public QcSample QcSample { get; set; } = null!;
    public int RowNumber { get; set; }
    public decimal? Pressure1Lbs { get; set; }
    public string? Pressure1Source { get; set; }
    public decimal? Pressure2Lbs { get; set; }
    public string? Pressure2Source { get; set; }
    public decimal? WeightGrams { get; set; }
    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public int? StarchScaleValueId { get; set; }
    public StarchScaleValue? StarchScaleValue { get; set; }
    public int? SizeCategory { get; set; }
    public required string SizeStatus { get; set; }
    public bool DefectsInspected { get; set; }
    public long FieldVersion { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<QcFruitDefect> Defects { get; } = new List<QcFruitDefect>();
}

public sealed class QcFruitDefect
{
    public long Id { get; set; }
    public long QcFruitReadingId { get; set; }
    public QcFruitReading QcFruitReading { get; set; } = null!;
    public int DefectTypeId { get; set; }
    public DefectType DefectType { get; set; } = null!;
    public string? Notes { get; set; }
}

public sealed class QcPhoto
{
    public long Id { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? QcSampleId { get; set; }
    public QcSample? QcSample { get; set; }
    public required string PhotoType { get; set; }
    public required string PhotoSource { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string StorageProvider { get; set; } = "Legacy";
    public string? DriveId { get; set; }
    public string? FileId { get; set; }
    public string? FolderId { get; set; }
    public required string SharePointDriveId { get; set; }
    public required string SharePointItemId { get; set; }
    public string? WebUrl { get; set; }
    public int? CapturedByUserId { get; set; }
    public User? CapturedByUser { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
    public string? DeleteReason { get; set; }
}

public sealed class QcSummaryEmailLog
{
    public long Id { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? QcSampleId { get; set; }
    public QcSample? QcSample { get; set; }
    public required string FromAddress { get; set; }
    public required string ToAddress { get; set; }
    public string? ReplyToAddress { get; set; }
    public required string Subject { get; set; }
    public required string Status { get; set; }
    public string? MessageId { get; set; }
    public int? SentByUserId { get; set; }
    public User? SentByUser { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public bool IsResend { get; set; }
    public string? ResendReason { get; set; }
    public bool IsOverride { get; set; }
    public string? OverrideReason { get; set; }
    public string? MissingItemsSnapshot { get; set; }
    public string? EmailBodySnapshot { get; set; }
    public string? ReportSnapshotReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class QcStation
{
    public int Id { get; set; }
    public required string StationCode { get; set; }
    public required string Name { get; set; }
    public string StationName { get; set; } = "";
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public string? WarehouseCode { get; set; }
    public string? Description { get; set; }
    public string? DeviceIdentifier { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ApiKeyHash { get; set; }
    public string? ApiKeyLastFour { get; set; }
    public DateTimeOffset? ApiKeyCreatedAt { get; set; }
    public DateTimeOffset? ApiKeyRotatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? LastSeenIp { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public string? Notes { get; set; }
    public ICollection<QcSample> Samples { get; } = new List<QcSample>();
}

public sealed class OfflineSyncItem
{
    public long Id { get; set; }
    public int? QcStationId { get; set; }
    public QcStation? QcStation { get; set; }
    public required string EntityName { get; set; }
    public required string LocalEntityId { get; set; }
    public long? ServerEntityId { get; set; }
    public required string SyncStatus { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}
