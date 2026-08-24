namespace CropQc.Web.Models;

public sealed class RunReportingPageViewModel
{
    public int CurrentCropYear { get; set; }
    public int AuthoritativeStartCropYear { get; set; }
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
    public int TotalReceivedBins { get; set; }
    public IReadOnlyList<RunSalesDeskTotalViewModel> SalesDeskTotals { get; set; } = [];
    public string? SelectedSalesDesk { get; set; }
    public IReadOnlyList<RunSalesDeskFilterOptionViewModel> SalesDeskFilterOptions { get; set; } = [];
    public int? PriorCropYear { get; set; }
    public bool HasAuthoritativePriorBaseline => PriorCropYear is not null;
    public int PriorBins { get; set; }
    public int? DifferenceBins => HasAuthoritativePriorBaseline ? TotalBins - PriorBins : null;
    public decimal? DifferencePercent => !HasAuthoritativePriorBaseline || PriorBins == 0 ? null : (TotalBins - PriorBins) * 100m / PriorBins;
    public DateOnly SelectedStart { get; set; }
    public DateOnly SelectedCutoff { get; set; }
    public DateOnly? PriorStart { get; set; }
    public DateOnly? PriorCutoff { get; set; }
    public IReadOnlyList<RunVarietyTotalViewModel> Varieties { get; set; } = [];
    public IReadOnlyList<RunWeekTotalViewModel> Weeks { get; set; } = [];
    public IReadOnlyList<RunSupportingRecordViewModel> SupportingRecords { get; set; } = [];
    public string? SelectedVarietyKey { get; set; }
    public DateOnly? SelectedWeekStart { get; set; }
    public string? SelectedGrowerNumber { get; set; }
    public int SupportingPage { get; set; } = 1;
    public bool HasMoreSupportingRecords { get; set; }
    public RunSheetReconciliationViewModel? SheetReconciliation { get; set; }
}

public sealed record RunSalesDeskTotalViewModel(int? SalesDeskId, string SalesDesk, int Bins, int DisplayOrder, bool IsUnassigned = false);
public sealed record RunSalesDeskFilterOptionViewModel(string Value, string Label);

public sealed record RunVarietyTotalViewModel(
    string VarietyKey,
    int? FruitProfileId,
    string Variety,
    string ProductionType,
    bool IsOrganic,
    int ReceivedBins,
    int Bins,
    int PriorBins,
    string ColorHex = "#607D8B",
    string TextColorHex = "#FFFFFF",
    bool IsColorConfigured = false)
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
    string Status,
    string SalesDesk = "N/A");

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

public sealed class GrowerLotProgressFilterForm
{
    public int? CropYear { get; set; }
    public string Facility { get; set; } = "All";
    public string? GrowerSearch { get; set; }
    public string? LotSearch { get; set; }
    public string? VarietyKey { get; set; }
    public string? ProductionType { get; set; }
    public string Sort { get; set; } = "GrowerNumber";
    public int Page { get; set; } = 1;
    public string? ExpandedGrowerNumber { get; set; }
    public string? ExpandedVarietyKey { get; set; }
    public string? SelectedLotKey { get; set; }
    public DateOnly? SelectedWeekStart { get; set; }
    public int SupportingPage { get; set; } = 1;
}

public sealed class GrowerLotProgressPageViewModel
{
    public int AuthoritativeStartCropYear { get; set; }
    public int CurrentCropYear { get; set; }
    public GrowerLotProgressFilterForm Filter { get; set; } = new();
    public IReadOnlyList<int> CropYears { get; set; } = [];
    public IReadOnlyList<GrowerLotVarietyOptionViewModel> VarietyOptions { get; set; } = [];
    public int GrowerCount { get; set; }
    public int ReceivedLotCount { get; set; }
    public int BinsReceived { get; set; }
    public int BinsRun { get; set; }
    public IReadOnlyList<GrowerProgressViewModel> Growers { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage { get; set; }
    public string? FilterValidationMessage { get; set; }
    public int ExcludedReceiptCount { get; set; }
    public int ExcludedRunLineCount { get; set; }
    public bool ExcludedSampleIsBounded { get; set; }
    public IReadOnlyList<GrowerLotProgressIssueViewModel> ExcludedIssues { get; set; } = [];
}

public sealed record GrowerLotVarietyOptionViewModel(string VarietyKey, string Variety, string ProductionType, bool IsOrganic);

public sealed class GrowerProgressViewModel
{
    public string GrowerNumber { get; set; } = "";
    public string? GrowerName { get; set; }
    public int ReceivedLotCount { get; set; }
    public int BinsReceived { get; set; }
    public int BinsRun { get; set; }
    public bool IsExpanded { get; set; }
    public IReadOnlyList<GrowerVarietyProgressViewModel> Varieties { get; set; } = [];
}

public sealed class GrowerVarietyProgressViewModel
{
    public string VarietyKey { get; set; } = "";
    public int FruitProfileId { get; set; }
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool IsOrganic { get; set; }
    public int BinsReceived { get; set; }
    public int BinsRun { get; set; }
    public int ReceivedLotCount { get; set; }
    public decimal? RunPercent => BinsReceived > 0 ? BinsRun * 100m / BinsReceived : null;
    public string ColorHex { get; set; } = "#607D8B";
    public string TextColorHex { get; set; } = "#FFFFFF";
    public bool IsColorConfigured { get; set; }
    public bool IsExpanded { get; set; }
    public IReadOnlyList<GrowerLotProgressViewModel> Lots { get; set; } = [];
}

public sealed class GrowerLotProgressViewModel
{
    public string LotKey { get; set; } = "";
    public int? GrowerLotId { get; set; }
    public string CanonicalVarietyKey { get; set; } = "";
    public string LotNumber { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool IsOrganic { get; set; }
    public string ReceivingFacilities { get; set; } = "";
    public DateTimeOffset? FirstReceiptAt { get; set; }
    public DateTimeOffset? LatestReceiptAt { get; set; }
    public int ReceiptCount { get; set; }
    public int BinsReceived { get; set; }
    public int BinsRun { get; set; }
    public int RunRecordCount { get; set; }
    public bool IsSelected { get; set; }
    public string? WeeklyDetailWarning { get; set; }
    public IReadOnlyList<GrowerLotWeekProgressViewModel> Weeks { get; set; } = [];
}

public sealed class GrowerLotWeekProgressViewModel
{
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd => WeekStart.AddDays(6);
    public int BinsRun { get; set; }
    public int CumulativeBinsRun { get; set; }
    public int RunRecordCount { get; set; }
    public bool IsSelected { get; set; }
    public IReadOnlyList<RunSupportingRecordViewModel> SupportingRecords { get; set; } = [];
    public int SupportingPage { get; set; } = 1;
    public bool HasPreviousSupportingRecords => SupportingPage > 1;
    public bool HasMoreSupportingRecords { get; set; }
}

public sealed record GrowerLotProgressIssueViewModel(string IssueType, string Explanation, string RecordUrl);
