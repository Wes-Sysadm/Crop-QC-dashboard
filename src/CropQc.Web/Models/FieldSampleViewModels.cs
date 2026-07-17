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
}

public sealed class FieldSampleListItemViewModel
{
    public long Id { get; set; }
    public string OrchardName { get; set; } = "";
    public string BlockName { get; set; } = "";
    public string OriginalBlockName { get; set; } = "";
    public string Variety { get; set; } = "";
    public DateTimeOffset SampleTakenAt { get; set; }
    public int EnteredFruitCount { get; set; }
    public decimal? AverageWeightGrams { get; set; }
    public decimal? AveragePressureLbs { get; set; }
}

public sealed class FieldSampleCreatePageViewModel
{
    public FieldSampleCreateForm Form { get; set; } = new();
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public IReadOnlyList<CanonicalOrchardBlock> Blocks { get; set; } = [];
}

public sealed class FieldSampleCreateForm
{
    public string OrchardName { get; set; } = "";
    public string? GrowerNumber { get; set; }
    public string BlockName { get; set; } = "";
    public int? CanonicalOrchardBlockId { get; set; }
    public int FruitProfileId { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
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
    public bool CanEdit { get; set; }
    public FieldSampleMetricSummary CurrentSummary { get; set; } = new();
    public IReadOnlyList<FieldSampleSizePoint> SizeDistribution { get; set; } = [];
    public IReadOnlyList<FieldSampleTrendPoint> Trend { get; set; } = [];
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<StarchScaleValue> StarchScaleValues { get; set; } = [];
    public SaveFruitReadingsForm FruitReadingForm { get; set; } = new();
}

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
