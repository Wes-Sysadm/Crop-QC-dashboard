namespace CropQc.Data.Entities;

public static class RunProjectionStatuses
{
    public const string Draft = "Draft";
    public const string Ready = "Ready";
    public const string Superseded = "Superseded";
    public const string Converted = "Converted to Actual Run";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlySet<string> Editable =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Draft, Ready };
}

public static class RunProjectionModes
{
    public const string Inventory = "Inventory";
    public const string Preharvest = "Preharvest";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Inventory, Preharvest };
}

public static class RunProjectionSourceTypes
{
    public const string Inventory = "Inventory";
    public const string FieldSample = "FieldSample";
    public const string GrowerLot = "GrowerLot";
}

public static class RunProjectionQcSourceTypes
{
    public const string Automatic = "Automatic";
    public const string ReceiptQc = "ReceiptQc";
    public const string FieldSample = "FieldSample";
    public const string None = "None";
}

public sealed class RunProjection
{
    public long Id { get; set; }
    public DateOnly PlannedRunDate { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; }
    public string ProjectionMode { get; set; } = RunProjectionModes.Inventory;
    public int? FacilityWarehouseId { get; set; }
    public Warehouse? FacilityWarehouse { get; set; }
    public string? FacilityCodeSnapshot { get; set; }
    public int? CommercialPackPlanId { get; set; }
    public CommercialPackPlan? CommercialPackPlan { get; set; }
    public string? PackPlanCodeSnapshot { get; set; }
    public string? PackPlanNameSnapshot { get; set; }
    public string? PackPlanTypeSnapshot { get; set; }
    public string? PackConfigurationSnapshotJson { get; set; }
    public string? PackAllocationSnapshotJson { get; set; }
    public string? PackCalculationVersion { get; set; }
    public DateTimeOffset? PackCalculatedAt { get; set; }
    public int CropYear { get; set; }
    public long? SourceProjectionId { get; set; }
    public RunProjection? SourceProjection { get; set; }
    public ICollection<RunProjection> DerivedProjections { get; } = new List<RunProjection>();
    public decimal ApplePoundsPerBin { get; set; }
    public decimal PearPoundsPerBin { get; set; }
    public decimal StandardBoxWeightPounds { get; set; }
    public decimal PeelerCullShare { get; set; } = 0.35m;
    public decimal JuiceCullShare { get; set; } = 0.40m;
    public decimal WasteCullShare { get; set; } = 0.25m;
    public string CullCalculationVersion { get; set; } = "1.0";
    public int TotalPlannedBins { get; set; }
    public decimal TotalProjectedPounds { get; set; }
    public decimal TotalProjectedBoxes { get; set; }
    public int TotalRoundedProjectedBoxes { get; set; }
    public decimal TotalPackedProjectedPounds { get; set; }
    public decimal TotalPackedProjectedBoxes { get; set; }
    public int TotalRoundedPackedProjectedBoxes { get; set; }
    public decimal TotalCullProjectedPounds { get; set; }
    public decimal TotalCullProjectedBoxes { get; set; }
    public int TotalRoundedCullProjectedBoxes { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public string? CancelReason { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
    public string? DeletionReason { get; set; }
    public Guid? DeletionOperationId { get; set; }
    public string? DeletedFromStatus { get; set; }
    public ICollection<RunProjectionSource> Sources { get; } = new List<RunProjectionSource>();
}

public sealed class RunProjectionSource
{
    public long Id { get; set; }
    public long RunProjectionId { get; set; }
    public RunProjection RunProjection { get; set; } = null!;
    public required string SourceType { get; set; }
    public string? InventoryKey { get; set; }
    public string? GrowerLotKeySnapshot { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? SourceInventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment? SourceInventoryAdjustment { get; set; }
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
    public int? CanonicalOrchardBlockId { get; set; }
    public CanonicalOrchardBlock? CanonicalOrchardBlock { get; set; }
    public int FruitProfileId { get; set; }
    public FruitProfile FruitProfile { get; set; } = null!;
    public long? FieldSampleId { get; set; }
    public QcSample? FieldSample { get; set; }
    public long? SourceProjectionSourceId { get; set; }
    public RunProjectionSource? SourceProjectionSource { get; set; }
    public ICollection<RunProjectionSource> DerivedSources { get; } = new List<RunProjectionSource>();
    public required string SelectedQcSourceType { get; set; }
    public long? SelectedQcSampleId { get; set; }
    public QcSample? SelectedQcSample { get; set; }
    public int PlannedBins { get; set; }
    public int? AvailableBinsSnapshot { get; set; }
    public int? ReceivedBinsSnapshot { get; set; }
    public int? AdditionalExpectedBinsSnapshot { get; set; }
    public string? ContributingReceiptIdsJson { get; set; }
    public string? ContributingSampleIdsJson { get; set; }
    public string? ReceiptWeightingSnapshotJson { get; set; }
    public string? RefreshHistoryJson { get; set; }
    public DateTimeOffset? LastRefreshedAt { get; set; }
    public bool AvailabilityOverrideAcknowledged { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public required string Commodity { get; set; }
    public decimal PoundsPerBinUsed { get; set; }
    public decimal ProjectedPounds { get; set; }
    public decimal ProjectedBoxes { get; set; }
    public int RoundedProjectedBoxes { get; set; }
    public decimal? ExpectedPackoutPercent { get; set; }
    public decimal? ExpectedCullPercent { get; set; }
    public bool ExpectedPackoutUsedDefault { get; set; }
    public decimal PackedProjectedPounds { get; set; }
    public decimal PackedProjectedBoxes { get; set; }
    public int RoundedPackedProjectedBoxes { get; set; }
    public decimal CullProjectedPounds { get; set; }
    public decimal CullProjectedBoxes { get; set; }
    public int RoundedCullProjectedBoxes { get; set; }
    public required string SourceLabelSnapshot { get; set; }
    public string? FacilitySnapshot { get; set; }
    public string? RoomSnapshot { get; set; }
    public string? LotSnapshot { get; set; }
    public string? OrchardSnapshot { get; set; }
    public string? GrowerSnapshot { get; set; }
    public string? GrowerNumberSnapshot { get; set; }
    public string? BlockSnapshot { get; set; }
    public required string VarietySnapshot { get; set; }
    public DateTimeOffset? QcSampleDateSnapshot { get; set; }
    public string? QcSampleTypeSnapshot { get; set; }
    public string? QcSampleStatusSnapshot { get; set; }
    public int? QcFruitCountSnapshot { get; set; }
    public int SizeBasisFruitCount { get; set; }
    public int GradeBasisFruitCount { get; set; }
    public int JointSizeGradeBasisFruitCount { get; set; }
    public decimal? AverageWeightGramsSnapshot { get; set; }
    public decimal? AveragePressureLbsSnapshot { get; set; }
    public string? GradeSummarySnapshot { get; set; }
    public string? DefectSummarySnapshot { get; set; }
    public string? JointSizeGradeSnapshotJson { get; set; }
    public string? FieldSampleTrendSnapshotJson { get; set; }
    public string? ProjectionWarning { get; set; }
    public string CalculationVersion { get; set; } = "1.0";
    public long? ActualBinsRunEntryId { get; set; }
    public BinsRunEntry? ActualBinsRunEntry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<RunProjectionSizeResult> SizeResults { get; } = new List<RunProjectionSizeResult>();
    public ICollection<RunProjectionGradeResult> GradeResults { get; } = new List<RunProjectionGradeResult>();
}

public sealed class RunProjectionSizeResult
{
    public long Id { get; set; }
    public long RunProjectionSourceId { get; set; }
    public RunProjectionSource RunProjectionSource { get; set; } = null!;
    public required string Commodity { get; set; }
    public int SizeCategory { get; set; }
    public int SampleCount { get; set; }
    public decimal Percentage { get; set; }
    public decimal UnroundedProjectedBoxes { get; set; }
    public int RoundedProjectedBoxes { get; set; }
    public decimal PackedProjectedBoxes { get; set; }
    public int RoundedPackedProjectedBoxes { get; set; }
    public decimal CullProjectedBoxes { get; set; }
    public int RoundedCullProjectedBoxes { get; set; }
}

public sealed class RunProjectionGradeResult
{
    public long Id { get; set; }
    public long RunProjectionSourceId { get; set; }
    public RunProjectionSource RunProjectionSource { get; set; } = null!;
    public required string GradeCode { get; set; }
    public int SampleCount { get; set; }
    public decimal Percentage { get; set; }
    public decimal GrossProjectedBoxes { get; set; }
    public int RoundedGrossProjectedBoxes { get; set; }
    public decimal PackedProjectedBoxes { get; set; }
    public int RoundedPackedProjectedBoxes { get; set; }
    public decimal CullProjectedBoxes { get; set; }
    public int RoundedCullProjectedBoxes { get; set; }
}
