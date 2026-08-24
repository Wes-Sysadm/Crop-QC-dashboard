namespace CropQc.Web.Models;

public static class RunSheetReconciliationStates
{
    public const string Available = "Available";
    public const string Stale = "Stale";
    public const string Loading = "Loading";
    public const string Unavailable = "Unavailable";
    public const string Match = "Match";
    public const string Pending = "PendingSheetVerification";
    public const string Attention = "AttentionNeeded";
}

public static class RunSheetReconciliationReasons
{
    public const string ProbableDateMismatch = "Probable Match — Date Mismatch";
    public const string MissingFromCropQc = "Missing from Crop QC";
    public const string MissingFromSheet = "Missing from Sheet";
    public const string BinMismatch = "Bin mismatch";
    public const string GrowerMismatch = "Grower mismatch";
    public const string VarietyMismatch = "Variety mismatch";
    public const string ProductionTypeMismatch = "Production type mismatch";
    public const string SalesDeskMissing = "Sales Desk missing";
    public const string SalesDeskMismatch = "Sales Desk mismatch";
    public const string UnknownSalesDeskCode = "Unknown Sales Desk code";
}

public sealed class RunSheetReconciliationViewModel
{
    public string Availability { get; set; } = RunSheetReconciliationStates.Loading;
    public string? DiagnosticMessage { get; set; }
    public DateTimeOffset? LastSuccessfulRefreshAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public int AttentionNeededCount { get; set; }
    public int PendingCount { get; set; }
    public int MatchedCount { get; set; }
    public IReadOnlyList<RunSheetReconciliationItemViewModel> Items { get; set; } = [];
    public bool HasCurrentResults => Availability == RunSheetReconciliationStates.Available;
    public bool HasStaleResults => Availability == RunSheetReconciliationStates.Stale;
}

public sealed class RunSheetReconciliationItemViewModel
{
    public string State { get; set; } = RunSheetReconciliationStates.Match;
    public string Facility { get; set; } = "";
    public DateOnly? SheetDate { get; set; }
    public DateOnly? CropQcDate { get; set; }
    public string SheetVariety { get; set; } = "—";
    public string CropQcVariety { get; set; } = "—";
    public string SheetProductionType { get; set; } = "—";
    public string CropQcProductionType { get; set; } = "—";
    public string SheetSalesDesk { get; set; } = "N/A";
    public string CropQcSalesDesk { get; set; } = "N/A";
    public int? SheetBins { get; set; }
    public int? CropQcBins { get; set; }
    public IReadOnlyList<string> Reasons { get; set; } = [];
    public IReadOnlyList<long> ActualRunIds { get; set; } = [];
    public IReadOnlyList<RunSheetGrowerComparisonViewModel> Growers { get; set; } = [];
}

public sealed record RunSheetGrowerComparisonViewModel(string GrowerNumber, int SheetBins, int CropQcBins);
