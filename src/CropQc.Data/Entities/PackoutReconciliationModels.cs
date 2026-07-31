namespace CropQc.Data.Entities;

public static class PackoutRunStatuses
{
    public const string Review = "Review";
    public const string PendingFinalization = "Pending finalization";
    public const string Finalized = "Finalized";
    public const string Reopened = "Reopened";
}

public static class PackoutProductCategories
{
    public const string Packed = "Packed product";
    public const string Juice = "Juice";
    public const string PeelerSlicer = "Peeler/Slicer";
    public const string Waste = "Waste";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Packed,
            Juice,
            PeelerSlicer,
            Waste
        };
}

public sealed class PackoutAnalysisConfiguration
{
    public int Id { get; set; } = 1;
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
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class PackCodeDefinition
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string NormalizedCode { get; set; }
    public required string DisplayName { get; set; }
    public required string ProductCategory { get; set; }
    public decimal? NetWeightPounds { get; set; }
    public int? SizeCategory { get; set; }
    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class PackoutRun
{
    public long Id { get; set; }
    public long? RunProjectionId { get; set; }
    public RunProjection? RunProjection { get; set; }
    public long? ActualRunId { get; set; }
    public ActualRun? ActualRun { get; set; }
    public long? RunExpectationId { get; set; }
    public RunExpectation? RunExpectation { get; set; }
    public long? BinsRunEntryId { get; set; }
    public BinsRunEntry? BinsRunEntry { get; set; }
    public required string Status { get; set; }
    public required string FacilitySnapshot { get; set; }
    public DateOnly PackingDate { get; set; }
    public int RunNumber { get; set; }
    public required string LotNumberSnapshot { get; set; }
    public required string VarietySnapshot { get; set; }
    public bool IsOrganicSnapshot { get; set; }
    public int CropYearSnapshot { get; set; }
    public decimal DumpedBins { get; set; }
    public decimal PoundsPerBin { get; set; }
    public decimal DumpedPounds { get; set; }
    public decimal PackedProductPounds { get; set; }
    public decimal JuicePounds { get; set; }
    public decimal PeelerSlicerPounds { get; set; }
    public decimal WastePounds { get; set; }
    public decimal? SupplementalJuicePounds { get; set; }
    public decimal? SupplementalPeelerSlicerPounds { get; set; }
    public decimal? SupplementalWastePounds { get; set; }
    public decimal? ActualPackoutPercent { get; set; }
    public decimal? ActualJuicePercent { get; set; }
    public decimal? ActualPeelerSlicerPercent { get; set; }
    public decimal? ActualWastePercent { get; set; }
    public decimal? SizeAccuracyScore { get; set; }
    public decimal? GradeAccuracyScore { get; set; }
    public decimal? PackoutAccuracyScore { get; set; }
    public decimal? JuiceAccuracyScore { get; set; }
    public decimal? PeelerSlicerAccuracyScore { get; set; }
    public decimal? WasteAccuracyScore { get; set; }
    public decimal? OverallAccuracyScore { get; set; }
    public decimal ReconciliationDifferencePounds { get; set; }
    public bool HasReconciliationWarning { get; set; }
    public string? ReviewNotes { get; set; }
    public string? ProjectionSnapshotJson { get; set; }
    public string? ActualDistributionSnapshotJson { get; set; }
    public string? AccuracySnapshotJson { get; set; }
    public string? ConfigurationSnapshotJson { get; set; }
    public string? CalculationVersion { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public int? FinalizedByUserId { get; set; }
    public User? FinalizedByUser { get; set; }
    public string? FinalReportFileName { get; set; }
    public string? FinalReportSha256 { get; set; }
    public string? FinalEmailMessageId { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public int? ReopenedByUserId { get; set; }
    public User? ReopenedByUser { get; set; }
    public string? ReopenReason { get; set; }
    public ICollection<PackoutReportSource> Sources { get; } = new List<PackoutReportSource>();
    public ICollection<PackoutReportLine> Lines { get; } = new List<PackoutReportLine>();
    public ICollection<PackoutEmailAttempt> EmailAttempts { get; } = new List<PackoutEmailAttempt>();
    public ICollection<PackoutSourceAllocation> SourceAllocations { get; } = new List<PackoutSourceAllocation>();
}

public sealed class PackoutReportSource
{
    public long Id { get; set; }
    public long PackoutRunId { get; set; }
    public PackoutRun PackoutRun { get; set; } = null!;
    public required string OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public required string ParserName { get; set; }
    public string? ParserVersion { get; set; }
    public decimal? Confidence { get; set; }
    public string? SafeDiagnostic { get; set; }
    public DateTimeOffset ParsedAt { get; set; }
}

public sealed class PackoutReportLine
{
    public long Id { get; set; }
    public long PackoutRunId { get; set; }
    public PackoutRun PackoutRun { get; set; } = null!;
    public long? PackoutReportSourceId { get; set; }
    public PackoutReportSource? PackoutReportSource { get; set; }
    public int SourceLineNumber { get; set; }
    public required string RawText { get; set; }
    public string? RawPackCode { get; set; }
    public string? NormalizedPackCode { get; set; }
    public int? PackCodeDefinitionId { get; set; }
    public PackCodeDefinition? PackCodeDefinition { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? NetWeightPounds { get; set; }
    public decimal? ExtendedWeightPounds { get; set; }
    public int? SizeCategory { get; set; }
    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public string? ProductCategory { get; set; }
    public decimal Confidence { get; set; }
    public bool RequiresReview { get; set; }
    public bool NegativeQuantityConfirmed { get; set; }
    public bool WasCorrected { get; set; }
    public string? CorrectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class PackoutEmailAttempt
{
    public long Id { get; set; }
    public long PackoutRunId { get; set; }
    public PackoutRun PackoutRun { get; set; } = null!;
    public required string Recipient { get; set; }
    public int? SenderUserId { get; set; }
    public User? SenderUser { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? MessageId { get; set; }
    public string? SafeError { get; set; }
    public bool IsUpdatedAnalysis { get; set; }
}
