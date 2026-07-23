using CropQc.Data;
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
    public int TargetSampleSize { get; set; } = 10;
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
    public string FruitType { get; set; } = "";
    public FieldSampleCommodityTerminology Terminology { get; set; } = FieldSampleCommodityTerminologyService.ForFruitType(null);
    public DateTimeOffset SampleTakenAt { get; set; }
    public string? Notes { get; set; }
    public string LifecycleStatus { get; set; } = "In Progress";
    public string EmailStatus { get; set; } = "Not Sent";
    public bool ChangedSinceLastSend { get; set; }
    public DateTimeOffset? LastSentAt { get; set; }
    public string? LastSentBy { get; set; }
    public string? LastRecipientSnapshot { get; set; }
    public IReadOnlyList<string> CompletionMissingItems { get; set; } = [];
    public bool CanMarkComplete { get; set; }
    public bool CanSend { get; set; }
    public int TargetSampleSize { get; set; } = 10;
    public long AutosaveVersion { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
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
    public IReadOnlyList<Grade> Grades { get; set; } = [];
    public IReadOnlyList<DefectType> DefectTypes { get; set; } = [];
    public IReadOnlyList<FieldSampleSizeThreshold> SizeThresholds { get; set; } = [];
    public IReadOnlyList<FieldSampleSendHistoryItem> SendHistory { get; set; } = [];
    public SaveFruitReadingsForm FruitReadingForm { get; set; } = new();
}

public sealed record FieldSampleSizeThreshold(int SizeCategory, decimal MinimumWeightGrams);

public sealed record FieldSampleSendHistoryItem(
    long Id,
    string Status,
    DateTimeOffset? SentAt,
    string? SentBy,
    string Recipients,
    string Subject,
    bool IsResend,
    string? Failure);

public sealed class FieldSampleReportPreviewViewModel
{
    public long SampleId { get; set; }
    public string Subject { get; set; } = "";
    public string Recipients { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public bool CanSend { get; set; }
    public bool IsResend { get; set; }
    public bool ChangedSinceLastSend { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
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
    long AutosaveVersion,
    FieldSampleQcStationStatusViewModel QcStation,
    IReadOnlyList<FieldSampleRefreshRowViewModel> Rows);

public sealed record FieldSampleRefreshRowViewModel(
    int RowNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs,
    decimal? PressureAverageLbs,
    decimal? WeightGrams,
    int? SizeCategory,
    int? StarchScaleValueId,
    int? GradeId,
    bool DefectsInspected,
    IReadOnlyList<int> DefectTypeIds,
    string? OtherDefectNotes,
    long FieldVersion);

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
    public decimal? AveragePressure1Lbs { get; set; }
    public decimal? AveragePressure2Lbs { get; set; }
    public decimal? PeakPressureLbs { get; set; }
    public decimal? MinimumPressureLbs { get; set; }
    public decimal? PressureStandardDeviationLbs { get; set; }
    public int PressureReadingCount { get; set; }
    public int MissingPressureCount { get; set; }
    public decimal? AveragePressureChangeFromPriorLbs { get; set; }
    public decimal? AveragePressureChangeFromPriorPercent { get; set; }
    public DateTimeOffset? PriorPressureSampleDate { get; set; }
    public IReadOnlyList<FieldSampleDistributionPoint> GradeDistribution { get; set; } = [];
    public IReadOnlyList<FieldSampleDistributionPoint> StarchDistribution { get; set; } = [];
    public IReadOnlyList<FieldSampleDefectSummaryPoint> DefectDistribution { get; set; } = [];
    public int DefectInspectedFruitCount { get; set; }
    public int DefectAffectedFruitCount { get; set; }
    public decimal? DefectAffectedPercentage { get; set; }
}

public sealed record FieldSampleSizePoint(int Size, decimal Percentage);
public sealed record FieldSampleDistributionPoint(string Label, decimal Percentage);
public sealed record FieldSampleDefectSummaryPoint(string Defect, int FruitCount, decimal PercentageOfInspectedFruit);

public sealed class FieldSampleTrendPoint
{
    public long SampleId { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; }
    public string Variety { get; set; } = "";
    public int TargetSampleSize { get; set; }
    public FieldSampleMetricSummary Summary { get; set; } = new();
    public IReadOnlyList<FieldSampleSizePoint> SizeDistribution { get; set; } = [];
}

public sealed record FieldSampleBlockSuggestion(int? BlockId, string CanonicalBlockName, string OrchardName, decimal Confidence, string Reason);

public sealed class FieldSampleAutosaveRequest
{
    public string ChangeId { get; set; } = "";
    public string Source { get; set; } = "Browser";
    public int? TargetSampleSize { get; set; }
    public List<FieldSampleAutosaveFieldChange> MetadataChanges { get; set; } = [];
    public List<FieldSampleAutosaveRowChange> RowChanges { get; set; } = [];
}

public sealed class FieldSampleAutosaveRowChange
{
    public int RowNumber { get; set; }
    public long FieldVersion { get; set; }
    public List<FieldSampleAutosaveFieldChange> Changes { get; set; } = [];
}

public sealed class FieldSampleAutosaveFieldChange
{
    public string Field { get; set; } = "";
    public string? Value { get; set; }
    public string? OriginalValue { get; set; }
}

public sealed record FieldSampleAutosaveConflict(
    string Scope,
    int? RowNumber,
    string Field,
    string? ClientValue,
    string? ServerValue,
    string Message);

public sealed record FieldSampleAutosaveValidationError(string Scope, int? RowNumber, string Field, string Message);

public sealed class FieldSampleAutosaveResult
{
    public bool Saved { get; set; }
    public DateTimeOffset? SavedAt { get; set; }
    public long AutosaveVersion { get; set; }
    public IReadOnlyDictionary<string, string?> MetadataValues { get; set; } = new Dictionary<string, string?>();
    public IReadOnlyList<FieldSampleRefreshRowViewModel> Rows { get; set; } = [];
    public IReadOnlyList<FieldSampleAutosaveConflict> Conflicts { get; set; } = [];
    public IReadOnlyList<FieldSampleAutosaveValidationError> ValidationErrors { get; set; } = [];
    public string? Error { get; set; }
}
