using CropQc.Data.Entities;

namespace CropQc.Web.Models;

public sealed class FieldSampleIndexViewModel
{
    public string? DataWarning { get; set; }
    public FieldSampleSearchForm Search { get; set; } = new();
    public IReadOnlyList<FieldSampleListItemViewModel> Samples { get; set; } = [];
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public bool CanCreate { get; set; }
}

public sealed class FieldSampleSearchForm
{
    public string? Search { get; set; }
    public int? FruitProfileId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? CompletionStatus { get; set; }
}

public sealed class FieldSampleListItemViewModel
{
    public long Id { get; set; }
    public string OrchardName { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string BlockName { get; set; } = "";
    public string OriginalBlockName { get; set; } = "";
    public string Variety { get; set; } = "";
    public DateTimeOffset SampleTakenAt { get; set; }
    public int EnteredFruitCount { get; set; }
    public decimal? AverageWeightGrams { get; set; }
    public decimal? AverageStarch { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public string CompletionStatus { get; set; } = "";
    public bool CanEdit { get; set; }
}

public sealed class FieldSampleCreatePageViewModel
{
    public FieldSampleCreateForm Form { get; set; } = new();
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public IReadOnlyList<CanonicalOrchardBlock> Blocks { get; set; } = [];
}

public class FieldSampleCreateForm
{
    public string OrchardName { get; set; } = "";
    public string? GrowerNumber { get; set; }
    public string BlockName { get; set; } = "";
    public int? CanonicalOrchardBlockId { get; set; }
    public bool ConfirmCreateNewBlock { get; set; }
    public int FruitProfileId { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
}

public sealed class FieldSampleMetadataForm : FieldSampleCreateForm
{
    public long SampleId { get; set; }
}

public sealed class FieldSampleDetailViewModel
{
    public string? DataWarning { get; set; }
    public long SampleId { get; set; }
    public string OrchardName { get; set; } = "";
    public string? GrowerNumber { get; set; }
    public string CanonicalBlockName { get; set; } = "";
    public string OriginalBlockName { get; set; } = "";
    public string Variety { get; set; } = "";
    public DateTimeOffset SampleTakenAt { get; set; }
    public string? Notes { get; set; }
    public int TargetSampleSize { get; set; } = 10;
    public bool CanEdit { get; set; }
    public bool IsEditingMetadata { get; set; }
    public DeviceCaptureSettingsViewModel DeviceCapture { get; set; } = DeviceCaptureSettingsViewModel.Disabled;
    public FieldSampleQcStationStatusViewModel QcStationStatus { get; set; } = new();
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public FieldSampleMetadataForm MetadataForm { get; set; } = new();
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public IReadOnlyList<CanonicalOrchardBlock> Blocks { get; set; } = [];
    public FieldSampleMetricSummary CurrentSummary { get; set; } = new();
    public IReadOnlyList<FieldSampleSizePoint> SizeDistribution { get; set; } = [];
    public IReadOnlyList<FieldSampleTrendPoint> Trend { get; set; } = [];
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<StarchScaleValue> StarchScaleValues { get; set; } = [];
    public SaveFruitReadingsForm FruitReadingForm { get; set; } = new();
}

public sealed class FieldSampleQcStationStatusViewModel
{
    public string State { get; set; } = "NotConfigured";
    public string Message { get; set; } = "No QC Station has synchronized this Field Sample yet.";
    public string? StationCode { get; set; }
    public string? StationName { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
}

public sealed record FieldSampleRefreshViewModel(
    long SampleId,
    int TargetSampleSize,
    DateTimeOffset? UpdatedAt,
    FieldSampleQcStationStatusViewModel QcStation,
    IReadOnlyList<FieldSampleRefreshRowViewModel> Rows);

public sealed record FieldSampleRefreshRowViewModel(
    int RowNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs,
    decimal? PressureAverageLbs,
    decimal? WeightGrams,
    int? SizeCategory,
    int? StarchScaleValueId);

public sealed class FieldSampleMetricSummary
{
    public int EnteredFruitCount { get; set; }
    public decimal? AverageWeightGrams { get; set; }
    public decimal? PeakWeightGrams { get; set; }
    public decimal? MinimumWeightGrams { get; set; }
    public int WeightRepresentedFruitCount { get; set; }
    public int MissingWeightCount { get; set; }
    public decimal? AverageStarch { get; set; }
    public int StarchRepresentedFruitCount { get; set; }
    public int MissingStarchCount { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public decimal? PeakPressureLbs { get; set; }
    public decimal? MinimumPressureLbs { get; set; }
    public decimal? PressureStandardDeviationLbs { get; set; }
    public int PressureReadingCount { get; set; }
    public int MissingPressureCount { get; set; }
    public decimal? AveragePressureChangeFromPriorLbs { get; set; }
    public decimal? AveragePressureChangeFromPriorPercent { get; set; }
    public DateTimeOffset? PriorPressureSampleDate { get; set; }
}

public sealed record FieldSampleSizePoint(int Size, decimal Percentage);

public sealed class FieldSampleTrendPoint
{
    public long SampleId { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; }
    public string Variety { get; set; } = "";
    public FieldSampleMetricSummary Summary { get; set; } = new();
    public IReadOnlyList<FieldSampleSizePoint> SizeDistribution { get; set; } = [];
}

public sealed record FieldSampleBlockSuggestion(int? BlockId, string CanonicalBlockName, string OrchardName, decimal Confidence, string Reason);
