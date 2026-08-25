using Microsoft.AspNetCore.Http;

namespace CropQc.Web.Models;

public sealed class PackoutUploadForm
{
    public long ActualRunId { get; set; }
    public DateOnly PackingDate { get; set; }
    public int RunNumber { get; set; } = 1;
    public decimal DumpedBins { get; set; }
    public List<IFormFile> Files { get; set; } = [];
}

public sealed class PackoutLineReviewForm
{
    public long PackoutRunId { get; set; }
    public long LineId { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string? PackCode { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? NetWeightPounds { get; set; }
    public int? SizeCategory { get; set; }
    public int? GradeId { get; set; }
    public string? ProductCategory { get; set; }
    public string? CorrectionReason { get; set; }
    public bool NegativeQuantityConfirmed { get; set; }
}

public sealed class PackoutSecondaryOutputForm
{
    public long PackoutRunId { get; set; }
    public long ConcurrencyVersion { get; set; }
    public decimal JuiceBins { get; set; }
    public decimal PeelerSlicerBins { get; set; }
    public decimal WasteBins { get; set; }
    public string? ReviewNotes { get; set; }
}

public sealed class PackoutFinalizeForm
{
    public long PackoutRunId { get; set; }
    public long ConcurrencyVersion { get; set; }
}

public sealed class PackoutReopenForm
{
    public long PackoutRunId { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class PackCodeDefinitionForm
{
    public int? Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProductCategory { get; set; } = "";
    public decimal? NetWeightPounds { get; set; }
    public int? SizeCategory { get; set; }
    public int? GradeId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PackoutAnalysisConfigurationForm
{
    public decimal AppleBinWeightPounds { get; set; } = 880m;
    public decimal PearBinWeightPounds { get; set; } = 920m;
    public decimal SizeScoreWeight { get; set; } = 35m;
    public decimal GradeScoreWeight { get; set; } = 35m;
    public decimal PackoutScoreWeight { get; set; } = 21m;
    public decimal JuiceScoreWeight { get; set; } = 3m;
    public decimal PeelerSlicerScoreWeight { get; set; } = 3m;
    public decimal WasteScoreWeight { get; set; } = 3m;
    public decimal CurrentCropYearHistoryWeight { get; set; } = 80m;
    public decimal PriorCropYearHistoryWeight { get; set; } = 20m;
}

public sealed class PackoutRunViewModel
{
    public long Id { get; set; }
    public long? RunProjectionId { get; set; }
    public long? ActualRunId { get; set; }
    public long? RunExpectationId { get; set; }
    public string ProjectionName { get; set; } = "";
    public long? BinsRunEntryId { get; set; }
    public string Status { get; set; } = "";
    public string Facility { get; set; } = "";
    public DateOnly PackingDate { get; set; }
    public int RunNumber { get; set; }
    public string LotNumber { get; set; } = "";
    public string Variety { get; set; } = "";
    public bool IsOrganic { get; set; }
    public int CropYear { get; set; }
    public decimal DumpedBins { get; set; }
    public decimal PoundsPerBin { get; set; }
    public decimal DumpedPounds { get; set; }
    public decimal PackedProductPounds { get; set; }
    public decimal JuicePounds { get; set; }
    public decimal PeelerSlicerPounds { get; set; }
    public decimal WastePounds { get; set; }
    public decimal SupplementalJuiceBins { get; set; }
    public decimal SupplementalPeelerSlicerBins { get; set; }
    public decimal SupplementalWasteBins { get; set; }
    public decimal? ActualPackoutPercent { get; set; }
    public decimal? OverallAccuracyScore { get; set; }
    public bool IsHistoricalReconstruction { get; set; }
    public bool ReconciliationAvailable { get; set; }
    public DateTimeOffset? PhysicalRunAt { get; set; }
    public DateTimeOffset? ReconstructedAt { get; set; }
    public decimal ReconciliationDifferencePounds { get; set; }
    public bool HasReconciliationWarning { get; set; }
    public long ConcurrencyVersion { get; set; }
    public bool CanEdit { get; set; }
    public bool CanReopen { get; set; }
    public bool CanAdmin { get; set; }
    public PackoutAnalysisConfigurationForm Configuration { get; set; } = new();
    public IReadOnlyList<PackoutSourceViewModel> Sources { get; set; } = [];
    public IReadOnlyList<PackoutLineViewModel> Lines { get; set; } = [];
    public IReadOnlyList<PackCodeOptionViewModel> PackCodes { get; set; } = [];
    public IReadOnlyList<PackoutGradeOptionViewModel> Grades { get; set; } = [];
}

public sealed record PackoutSourceViewModel(
    long Id,
    string FileName,
    long FileSizeBytes,
    DateTimeOffset? UploadedAt,
    string UploadedBy,
    string ParseStatus,
    bool CanOpen,
    string Parser,
    decimal? Confidence,
    string? Diagnostic,
    DateTimeOffset ParsedAt);

public sealed record PackoutLineViewModel(
    long Id,
    int SourceLineNumber,
    string RawText,
    string? PackCode,
    decimal? Quantity,
    decimal? NetWeightPounds,
    decimal? ExtendedWeightPounds,
    int? SizeCategory,
    int? GradeId,
    string? Grade,
    string? ProductCategory,
    decimal Confidence,
    bool RequiresReview,
    bool WasCorrected,
    bool NegativeQuantityConfirmed);

public sealed record PackCodeOptionViewModel(
    int Id,
    string Code,
    string DisplayName,
    string ProductCategory,
    decimal? NetWeightPounds,
    int? SizeCategory,
    int? GradeId,
    bool IsActive);

public sealed record PackoutGradeOptionViewModel(int Id, string Code, string Name);
