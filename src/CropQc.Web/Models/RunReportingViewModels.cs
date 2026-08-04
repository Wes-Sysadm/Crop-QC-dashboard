namespace CropQc.Web.Models;

public sealed class RunReportingPageViewModel
{
    public int CurrentCropYear { get; set; }
    public IReadOnlyList<RunFacilitySummaryViewModel> FacilitySummaries { get; set; } = [];
    public IReadOnlyList<int> OlderCropYears { get; set; } = [];
    public RunTotalsDetailViewModel? Detail { get; set; }
    public IReadOnlyList<RunReportingIssueViewModel> Issues { get; set; } = [];
    public bool CanViewNeedsReview { get; set; }
    public int NeedsReviewPage { get; set; } = 1;
    public bool HasMoreIssues { get; set; }
}

public sealed record RunFacilitySummaryViewModel(
    string Facility,
    IReadOnlyList<RunCropYearSummaryViewModel> CropYears);

public sealed record RunCropYearSummaryViewModel(int CropYear, int Bins);

public sealed class RunTotalsDetailViewModel
{
    public string Facility { get; set; } = "";
    public int CropYear { get; set; }
    public int TotalBins { get; set; }
    public int PriorCropYear { get; set; }
    public int PriorBins { get; set; }
    public int DifferenceBins => TotalBins - PriorBins;
    public decimal? DifferencePercent => PriorBins == 0 ? null : (TotalBins - PriorBins) * 100m / PriorBins;
    public DateOnly SelectedStart { get; set; }
    public DateOnly SelectedCutoff { get; set; }
    public DateOnly PriorStart { get; set; }
    public DateOnly PriorCutoff { get; set; }
    public IReadOnlyList<RunVarietyTotalViewModel> Varieties { get; set; } = [];
    public IReadOnlyList<RunWeekTotalViewModel> Weeks { get; set; } = [];
    public IReadOnlyList<RunSupportingRecordViewModel> SupportingRecords { get; set; } = [];
    public string? SelectedVarietyKey { get; set; }
    public DateOnly? SelectedWeekStart { get; set; }
    public string? SelectedGrowerNumber { get; set; }
    public int SupportingPage { get; set; } = 1;
    public bool HasMoreSupportingRecords { get; set; }
}

public sealed record RunVarietyTotalViewModel(
    string VarietyKey,
    int? FruitProfileId,
    string Variety,
    string ProductionType,
    bool IsOrganic,
    int Bins,
    int PriorBins)
{
    public int DifferenceBins => Bins - PriorBins;
    public decimal? DifferencePercent => PriorBins == 0 ? null : (Bins - PriorBins) * 100m / PriorBins;
}

public sealed record RunWeekTotalViewModel(
    string VarietyKey,
    string Variety,
    string ProductionType,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int Bins,
    IReadOnlyList<RunGrowerTotalViewModel> Growers);

public sealed record RunGrowerTotalViewModel(string GrowerNumber, int Bins, int RecordCount);

public sealed record RunSupportingRecordViewModel(
    long EntryId,
    long? ActualRunId,
    string RecordType,
    string RecordUrl,
    DateTimeOffset RunAt,
    string RecordedEmployee,
    string RunFacility,
    string SourceFacility,
    string SourceRoom,
    string Lot,
    string GrowerNumber,
    int CropYear,
    string Variety,
    string ProductionType,
    int Bins,
    string Status);

public sealed record RunReportingIssueViewModel(
    string IssueType,
    string Explanation,
    int ExcludedBins,
    int? CropYear,
    string Variety,
    string RecordedUser,
    DateTimeOffset RunAt,
    string RecordSource,
    string RecordUrl,
    long EntryId);
