namespace CropQc.Web.Models;

public sealed class RunProjectionPlannerViewModel
{
    public DateOnly SelectedDate { get; set; }
    public DateOnly PacificToday { get; set; }
    public DateOnly CalendarStartDate { get; set; }
    public DateOnly CalendarEndDate { get; set; }
    public IReadOnlyList<RunProjectionCalendarDayViewModel> CalendarDays { get; set; } = [];
    public IReadOnlyList<RunProjectionDateShortcutViewModel> HistoricalProjectionDates { get; set; } = [];
    public IReadOnlyList<RunProjectionDateShortcutViewModel> LaterProjectionDates { get; set; } = [];
    public IReadOnlyList<RunProjectionListItemViewModel> Projections { get; set; } = [];
    public IReadOnlyList<RunProjectionListItemViewModel> RecentActivity { get; set; } = [];
    public IReadOnlyList<RunProjectionFacilityOptionViewModel> FacilityOptions { get; set; } = [];
    public IReadOnlyList<RunProjectionFacilityTotalsViewModel> FacilityTotals { get; set; } = [];
    public RunProjectionDetailViewModel? SelectedProjection { get; set; }
    public RunProjectionCreateForm CreateForm { get; set; } = new();
    public string SelectedFacility { get; set; } = "All";
    public string SelectedDeletionStatus { get; set; } = "Active";
    public string SelectedSort { get; set; } = "Facility";
    public int UnassignedProjectionCount { get; set; }
    public bool CanEdit { get; set; }
    public bool CanAdmin { get; set; }
    public bool CanViewDeleted { get; set; }
    public bool HasUpcomingProjections { get; set; }
    public bool IsDirectProjectionOpen { get; set; }
    public int VisibilityPastDays { get; set; }
    public int VisibilityFutureDays { get; set; }
    public decimal DefaultExpectedPackoutPercent { get; set; }
    public string? PlannerWarning { get; set; }
    public string? DiagnosticReference { get; set; }
}

public sealed record RunProjectionDateShortcutViewModel(
    DateOnly Date,
    int ProjectionCount,
    int TotalPlannedBins);

public sealed class RunProjectionCalendarDayViewModel
{
    public DateOnly Date { get; set; }
    public int ProjectionCount { get; set; }
    public int WpProjectionCount { get; set; }
    public int WpPlannedBins { get; set; }
    public int EbsProjectionCount { get; set; }
    public int EbsPlannedBins { get; set; }
    public int UnassignedProjectionCount { get; set; }
    public int UnassignedPlannedBins { get; set; }
    public int TotalPlannedBins => WpPlannedBins + EbsPlannedBins + UnassignedPlannedBins;
    public bool IsSelected { get; set; }
    public bool IsToday { get; set; }
}

public sealed record RunProjectionFacilityOptionViewModel(int WarehouseId, string Code, string Name);

public sealed class RunProjectionFacilityTotalsViewModel
{
    public string FacilityCode { get; set; } = "";
    public int ProjectionCount { get; set; }
    public int PlannedBins { get; set; }
    public decimal GrossPounds { get; set; }
    public decimal PackedPounds { get; set; }
    public decimal PackedBoxes { get; set; }
    public decimal CullBoxes { get; set; }
    public decimal? EffectivePackoutPercent => GrossPounds <= 0 ? null : PackedPounds / GrossPounds * 100m;
}

