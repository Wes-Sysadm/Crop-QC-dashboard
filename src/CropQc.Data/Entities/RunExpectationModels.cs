namespace CropQc.Data.Entities;

public static class RunExpectationCalculationVersions
{
    public const string Current = "1.0-exact-room-lot-qc-snapshot";
}

public static class ActualAllocationVersions
{
    public const string Current = "1.0-bin-share-largest-remainder";
}

/// <summary>
/// Immutable expected outcome for one persisted Actual Run revision.
/// </summary>
public sealed class RunExpectation
{
    public long Id { get; set; }
    public long ActualRunId { get; set; }
    public ActualRun ActualRun { get; set; } = null!;
    public long ActualRunRevisionId { get; set; }
    public ActualRunRevision ActualRunRevision { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public int FacilityWarehouseId { get; set; }
    public required string FacilitySnapshot { get; set; }
    public DateTimeOffset RunAtSnapshot { get; set; }
    public int TotalBins { get; set; }
    public decimal GrossPounds { get; set; }
    public decimal ExpectedPackoutPercent { get; set; }
    public decimal ExpectedPackedPounds { get; set; }
    public decimal ExpectedPackedBoxes { get; set; }
    public int ExpectedWholeBoxes { get; set; }
    public decimal ExpectedCullPounds { get; set; }
    public decimal ExpectedJuicePounds { get; set; }
    public decimal ExpectedPeelerPounds { get; set; }
    public decimal ExpectedWastePounds { get; set; }
    public decimal ConfidencePercent { get; set; }
    public required string SizeDistributionSnapshotJson { get; set; }
    public required string GradeDistributionSnapshotJson { get; set; }
    public required string ConfigurationSnapshotJson { get; set; }
    public required string CalculationVersion { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<RunExpectationSource> Sources { get; } = new List<RunExpectationSource>();
    public ICollection<PackoutRun> PackoutRuns { get; } = new List<PackoutRun>();
}

public sealed class RunExpectationSource
{
    public long Id { get; set; }
    public long RunExpectationId { get; set; }
    public RunExpectation RunExpectation { get; set; } = null!;
    public long BinsRunEntryId { get; set; }
    public BinsRunEntry BinsRunEntry { get; set; } = null!;
    public int WarehouseId { get; set; }
    public int RoomId { get; set; }
    public required string FacilitySnapshot { get; set; }
    public required string RoomSnapshot { get; set; }
    public int? CropYearSnapshot { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public required string GrowerSnapshot { get; set; }
    public required string LotSnapshot { get; set; }
    public required string VarietySnapshot { get; set; }
    public required string ProductionTypeSnapshot { get; set; }
    public bool IsOrganicSnapshot { get; set; }
    public int BinsContributed { get; set; }
    public decimal ContributionPercent { get; set; }
    public long? QcSampleId { get; set; }
    public QcSample? QcSample { get; set; }
    public DateTimeOffset? QcSampleTakenAtSnapshot { get; set; }
    public int QcFruitCountSnapshot { get; set; }
    public required string QcMeasurementSnapshotJson { get; set; }
    public required string SizeDistributionSnapshotJson { get; set; }
    public required string GradeDistributionSnapshotJson { get; set; }
    public decimal GrossPounds { get; set; }
    public decimal ExpectedPackedPounds { get; set; }
    public int ExpectedWholeBoxes { get; set; }
    public decimal ExpectedCullPounds { get; set; }
    public decimal ConfidencePercent { get; set; }
    public string? WarningSnapshot { get; set; }
}

/// <summary>
/// Estimated proportional allocation of one authoritative overall Packout Result.
/// </summary>
public sealed class PackoutSourceAllocation
{
    public long Id { get; set; }
    public long PackoutRunId { get; set; }
    public PackoutRun PackoutRun { get; set; } = null!;
    public long RunExpectationSourceId { get; set; }
    public RunExpectationSource RunExpectationSource { get; set; } = null!;
    public int BinsContributed { get; set; }
    public decimal ContributionPercent { get; set; }
    public decimal AllocatedPackedPounds { get; set; }
    public int AllocatedWholeBoxes { get; set; }
    public decimal AllocatedResidualPounds { get; set; }
    public decimal AllocatedJuicePounds { get; set; }
    public decimal AllocatedPeelerPounds { get; set; }
    public decimal AllocatedWastePounds { get; set; }
    public required string PackCodeAllocationJson { get; set; }
    public required string SizeAllocationJson { get; set; }
    public required string GradeAllocationJson { get; set; }
    public required string AllocationVersion { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
}
