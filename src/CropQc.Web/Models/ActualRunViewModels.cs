namespace CropQc.Web.Models;

public sealed class ActualRunDetailUnavailableViewModel
{
    public long ActualRunId { get; set; }
    public string ReferenceId { get; set; } = "";
}

public sealed class ActualRunDetailViewModel
{
    public long Id { get; set; }
    public string Status { get; set; } = "";
    public int RevisionNumber { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public string Facility { get; set; } = "";
    public int? SalesDeskId { get; set; }
    public string SalesDesk { get; set; } = "Unassigned";
    public string CreatedBy { get; set; } = "";
    public string? Notes { get; set; }
    public int TotalBins { get; set; }
    public IReadOnlyList<ActualRunContributionViewModel> Contributions { get; set; } = [];
    public IReadOnlyList<RunExpectationViewModel> Expectations { get; set; } = [];
    public RunExpectationViewModel? CurrentExpectation { get; set; }
    public ActualRunPackoutViewModel? Packout { get; set; }
    public bool CanViewPackout { get; set; }
    public bool CanUploadPackout { get; set; }
    public bool CanEditPackout { get; set; }
    public bool CanAdminPackout { get; set; }
    public bool CanCorrectSalesDesk { get; set; }
    public long ConcurrencyVersion { get; set; }
    public IReadOnlyList<SalesDeskOptionViewModel> SalesDeskOptions { get; set; } = [];
    public IReadOnlyList<ActualRunSalesDeskCorrectionViewModel> SalesDeskCorrections { get; set; } = [];
    public bool OptionalDetailAvailable { get; set; } = true;
    public string? DetailWarning { get; set; }
}

public sealed record ActualRunSalesDeskCorrectionViewModel(
    string PreviousSalesDesk,
    string NewSalesDesk,
    string Reason,
    string CorrectedBy,
    DateTimeOffset CorrectedAt);

public sealed record ActualRunContributionViewModel(
    long BinsRunEntryId,
    string Facility,
    string Room,
    string Grower,
    string GrowerNumber,
    string Lot,
    string Variety,
    string ProductionType,
    bool? IsOrganic,
    string InventoryStatus,
    string TreatmentSummary,
    IReadOnlyList<TreatmentReportLinkViewModel> TreatmentReports,
    int? CropYear,
    int Bins,
    decimal ContributionPercent);

public sealed record TreatmentReportLinkViewModel(long ApplicationId, long AttachmentId, string FileName, string ContentType);

public sealed class RunExpectationViewModel
{
    public long Id { get; set; }
    public int RevisionNumber { get; set; }
    public int TotalBins { get; set; }
    public decimal GrossPounds { get; set; }
    public decimal ExpectedPackoutPercent { get; set; }
    public decimal ExpectedPackedPounds { get; set; }
    public int ExpectedWholeBoxes { get; set; }
    public decimal ExpectedCullPounds { get; set; }
    public decimal ExpectedJuicePounds { get; set; }
    public decimal ExpectedPeelerPounds { get; set; }
    public decimal ExpectedWastePounds { get; set; }
    public decimal ConfidencePercent { get; set; }
    public IReadOnlyDictionary<string, decimal> SizeDistribution { get; set; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, decimal> GradeDistribution { get; set; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    public string CalculationVersion { get; set; } = "";
    public DateTimeOffset CalculatedAt { get; set; }
    public bool IsHistoricalReconstruction { get; set; }
    public DateTimeOffset? ReconstructedAt { get; set; }
    public DateTimeOffset? PhysicalRunAt { get; set; }
    public DateTimeOffset? QcEvidenceCutoff { get; set; }
    public string? ConfigurationBasis { get; set; }
    public string? CorrectionPackageIdentifier { get; set; }
}

public sealed class ActualRunPackoutViewModel
{
    public long Id { get; set; }
    public string Status { get; set; } = "";
    public decimal DumpedBins { get; set; }
    public decimal PackedPounds { get; set; }
    public decimal JuicePounds { get; set; }
    public decimal PeelerPounds { get; set; }
    public decimal WastePounds { get; set; }
    public decimal? ActualPackoutPercent { get; set; }
    public decimal? AccuracyPercent { get; set; }
    public decimal? SizeAccuracyPercent { get; set; }
    public decimal? GradeAccuracyPercent { get; set; }
    public decimal? PackoutVariancePercent { get; set; }
    public IReadOnlyList<PackoutDocumentViewModel> Documents { get; set; } = [];
    public IReadOnlyList<EstimatedAllocationViewModel> Allocations { get; set; } = [];
}

public sealed record PackoutDocumentViewModel(
    long Id,
    string FileName,
    long FileSizeBytes,
    DateTimeOffset? UploadedAt,
    string UploadedBy,
    string ParseStatus,
    string? Diagnostic,
    bool CanOpen);

public sealed record EstimatedAllocationViewModel(
    string Room,
    string Grower,
    string Lot,
    int Bins,
    decimal ContributionPercent,
    decimal PackedPounds,
    int WholeBoxes,
    decimal ResidualPounds,
    decimal JuicePounds,
    decimal PeelerPounds,
    decimal WastePounds,
    string AllocationVersion);
