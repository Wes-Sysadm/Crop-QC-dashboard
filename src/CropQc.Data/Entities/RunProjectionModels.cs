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

public static class RunProjectionSourceTypes
{
    public const string Inventory = "Inventory";
    public const string FieldSample = "FieldSample";
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
    public int CropYear { get; set; }
    public decimal ApplePoundsPerBin { get; set; }
    public decimal PearPoundsPerBin { get; set; }
    public decimal StandardBoxWeightPounds { get; set; }
    public int TotalPlannedBins { get; set; }
    public decimal TotalProjectedPounds { get; set; }
    public decimal TotalProjectedBoxes { get; set; }
    public int TotalRoundedProjectedBoxes { get; set; }
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
    public ICollection<RunProjectionSource> Sources { get; } = new List<RunProjectionSource>();
}

public sealed class RunProjectionSource
{
    public long Id { get; set; }
    public long RunProjectionId { get; set; }
    public RunProjection RunProjection { get; set; } = null!;
    public required string SourceType { get; set; }
    public string? InventoryKey { get; set; }
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
    public required string SelectedQcSourceType { get; set; }
    public long? SelectedQcSampleId { get; set; }
    public QcSample? SelectedQcSample { get; set; }
    public int PlannedBins { get; set; }
    public int? AvailableBinsSnapshot { get; set; }
    public bool AvailabilityOverrideAcknowledged { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public required string Commodity { get; set; }
    public decimal PoundsPerBinUsed { get; set; }
    public decimal ProjectedPounds { get; set; }
    public decimal ProjectedBoxes { get; set; }
    public int RoundedProjectedBoxes { get; set; }
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
    public int? QcFruitCountSnapshot { get; set; }
    public decimal? AverageWeightGramsSnapshot { get; set; }
    public decimal? AveragePressureLbsSnapshot { get; set; }
    public string? GradeSummarySnapshot { get; set; }
    public string? DefectSummarySnapshot { get; set; }
    public string? ProjectionWarning { get; set; }
    public long? ActualBinsRunEntryId { get; set; }
    public BinsRunEntry? ActualBinsRunEntry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<RunProjectionSizeResult> SizeResults { get; } = new List<RunProjectionSizeResult>();
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
}