public class RunProjectionListItemViewModel
{
    public long Id { get; set; }
    public DateOnly PlannedRunDate { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string ProjectionMode { get; set; } = "";
    public int? FacilityWarehouseId { get; set; }
    public string FacilityCode { get; set; } = "Unassigned";
    public int TotalPlannedBins { get; set; }
    public decimal TotalProjectedPounds { get; set; }
    public decimal TotalProjectedBoxes { get; set; }
    public int TotalRoundedProjectedBoxes { get; set; }
    public decimal TotalPackedProjectedPounds { get; set; }
    public decimal TotalPackedProjectedBoxes { get; set; }
    public decimal TotalCullProjectedBoxes { get; set; }
    public decimal? EffectivePackoutPercent =>
        TotalProjectedPounds <= 0 ? null : TotalPackedProjectedPounds / TotalProjectedPounds * 100m;
    public string Creator { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public int SourceCount { get; set; }
    public int ConvertedSourceCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletionReason { get; set; }
}

public sealed class RunProjectionDetailViewModel : RunProjectionListItemViewModel
{
    public bool IsLocked { get; set; }
    public int CropYear { get; set; }
    public long? SourceProjectionId { get; set; }
    public decimal ApplePoundsPerBin { get; set; }
    public decimal PearPoundsPerBin { get; set; }
    public decimal StandardBoxWeightPounds { get; set; }
    public decimal PeelerCullShare { get; set; } = 0.35m;
    public decimal JuiceCullShare { get; set; } = 0.35m;
    public decimal WasteCullShare { get; set; } = 0.30m;
    public string CullCalculationVersion { get; set; } = "2.0";
    public int TotalRoundedPackedProjectedBoxes { get; set; }
    public decimal TotalCullProjectedPounds { get; set; }
    public int TotalRoundedCullProjectedBoxes { get; set; }
    public new decimal? EffectivePackoutPercent =>
        TotalProjectedPounds <= 0 || Sources.Any(x => x.ExpectedPackoutPercent is null)
            ? null
            : TotalPackedProjectedPounds / TotalProjectedPounds * 100m;
    public long ConcurrencyVersion { get; set; }
    public string? CancelReason { get; set; }
    public string? DeletedFromStatus { get; set; }
    public Guid? DeletionOperationId { get; set; }
    public IReadOnlyList<RunProjectionSourceViewModel> Sources { get; set; } = [];
    public IReadOnlyList<RunProjectionCombinedSizeViewModel> CombinedSizes { get; set; } = [];
    public IReadOnlyList<RunProjectionCombinedGradeViewModel> CombinedGrades { get; set; } = [];
    public int? CommercialPackPlanId { get; set; }
    public string? PackPlanCode { get; set; }
    public string? PackPlanName { get; set; }
    public string? PackPlanType { get; set; }
    public string? PackCalculationVersion { get; set; }
    public DateTimeOffset? PackCalculatedAt { get; set; }
    public IReadOnlyList<RunProjectionPackPlanOptionViewModel> PackPlanOptions { get; set; } = [];
    public IReadOnlyList<RunProjectionPackResultViewModel> PackResults { get; set; } = [];
    public IReadOnlyList<RunProjectionUnallocatedFruitViewModel> UnallocatedFruit { get; set; } = [];
    public IReadOnlyList<string> PackWarnings { get; set; } = [];
    public decimal PackAssignedPounds { get; set; }
    public decimal PackUnallocatedPounds { get; set; }
    public decimal PackRoundingResidualPounds { get; set; }
    public bool HasUnknownCommodity => Sources.Any(x => x.Commodity == "Unknown");
    public bool HasUnmappedFieldSampleSources => Sources.Any(x => x.SourceType == "FieldSample" && x.ActualBinsRunEntryId is null);
    public bool CanEditRecord { get; set; }
    public bool CanDeleteRecord { get; set; }
}

public sealed record RunProjectionPackPlanOptionViewModel(
    int Id,
    string Code,
    string DisplayName,
    string Commodity,
    string PlanType);

public sealed record RunProjectionPackContributionViewModel(
    long SourceId,
    string SourceLabel,
    int SizeCategory,
    decimal AssignedPounds,
    decimal GrossPounds,
    decimal CullPounds);

public sealed record RunProjectionPackGradeViewModel(string GradeCode, decimal AssignedPounds);

public sealed record RunProjectionPackResultViewModel(
    int DefinitionId,
    string PackCode,
    string PackName,
    string Commodity,
    string PackType,
    decimal PackageWeightPounds,
    bool IsMixedSize,
    string MixRule,
    IReadOnlyList<int> EligibleSizes,
    decimal GrossAssignedPounds,
    decimal AssignedPounds,
    decimal CullPounds,
    decimal UnroundedPacks,
    int RoundedPacks,
    decimal RoundingResidualPounds,
    decimal PercentageOfProjectedPackout,
    IReadOnlyList<RunProjectionPackContributionViewModel> Contributions,
    int JointBasisFruitCount,
    IReadOnlyList<RunProjectionPackGradeViewModel> GradeAllocations,
    string? GradeWarning);

public sealed record RunProjectionUnallocatedFruitViewModel(
    long SourceId,
    string SourceLabel,
    string Commodity,
    int SizeCategory,
    decimal Pounds,
    decimal StandardBoxEquivalents,
    string Reason);

public sealed class RunProjectionPackPlanForm
{
    public long ProjectionId { get; set; }
    public int CommercialPackPlanId { get; set; }
    public long ConcurrencyVersion { get; set; }
    public DateOnly PlannedRunDate { get; set; }
    public string? ConfigurationHash { get; set; }
}

public sealed class RunProjectionPackPlanPreviewViewModel
{
    public long ProjectionId { get; set; }
    public string ProjectionName { get; set; } = "";
    public DateOnly PlannedRunDate { get; set; }
    public long ConcurrencyVersion { get; set; }
    public int CommercialPackPlanId { get; set; }
    public string ProposedPlanName { get; set; } = "";
    public string ProposedPlanType { get; set; } = "";
    public string ConfigurationHash { get; set; } = "";
    public IReadOnlyList<RunProjectionPackResultViewModel> CurrentPacks { get; set; } = [];
    public IReadOnlyList<RunProjectionPackResultViewModel> ProposedPacks { get; set; } = [];
    public IReadOnlyList<RunProjectionUnallocatedFruitViewModel> CurrentUnallocated { get; set; } = [];
    public IReadOnlyList<RunProjectionUnallocatedFruitViewModel> ProposedUnallocated { get; set; } = [];
    public IReadOnlyList<string> ProposedWarnings { get; set; } = [];
    public decimal CurrentAssignedPounds { get; set; }
    public decimal ProposedAssignedPounds { get; set; }
}

public sealed class RunProjectionSourceViewModel
{
    public long Id { get; set; }
    public string SourceType { get; set; } = "";
    public string? InventoryKey { get; set; }
    public string? GrowerLotKey { get; set; }
    public int? CanonicalOrchardBlockId { get; set; }
    public int FruitProfileId { get; set; }
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public string SourceLabel { get; set; } = "";
    public string? Facility { get; set; }
    public string? Room { get; set; }
    public string? Lot { get; set; }
    public string? Orchard { get; set; }
    public string? Grower { get; set; }
    public string? GrowerNumber { get; set; }
    public string? Block { get; set; }
    public string Variety { get; set; } = "";
    public string Commodity { get; set; } = "";
    public int PlannedBins { get; set; }
    public int? AvailableBinsSnapshot { get; set; }
    public int? ReceivedBinsToDate { get; set; }
    public int AdditionalExpectedBins { get; set; }
    public IReadOnlyList<RunProjectionReceiptContributionViewModel> ReceiptContributions { get; set; } = [];
    public IReadOnlyList<long> ContributingSampleIds { get; set; } = [];
    public DateTimeOffset? LastRefreshedAt { get; set; }
    public bool AvailabilityOverrideAcknowledged { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public string SelectedQcSourceType { get; set; } = "";
    public long? SelectedQcSampleId { get; set; }
    public string QcBasis { get; set; } = "";
    public string? QcSampleType { get; set; }
    public string? QcSampleStatus { get; set; }
    public DateTimeOffset? QcSampleDate { get; set; }
    public long? QcSampleId { get; set; }
    public int? QcFruitCount { get; set; }
    public int SizeBasisFruitCount { get; set; }
    public int GradeBasisFruitCount { get; set; }
    public int JointSizeGradeBasisFruitCount { get; set; }
    public string? JointSizeGradeSnapshotJson { get; set; }
    public decimal? AverageWeightGrams { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public string? GradeSummary { get; set; }
    public string? DefectSummary { get; set; }
    public decimal? TotalDefectPercentage { get; set; }
    public string? FieldSampleTrendSnapshotJson { get; set; }
    public decimal PoundsPerBin { get; set; }
    public decimal ProjectedPounds { get; set; }
    public decimal ProjectedBoxes { get; set; }
    public int RoundedProjectedBoxes { get; set; }
    public decimal? ExpectedPackoutPercent { get; set; }
    public decimal? ExpectedCullPercent { get; set; }
    public bool ExpectedPackoutUsedDefault { get; set; }
    public decimal PackedProjectedPounds { get; set; }
    public decimal PackedProjectedBoxes { get; set; }
    public int RoundedPackedProjectedBoxes { get; set; }
    public decimal CullProjectedPounds { get; set; }
    public decimal CullProjectedBoxes { get; set; }
    public int RoundedCullProjectedBoxes { get; set; }
    public string CalculationVersion { get; set; } = "";
    public string? Warning { get; set; }
    public long? ActualBinsRunEntryId { get; set; }
    public IReadOnlyList<RunProjectionSizeResultViewModel> SizeResults { get; set; } = [];
    public IReadOnlyList<RunProjectionGradeResultViewModel> GradeResults { get; set; } = [];
    public IReadOnlyList<RunProjectionQcChoiceViewModel> QcChoices { get; set; } = [];
    public IReadOnlyList<RunProjectionInventoryMappingChoiceViewModel> InventoryMappingChoices { get; set; } = [];
}

public sealed record RunProjectionReceiptContributionViewModel(
    long ReceiptId,
    string ReceiptReference,
    DateTimeOffset ReceivedAt,
    int BinsReceived,
    decimal WeightPercent,
    IReadOnlyList<long> SampleIds);

public sealed record RunProjectionSizeResultViewModel(
    string Commodity,
    int Size,
    int SampleCount,
    decimal Percentage,
    decimal UnroundedBoxes,
    int RoundedBoxes,
    decimal PackedBoxes,
    int RoundedPackedBoxes,
    decimal CullBoxes,
    int RoundedCullBoxes);

public sealed record RunProjectionGradeResultViewModel(
    string Grade,
    int SampleCount,
    decimal Percentage,
    decimal GrossBoxes,
    int RoundedGrossBoxes,
    decimal PackedBoxes,
    int RoundedPackedBoxes,
    decimal CullBoxes,
    int RoundedCullBoxes);

public sealed record RunProjectionCombinedSizeViewModel(
    string Commodity,
    int Size,
    decimal UnroundedBoxes,
    int RoundedBoxes,
    decimal PackedBoxes,
    int RoundedPackedBoxes,
    decimal CullBoxes,
    int RoundedCullBoxes);

public sealed record RunProjectionCombinedGradeViewModel(
    string Grade,
    decimal GrossBoxes,
    int RoundedGrossBoxes,
    decimal PackedBoxes,
    int RoundedPackedBoxes,
    decimal CullBoxes,
    int RoundedCullBoxes);

public sealed record RunProjectionQcChoiceViewModel(
    string Value,
    string Label,
    long? SampleId,
    string SourceType,
    DateTimeOffset? SampleDate,
    string? SampleType,
    string? Status,
    int FruitCount,
    decimal? AverageWeight,
    decimal? AveragePressure,
    bool HasSize,
    bool HasGrade,
    bool HasDefects,
    bool IsSelected);

public sealed record RunProjectionSourceCandidateViewModel(
    string SourceKey,
    string SourceType,
    string Label,
    string? Facility,
    string? Room,
    string? Lot,
    string? Orchard,
    string? Grower,
    string? GrowerNumber,
    string? Block,
    string Variety,
    string Commodity,
    int? AvailableBins,
    bool ReceiptQcAvailable,
    bool FieldSampleAvailable,
    DateTimeOffset? LatestSampleDate,
    int? CanonicalOrchardBlockId,
    int? FruitProfileId,
    long? DefaultFieldSampleId);

public sealed record RunProjectionInventoryMappingChoiceViewModel(
    string InventoryKey,
    string Label,
    int AvailableBins);

public sealed record RunProjectionInventorySource(
    string InventoryKey,
    long? ReceiptId,
    string? ReceiptReference,
    long? InventoryAdjustmentId,
    int WarehouseId,
    int RoomId,
    string Facility,
    string Room,
    int? FruitProfileId,
    string FruitType,
    int? CanonicalOrchardBlockId,
    string Grower,
    string? GrowerNumber,
    string Lot,
    string Variety,
    int CurrentBins,
    DateTimeOffset? ReceiptDate);

public sealed class RunProjectionCreateForm
{
    public DateOnly PlannedRunDate { get; set; }
    public string Name { get; set; } = "";
    public string ProjectionMode { get; set; } = "";
    public int? FacilityWarehouseId { get; set; }
}

public sealed class RunProjectionHeaderForm
{
    public long Id { get; set; }
    public DateOnly PlannedRunDate { get; set; }
    public string Name { get; set; } = "";
    public int? FacilityWarehouseId { get; set; }
    public long ConcurrencyVersion { get; set; }
}

public sealed class RunProjectionAddSourceForm
{
    public long ProjectionId { get; set; }
    public string SourceKey { get; set; } = "";
    public int PlannedBins { get; set; } = 1;
    public string SelectedQcSource { get; set; } = "Automatic";
    public decimal? ExpectedPackoutPercent { get; set; }
    public bool ExpectedPackoutUsedDefault { get; set; }
    public bool AvailabilityOverrideAcknowledged { get; set; }
    public long ConcurrencyVersion { get; set; }
}

public sealed class RunProjectionUpdateSourceForm
{
    public long ProjectionId { get; set; }
    public long SourceId { get; set; }
    public int PlannedBins { get; set; }
    public string SelectedQcSource { get; set; } = "Automatic";
    public decimal? ExpectedPackoutPercent { get; set; }
    public bool AvailabilityOverrideAcknowledged { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public long ConcurrencyVersion { get; set; }
}

public sealed class RunProjectionApplyPackoutForm
{
    public long ProjectionId { get; set; }
    public decimal ExpectedPackoutPercent { get; set; }
    public long ConcurrencyVersion { get; set; }
}

public sealed class RunProjectionStatusForm
{
    public long Id { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string? Reason { get; set; }
}

public sealed class RunProjectionDuplicateForm
{
    public long Id { get; set; }
    public DateOnly PlannedRunDate { get; set; }
    public string? Name { get; set; }
    public int? FacilityWarehouseId { get; set; }
}

public sealed class RunProjectionCreateInventoryForm
{
    public long Id { get; set; }
    public DateOnly PlannedRunDate { get; set; }
    public string Name { get; set; } = "";
    public int? FacilityWarehouseId { get; set; }
    public long ConcurrencyVersion { get; set; }
    public List<RunProjectionInventoryMappingForm> Mappings { get; set; } = [];
}

public sealed class RunProjectionInventoryMappingForm
{
    public long PreharvestSourceId { get; set; }
    public string InventoryKey { get; set; } = "";
    public bool AvailabilityOverrideAcknowledged { get; set; }
}

public sealed class DeleteRunProjectionForm
{
    public long Id { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string Reason { get; set; } = "";
    public string ConfirmationValue { get; set; } = "";
    public bool ConfirmDeletion { get; set; }
    public string OperationToken { get; set; } = "";
}

public sealed class RunProjectionDeletionConfirmationViewModel
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string FacilityCode { get; set; } = "Unassigned";
    public DateOnly PlannedRunDate { get; set; }
    public string Status { get; set; } = "";
    public string ProjectionMode { get; set; } = "";
    public int SourceCount { get; set; }
    public int TotalPlannedBins { get; set; }
    public decimal TotalProjectedBoxes { get; set; }
    public IReadOnlyList<long> LinkedActualRunIds { get; set; } = [];
    public string Creator { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string? BlockingReason { get; set; }
    public DeleteRunProjectionForm Form { get; set; } = new();
}
