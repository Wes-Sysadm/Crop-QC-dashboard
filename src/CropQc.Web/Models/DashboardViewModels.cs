using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;

namespace CropQc.Web.Models;

public sealed record StatusCountCard(string Label, int Count, string Href, string CssClass, string HelperText);

public sealed class HomeDashboardViewModel
{
    public string? DataWarning { get; set; }
    public int ActiveCropYear { get; set; }
    public IReadOnlyList<StatusCountCard> Cards { get; set; } = [];
    public IReadOnlyList<SampleListItemViewModel> TodaySamples { get; set; } = [];
    public IReadOnlyList<RoomSummaryItemViewModel> RoomSummaries { get; set; } = [];
    public RoomSummaryFilterForm RoomSummaryFilter { get; set; } = new();
    public IReadOnlyList<string> FacilityOptions { get; set; } = ["All", "MCD", "WP", "EBS", "DH"];
    public IReadOnlyList<string> EbsLocationOptions { get; set; } = ["All EBS", "Evans", "Lamb", "BM"];
    public IReadOnlyList<StorageFacilitySummaryViewModel> StorageByFacility { get; set; } = [];
}

public sealed class RoomSummaryFilterForm
{
    public string Facility { get; set; } = "All";
    public string EbsLocation { get; set; } = "All EBS";
    public string RoomStatus { get; set; } = "WithFruit";
}

public sealed class RoomSummaryItemViewModel
{
    public int RoomId { get; set; }
    public string Warehouse { get; set; } = "";
    public string Facility { get; set; } = "";
    public string LocationGroup { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string CompuTechCode { get; set; } = "";
    public string Status { get; set; } = "Empty";
    public int CurrentLotsCount { get; set; }
    public int? CurrentBinsCount { get; set; }
    public int RoomCapacityBins { get; set; }
    public bool IsCapacityConfigured => RoomCapacityBins > 0;
    public decimal? PercentFull => CurrentBinsCount is int currentBins && RoomCapacityBins > 0
        ? decimal.Round(currentBins / (decimal)RoomCapacityBins * 100m, 1)
        : null;
    public bool IsOverCapacity => PercentFull > 100m;
    public string AttentionCategory { get; set; } = "Stable";
    public int AttentionSort { get; set; } = 4;
    public string RankingReason { get; set; } = "No current concerns identified";
    public int QcRepresentedBins { get; set; }
    public int QcMissingBins { get; set; }
    public decimal QcCoveragePercent { get; set; }
    public string MajorWeakLotIndicator { get; set; } = "";
    public int StartingSeasonBins { get; set; }
    public int NetChangeBins { get; set; }
    public string VarietyStatusSummary { get; set; } = "";
    public IReadOnlyList<RoomVarietyColorSegmentViewModel> VarietyColorSegments { get; set; } = [];
    public int OrganicBins { get; set; }
    public int ConventionalBins { get; set; }
    public int UnknownOrganicStatusBins { get; set; }
    public decimal OrganicPercent { get; set; }
    public bool IsMajorityOrganic { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string LotSummary { get; set; } = "Empty";
    public decimal? AveragePressureLbs { get; set; }
    public decimal? PressureStdDevLbs { get; set; }
    public decimal? MonthOverMonthPressureChangeLbs { get; set; }
    public decimal? AverageStarch { get; set; }
    public int ReceivingStarchRepresentedBins { get; set; }
    public int ReceivingStarchMissingBins { get; set; }
    public int ReceivingPressureRepresentedBins { get; set; }
    public int ReceivingPressureMissingBins { get; set; }
    public decimal? LatestPressureLbs { get; set; }
    public DateTimeOffset? LatestPressureDate { get; set; }
    public int LatestPressureRepresentedBins { get; set; }
    public int LatestPressureMissingBins { get; set; }
    public int PressureChangeRepresentedBins { get; set; }
    public int PressureChangeMissingBins { get; set; }
    public int PressureStandardDeviationRepresentedBins { get; set; }
    public int PressureReadingCount { get; set; }
    public string DefectSummary { get; set; } = "None";
    public DateTimeOffset? LastSampleDate { get; set; }
    public string LatestQcSource { get; set; } = "";
    public int SampleCount { get; set; }
    public int EnteredFruitCount { get; set; }
    public IReadOnlyList<string> ReviewFlags { get; set; } = [];
    public string? WeakestLotLabel { get; set; }
    public string? WeakestLotReason { get; set; }
    public long? WeakestLotReceiptId { get; set; }
}

public sealed class RoomVarietyColorSegmentViewModel
{
    public string VarietyKey { get; set; } = "";
    public string VarietyName { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool? IsOrganic { get; set; }
    public string OrganicLabel => IsOrganic switch { true => "Organic", false => "Conventional", _ => "Organic status unknown" };
    public string IdentityLabel => string.Equals(ProductionType?.Trim(), OrganicLabel, StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(ProductionType)
            ? OrganicLabel
            : $"{ProductionType.Trim()} - {OrganicLabel}";
    public int CurrentBins { get; set; }
    public decimal Percent { get; set; }
    public string HexColor { get; set; } = "#607D8B";
    public bool IsConfiguredColor { get; set; }
}

public sealed class RoomDetailViewModel
{
    public string? DataWarning { get; set; }
    public RoomSummaryItemViewModel? Summary { get; set; }
    public IReadOnlyList<RoomLotSummaryViewModel> CurrentLots { get; set; } = [];
    public IReadOnlyList<RoomGrowerSummaryViewModel> CurrentGrowers { get; set; } = [];
    public IReadOnlyList<RoomLotSummaryViewModel> DepletedLots { get; set; } = [];
    public IReadOnlyList<RoomDepletionListItemViewModel> Depletions { get; set; } = [];
    public IReadOnlyList<RoomInventoryAdjustmentListItemViewModel> InventoryAdjustments { get; set; } = [];
    public IReadOnlyList<ReceiptListItemViewModel> LinkedReceipts { get; set; } = [];
    public IReadOnlyList<RoomReceiptEvidenceLinkViewModel> LikelySourceReceipts { get; set; } = [];
    public BinsRunProjectionViewModel BaselineProjection { get; set; } = new();
    public IReadOnlyList<RoomProjectionLotViewModel> ProjectionLots { get; set; } = [];
    public IReadOnlyList<RoomSampleTimelineItemViewModel> SampleTimeline { get; set; } = [];
    public IReadOnlyList<RoomReceiptOptionViewModel> DepletionReceiptOptions { get; set; } = [];
    public IReadOnlyList<RoomInventoryLotOptionViewModel> TransferLotOptions { get; set; } = [];
    public int TransferCurrentRoomBins { get; set; }
    public int TransferAvailableBins { get; set; }
    public bool TransferInventoryReconciles { get; set; }
    public string? TransferInventoryError { get; set; }
    public IReadOnlyList<RoomTransferFacilityViewModel> TransferDestinationFacilities { get; set; } = [];
    public IReadOnlyList<RoomTransferDestinationViewModel> TransferDestinationOptions { get; set; } = [];
    public RoomDepletionForm DepletionForm { get; set; } = new();
    public RoomInventoryTrueUpForm TrueUpForm { get; set; } = new();
    public RoomTransferForm TransferForm { get; set; } = new();
    public bool CanManageDepletions { get; set; }
    public IReadOnlyList<RoomInventoryLossOptionViewModel> InventoryLossOptions { get; set; } = [];
    public IReadOnlyList<RoomInventoryLossHistoryViewModel> InventoryLosses { get; set; } = [];
    public RoomInventoryLossForm InventoryLossForm { get; set; } = new();
    public bool CanRecordInventoryLoss { get; set; }
    public bool CanReverseInventoryLoss { get; set; }
    public IReadOnlyList<CurrentTreatmentSegmentViewModel> CurrentTreatmentStatus { get; set; } = [];
    public IReadOnlyList<RoomTreatmentApplicationHistoryViewModel> TreatmentApplicationHistory { get; set; } = [];
    public bool CanApplyTreatment { get; set; }
    public bool CanReverseTreatment { get; set; }
}

public sealed class RoomTreatmentApplyForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int RoomId { get; set; }
    public int TreatmentChemicalId { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
    public bool ConfirmedReview { get; set; }
}

public sealed class ReceiptTreatmentApplyForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public long ReceiptId { get; set; }
    public int TreatmentChemicalId { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
    public bool ConfirmedReview { get; set; }
}

public sealed class ReverseRoomTreatmentApplicationForm
{
    public long Id { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class TreatmentReportUploadForm
{
    public List<IFormFile> Files { get; set; } = [];
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class RemoveTreatmentReportForm
{
    public string Reason { get; set; } = "";
}

public sealed class RoomTreatmentApplyPageViewModel
{
    public RoomTreatmentApplyForm Form { get; set; } = new();
    public string Warehouse { get; set; } = "";
    public string Room { get; set; } = "";
    public int TotalBins { get; set; }
    public string? Error { get; set; }
    public bool IsReview { get; set; }
    public TreatmentChemicalOptionViewModel? SelectedTreatment { get; set; }
    public decimal EstimatedCost { get; set; }
    public IReadOnlyList<TreatmentChemicalOptionViewModel> TreatmentOptions { get; set; } = [];
    public IReadOnlyList<RoomTreatmentFruitSnapshotViewModel> Fruit { get; set; } = [];
}

public sealed class ReceiptTreatmentApplyPageViewModel
{
    public ReceiptTreatmentApplyForm Form { get; set; } = new();
    public string ReceiptNumber { get; set; } = "";
    public string Grower { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Room { get; set; } = "";
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public int TotalBins { get; set; }
    public string? Error { get; set; }
    public bool IsReview { get; set; }
    public TreatmentChemicalOptionViewModel? SelectedTreatment { get; set; }
    public decimal EstimatedCost { get; set; }
    public IReadOnlyList<TreatmentChemicalOptionViewModel> TreatmentOptions { get; set; } = [];
}

public sealed record TreatmentChemicalOptionViewModel(
    int Id,
    string ProductName,
    string? CommonName,
    string Crop,
    decimal Volume,
    string Unit,
    decimal UnitPrice,
    string Currency)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CommonName) ? ProductName : $"{CommonName} — {ProductName}";
}

public sealed record RoomTreatmentFruitSnapshotViewModel(
    string IdentityKey,
    string GrowerNumber,
    string GrowerName,
    string Variety,
    string ProductionType,
    bool? IsOrganic,
    int Bins);

public sealed record CurrentTreatmentSegmentViewModel(
    long? SegmentId,
    string IdentityKey,
    string GrowerNumber,
    string GrowerName,
    string Variety,
    string ProductionType,
    bool? IsOrganic,
    int Bins,
    string TreatmentState,
    string TreatmentSignature,
    IReadOnlyList<TreatmentApplicationSummaryViewModel> Treatments,
    long? ReceiptId = null);

public sealed record TreatmentApplicationSummaryViewModel(
    long Id,
    DateTimeOffset AppliedAt,
    string ProductName,
    string? CommonName,
    bool IsReversed);

public sealed record RoomTreatmentApplicationHistoryViewModel(
    long Id,
    DateTimeOffset AppliedAt,
    string ProductName,
    string? CommonName,
    int BinsAffected,
    string AppliedBy,
    decimal EstimatedCost,
    string Currency,
    string? Notes,
    bool IsReversed,
    DateTimeOffset? ReversedAt,
    string? ReversalReason,
    IReadOnlyList<TreatmentReportAttachmentViewModel> Attachments);

public sealed record TreatmentReportAttachmentViewModel(
    long Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset CreatedAt)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool IsPdf => string.Equals(ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
}

public sealed class RoomGrowerSummaryViewModel
{
    public string GrowerNumber { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public int CurrentBins { get; set; }
    public int CurrentLotCount { get; set; }
    public decimal? WeightedPressureLbs { get; set; }
    public int PressureRepresentedBins { get; set; }
    public decimal? WeightedStarch { get; set; }
    public int StarchRepresentedBins { get; set; }
    public IReadOnlyList<VarietyBinPresentationViewModel> Varieties { get; set; } = [];
    public IReadOnlyList<RoomLotSummaryViewModel> Lots { get; set; } = [];
}

public sealed class RoomProjectionRequest
{
    public List<string> InventoryKeys { get; set; } = [];
}

public sealed class RoomProjectionLotViewModel
{
    public string InventoryKey { get; set; } = "";
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public int CurrentBins { get; set; }
    public string GradeSummary { get; set; } = "";
    public DateTimeOffset? LastSampleDate { get; set; }
    public IReadOnlyList<string> Indicators { get; set; } = [];
}

public sealed class RoomSampleTimelineItemViewModel
{
    public DateTimeOffset SampleDate { get; set; }
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string SampleType { get; set; } = "";
    public int EnteredFruitCount { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public decimal? AverageStarch { get; set; }
    public string SizeSummary { get; set; } = "";
    public string GradeSummary { get; set; } = "";
}

public sealed class RoomCountBreakdownViewModel
{
    public string? DataWarning { get; set; }
    public RoomSummaryItemViewModel? Summary { get; set; }
    public IReadOnlyList<RoomCountBreakdownRowViewModel> Rows { get; set; } = [];
    public int IncludedBins => Rows.Where(x => x.IsIncluded).Sum(x => x.Bins);
}

public sealed class RoomCountBreakdownRowViewModel
{
    public string SourceType { get; set; } = "";
    public long? ReceiptId { get; set; }
    public string? DisplayReceiptId { get; set; }
    public string SampleType { get; set; } = "";
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public int Bins { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset Date { get; set; }
    public bool IsIncluded { get; set; }
    public string DecisionReason { get; set; } = "";
}

public sealed class RoomsPageViewModel
{
    public string? DataWarning { get; set; }
    public RoomSummaryFilterForm Filter { get; set; } = new() { RoomStatus = "All" };
    public IReadOnlyList<RoomSummaryItemViewModel> Rooms { get; set; } = [];
    public IReadOnlyList<string> FacilityOptions { get; set; } = ["All", "MCD", "WP", "EBS", "DH"];
    public IReadOnlyList<string> EbsLocationOptions { get; set; } = ["All EBS", "Evans", "Lamb", "BM"];
    public bool CanApplyTreatment { get; set; }
}

public sealed class RoomLotSummaryViewModel
{
    public string InventoryKey { get; set; } = "";
    public long? ReceiptId { get; set; }
    public long? InventoryAdjustmentId { get; set; }
    public int RoomId { get; set; }
    public int? CropYear { get; set; }
    public int? FruitProfileId { get; set; }
    public int? GrowerLotId { get; set; }
    public string Warehouse { get; set; } = "";
    public string Facility { get; set; } = "";
    public string LocationGroup { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string DisplayReceiptId { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string OrchardName { get; set; } = "";
    public string BlockName { get; set; } = "";
    public string PoolStart { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public string LotCode { get; set; } = "";
    public string VarietyCode { get; set; } = "";
    public string CanonicalVarietyKey { get; set; } = "";
    public string CanonicalVarietyName { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool? IsOrganic { get; set; }
    public string VarietyHexColor { get; set; } = "#607D8B";
    public string InventoryStatus { get; set; } = "";
    public DateTimeOffset? FirstReceivedAt { get; set; }
    public int OriginalBins { get; set; }
    public int DepletedBins { get; set; }
    public int CurrentBins { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public decimal? PressureStdDevLbs { get; set; }
    public decimal? MonthOverMonthPressureChangeLbs { get; set; }
    public decimal? AverageStarch { get; set; }
    public string DefectSummary { get; set; } = "None";
    public string GradeSummary { get; set; } = "Unavailable";
    public string SizeSummary { get; set; } = "Unavailable";
    public DateTimeOffset? LastSampleDate { get; set; }
    public string LatestQcSource { get; set; } = "";
    public int SampleCount { get; set; }
    public int EnteredFruitCount { get; set; }
    public string DepletionStatus { get; set; } = "Current";
    public IReadOnlyList<string> ReviewFlags { get; set; } = [];
    public string? WeakestReason { get; set; }
    public IReadOnlyList<RoomReceiptEvidenceLinkViewModel> ReceiptEvidence { get; set; } = [];
    public int ReceiptEvidenceCount { get; set; }
    public IReadOnlyList<RoomSampleLinkViewModel> Samples { get; set; } = [];
    public int SampleEvidenceCount { get; set; }
}

public sealed record RoomReceiptOptionViewModel(long ReceiptId, string Label, int CurrentBins);
public sealed record RoomInventoryLotOptionViewModel(
    string LotKey,
    string Label,
    int CurrentBins,
    string TreatmentSignature = "",
    string TreatmentLabel = "Untreated",
    string Grower = "",
    string Variety = "",
    long? TreatmentSegmentId = null,
    long? TreatmentReceiptId = null);
public sealed record RoomTransferFacilityViewModel(
    int WarehouseId,
    string Label,
    string WarehouseCode,
    string WarehouseName);
public sealed record RoomTransferDestinationViewModel(
    int RoomId,
    int WarehouseId,
    string Label,
    int SortOrder);
public static class RoomReceiptEvidenceTypes
{
    public const string Direct = "Direct";
    public const string TransferLinked = "Transfer-linked";
    public const string PossibleSource = "Possible source";
}

public sealed record RoomReceiptEvidenceLinkViewModel(
    long ReceiptId,
    string DisplayReceiptId,
    string EvidenceType = RoomReceiptEvidenceTypes.Direct,
    string GrowerNumber = "",
    string GrowerName = "",
    DateTimeOffset? ReceivedAt = null,
    string OriginalWarehouse = "",
    string OriginalRoom = "",
    int OriginalBins = 0,
    IReadOnlyList<long>? TransferPathIds = null);
public sealed record RoomSampleLinkViewModel(long SampleId, string DisplayReceiptId, string SampleType);

public sealed class RoomInventoryAdjustmentListItemViewModel
{
    public long Id { get; set; }
    public long? ReceiptId { get; set; }
    public string Lot { get; set; } = "";
    public string Room { get; set; } = "";
    public int? OldBinCount { get; set; }
    public int ChangeAmount { get; set; }
    public int NewBinCount { get; set; }
    public string AdjustmentType { get; set; } = "";
    public string? Source { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public string? TransferFrom { get; set; }
    public string? TransferTo { get; set; }
    public DateTimeOffset AdjustmentAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class RoomDepletionListItemViewModel
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public string DisplayReceiptId { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public int BinCount { get; set; }
    public string? Destination { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset DepletedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
}

public sealed class RoomDepletionForm
{
    public int RoomId { get; set; }
    public long ReceiptId { get; set; }
    public int BinCount { get; set; }
    public DateTimeOffset DepletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Destination { get; set; }
    public string? Notes { get; set; }
    public bool ConfirmOverDepletion { get; set; }
}

public sealed class RoomInventoryTrueUpForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int RoomId { get; set; }
    public long ReceiptId { get; set; }
    public int NewBinCount { get; set; }
    public DateTimeOffset AdjustmentAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "";
    public string? Notes { get; set; }
}

public sealed class RoomTransferForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int FromRoomId { get; set; }
    public int DestinationWarehouseId { get; set; }
    public int DestinationRoomId { get; set; }
    public string SourceLotKey { get; set; } = "";
    public string TreatmentSignature { get; set; } = "";
    public long? TreatmentSegmentId { get; set; }
    public int BinCount { get; set; }
    public DateTimeOffset TransferAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "";
    public string? Notes { get; set; }
    public bool ConfirmOverTransfer { get; set; }
}

public sealed class ReverseRoomTransferForm
{
    public long Id { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string Reason { get; set; } = "";
}

public sealed class RoomInventoryLossForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int RoomId { get; set; }
    public long InventoryAdjustmentId { get; set; }
    public string TreatmentSignature { get; set; } = "";
    public long? TreatmentSegmentId { get; set; }
    public int ExpectedCurrentBins { get; set; }
    public int BinCount { get; set; }
    public DateTimeOffset? OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
}

public sealed class ReverseRoomInventoryLossForm
{
    public long Id { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string Reason { get; set; } = "";
}

public sealed record RoomInventoryLossOptionViewModel(
    long InventoryAdjustmentId,
    string Label,
    string Facility,
    string Room,
    string Grower,
    string Lot,
    string Variety,
    string ProductionType,
    bool? IsOrganic,
    int CurrentBins,
    string TreatmentSignature = "",
    string TreatmentLabel = "Untreated",
    long? TreatmentSegmentId = null);

public sealed record RoomInventoryLossHistoryViewModel(
    long Id,
    int RoomId,
    long? ReceiptId,
    string ReceiptReference,
    string LossType,
    int BinCount,
    string Facility,
    string Room,
    string Grower,
    string Lot,
    string Variety,
    string ProductionType,
    bool? IsOrganic,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    string Reason,
    string? Notes,
    bool IsReversed,
    DateTimeOffset? ReversedAt,
    string? ReversedBy,
    string? ReverseReason);

public sealed class RoomInventoryImportPageViewModel
{
    public RoomInventoryImportForm Form { get; set; } = new();
    public RoomInventoryImportPreviewViewModel? ImportPreview { get; set; }
    public IReadOnlyList<RoomInventoryCurrentLotViewModel> CurrentLots { get; set; } = [];
    public IReadOnlyList<CurrentInventorySourceRowViewModel> CurrentLotBreakdown { get; set; } = [];
    public IReadOnlyList<string> FacilityOptions { get; set; } = ["All", "MCD", "WP", "EBS", "DH"];
    public IReadOnlyList<string> EbsLocationOptions { get; set; } = ["All EBS", "Evans", "Lamb", "BM"];
    public string CsvTemplateHeader { get; set; } = "";
    public string CsvExample { get; set; } = "";
    public string? CurrentLotWarning { get; set; }
    public InventoryDiagnosticOverviewViewModel InventoryDiagnostics { get; set; } = new();
    public bool CanManageInventoryDiagnostics { get; set; }
}

public sealed class RoomInventoryImportForm
{
    public IFormFile? CsvFile { get; set; }
    public string? CsvText { get; set; }
    public bool UseBuiltInSeed { get; set; }
    public bool ConfirmImport { get; set; }
    public bool ConfirmReplaceExistingBatch { get; set; }
    public string Facility { get; set; } = "All";
    public string EbsLocation { get; set; } = "All EBS";
    public string? RoomCode { get; set; }
    public string? LotNumber { get; set; }
    public string? Grower { get; set; }
    public string? Variety { get; set; }
}

public sealed class RoomInventoryImportPreviewViewModel
{
    public int AddCount { get; set; }
    public int UpdateCount { get; set; }
    public int ReplaceBatchCount { get; set; }
    public int UnchangedCount { get; set; }
    public int WarningCount { get; set; }
    public int DuplicateCount { get; set; }
    public int InvalidCount { get; set; }
    public string CsvText { get; set; } = "";
    public bool IsBuiltInSeed { get; set; }
    public IReadOnlyList<RoomInventoryImportPreviewRow> Rows { get; set; } = [];
    public IReadOnlyList<RoomInventoryImportRoomTotalPreview> RoomTotals { get; set; } = [];
    public bool RequiresReplaceConfirmation => ReplaceBatchCount > 0;
    public bool CanApply => DuplicateCount == 0 && InvalidCount == 0 && Rows.Any(x => x.Action is "Add" or "Update" or "Replace");
}

public sealed class RoomInventoryImportPreviewRow
{
    public int RowNumber { get; set; }
    public string Column { get; set; } = "";
    public int CropYear { get; set; }
    public string Facility { get; set; } = "";
    public string SubLocation { get; set; } = "";
    public string CropQcRoomName { get; set; } = "";
    public string CompuTechRoomCode { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string NormalizedRoomCode { get; set; } = "";
    public string Variety { get; set; } = "";
    public string LotNumber { get; set; } = "";
    public string InventoryStatus { get; set; } = "";
    public DateTimeOffset EffectiveDate { get; set; }
    public int? BinCount { get; set; }
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Action { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsWarning { get; set; }
    public int? RoomId { get; set; }
    public int? WarehouseId { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public string Grower { get; set; } = "";
    public string PoolStart { get; set; } = "";
    public int? OldBinCount { get; set; }
    public int? NewBinCount { get; set; }
}

public sealed record RoomInventoryImportRoomTotalPreview(
    int CropYear,
    string Warehouse,
    string RoomCode,
    string Variety,
    string Status,
    DateTimeOffset EffectiveDate,
    int LotCount,
    int BinCount);

public sealed class RoomInventoryCurrentLotViewModel
{
    public int RoomId { get; set; }
    public int? CropYear { get; set; }
    public string Facility { get; set; } = "";
    public string SubLocation { get; set; } = "";
    public string CropQcRoomName { get; set; } = "";
    public string CompuTechRoomCode { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string MasterRoomCode { get; set; } = "";
    public string Grower { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string LotNumber { get; set; } = "";
    public string PoolStart { get; set; } = "";
    public string Variety { get; set; } = "";
    public string InventoryStatus { get; set; } = "";
    public int CurrentBins { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset LastAdjustmentAt { get; set; }
}

public sealed class BinsRunPageViewModel
{
    public BinsRunFilterForm Filter { get; set; } = new();
    public BinsRunForm Form { get; set; } = new();
    public ActualRunForm ActualRunForm { get; set; } = new();
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public BinsRunRoomSummaryViewModel? RoomSummary { get; set; }
    public IReadOnlyList<BinsRunInventoryOptionViewModel> AvailableInventory { get; set; } = [];
    public IReadOnlyList<BinsRunHistoryItemViewModel> History { get; set; } = [];
    public IReadOnlyList<ActualRunHistoryItemViewModel> ActualRuns { get; set; } = [];
    public IReadOnlyList<ActualRunOverrideRequestViewModel> PendingOverrideRequests { get; set; } = [];
    public bool CanRecord { get; set; }
    public bool CanAdmin { get; set; }
    public bool CanTransfer { get; set; }
    public bool CanTrueUp { get; set; }
    public string CurrentEmploymentFacility { get; set; } = EmploymentFacilities.Unassigned;
    public int? ForcedRunFacilityWarehouseId { get; set; }
    public string? ForcedRunFacilityCode { get; set; }
    public bool RequiresRunFacilitySelection { get; set; }
    public IReadOnlyList<Warehouse> RunFacilityOptions { get; set; } = [];
    public int? SelectedAvailableBins { get; set; }
    public string? InventorySelectionMessage { get; set; }
    public RunProjectionPlannerViewModel Planner { get; set; } = new();
    public RoomTransferForm TransferForm { get; set; } = new();
    public RoomInventoryTrueUpForm TrueUpForm { get; set; } = new();
    public IReadOnlyList<RoomInventoryLotOptionViewModel> TransferLotOptions { get; set; } = [];
    public int TransferCurrentRoomBins { get; set; }
    public int TransferAvailableBins { get; set; }
    public bool TransferInventoryReconciles { get; set; }
    public string? TransferInventoryError { get; set; }
    public IReadOnlyList<RoomReceiptOptionViewModel> TrueUpReceiptOptions { get; set; } = [];
    public IReadOnlyList<RoomTransferFacilityViewModel> TransferDestinationFacilities { get; set; } = [];
    public IReadOnlyList<RoomTransferDestinationViewModel> TransferDestinationOptions { get; set; } = [];
    public IReadOnlyList<RoomInventoryAdjustmentListItemViewModel> InventoryActivity { get; set; } = [];
    public RunReportingPageViewModel RunReporting { get; set; } = new();
}

public sealed class BinsRunFilterForm
{
    public string Section { get; set; } = "Planner";
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public int? TransferDestinationWarehouseId { get; set; }
    public int? TransferDestinationRoomId { get; set; }
    public string? TransferTreatmentSignature { get; set; }
    public List<int> RoomIds { get; set; } = [];
    public string SelectionMode { get; set; } = ActualRunSelectionModes.ByRoom;
    public int? FruitProfileId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public long? ProjectionId { get; set; }
    public long? ProjectionSourceId { get; set; }
    public string? SourceKey { get; set; }
    public string Facility { get; set; } = "All";
    public string ProjectionVisibility { get; set; } = "Active";
    public string ProjectionSort { get; set; } = "Facility";
    public long? EditActualRunId { get; set; }
    public string? ReportFacility { get; set; }
    public int? ReportCropYear { get; set; }
    public string? ReportVarietyKey { get; set; }
    public DateOnly? ReportWeekStart { get; set; }
    public string? ReportGrowerNumber { get; set; }
    public int ReportPage { get; set; } = 1;
}

public sealed class VarietyBinPresentationViewModel
{
    public string CanonicalVarietyKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool? IsOrganic { get; set; }
    public string OrganicLabel => IsOrganic switch { true => "Organic", false => "Conventional", _ => "Organic status unavailable" };
    public string HexColor { get; set; } = "#607D8B";
    public bool IsConfiguredColor { get; set; }
    public int BinCount { get; set; }
    public decimal Percent { get; set; }
}

public static class ActualRunSelectionModes
{
    public const string ByRoom = "Room";
    public const string ByVariety = "Variety";
}

public sealed class BinsRunForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public string InventoryKey { get; set; } = "";
    public string TreatmentSignature { get; set; } = "";
    public long? TreatmentSegmentId { get; set; }
    public int BinsRun { get; set; }
    public int ExpectedAvailableBins { get; set; }
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
    public long? RunProjectionId { get; set; }
    public long? RunProjectionSourceId { get; set; }
}

public sealed class ActualRunForm
{
    public long? Id { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public long? RunProjectionId { get; set; }
    public int? RunFacilityWarehouseId { get; set; }
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
    public List<ActualRunLineForm> Lines { get; set; } = [];
}

public sealed class ActualRunLineForm
{
    public string InventoryKey { get; set; } = "";
    public string TreatmentSignature { get; set; } = "";
    public long? TreatmentSegmentId { get; set; }
    public int BinsRun { get; set; }
    public int ExpectedAvailableBins { get; set; }
    public long? RunProjectionSourceId { get; set; }
}

public sealed class CancelActualRunForm
{
    public long Id { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string Reason { get; set; } = "";
}

public sealed class ApproveActualRunOverrideForm
{
    public long RequestId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class BinsRunProjectionRequest
{
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public List<string> InventoryKeys { get; set; } = [];
}

public sealed record BinsRunInventoryOptionViewModel(
    string InventoryKey,
    long? ReceiptId,
    long? InventoryAdjustmentId,
    int WarehouseId,
    int RoomId,
    string Label,
    string Grower,
    string Lot,
    string Variety,
    string Room,
    int CurrentBins,
    string GradeSummary,
    DateTimeOffset? ReceiptDate,
    int? FruitProfileId,
    string FruitType,
    int? CanonicalOrchardBlockId,
    int? CropYear = null,
    string ProductionType = "",
    string Facility = "",
    string RoomName = "",
    int? GrowerLotId = null,
    string SourceReference = "",
    string TreatmentSignature = "",
    string TreatmentLabel = "Untreated",
    string GrowerNumber = "",
    long? TreatmentSegmentId = null,
    long? TreatmentReceiptId = null);

public sealed class ActualRunHistoryItemViewModel
{
    public long Id { get; set; }
    public long? RunProjectionId { get; set; }
    public string Status { get; set; } = "";
    public int RevisionNumber { get; set; }
    public long ConcurrencyVersion { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = "";
    public int? RunFacilityWarehouseId { get; set; }
    public string RunFacility { get; set; } = "Unresolved";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? CancellationReason { get; set; }
    public IReadOnlyList<ActualRunHistoryLineViewModel> Lines { get; set; } = [];
}

public sealed class ActualRunHistoryLineViewModel
{
    public long Id { get; set; }
    public string InventoryKey { get; set; } = "";
    public int RoomId { get; set; }
    public int? CropYear { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public string TransactionType { get; set; } = "";
    public string Room { get; set; } = "";
    public string Grower { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public int PreviousAvailableBins { get; set; }
    public int BinsRun { get; set; }
    public int NewAvailableBins { get; set; }
    public bool IsReversed { get; set; }
    public bool IsOverdrawOverride { get; set; }
    public string? OverrideReason { get; set; }
    public string TreatmentState { get; set; } = TreatmentLineageStates.Untreated;
    public string TreatmentSignature { get; set; } = "u";
    public string TreatmentSummary { get; set; } = "Untreated";
}

public sealed class ActualRunOverrideRequestViewModel
{
    public long Id { get; set; }
    public long? ActualRunId { get; set; }
    public string OperationType { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public IReadOnlyList<ActualRunOverrideLineViewModel> Lines { get; set; } = [];
}

public sealed class ActualRunOverrideLineViewModel
{
    public string Room { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public int AvailableBins { get; set; }
    public int RequestedBins { get; set; }
    public int ShortageBins { get; set; }
}

public sealed class BinsRunRoomSummaryViewModel
{
    public int WarehouseId { get; set; }
    public int RoomId { get; set; }
    public string Facility { get; set; } = "";
    public string Location { get; set; } = "";
    public string RoomName { get; set; } = "";
    public int TotalAvailableBins { get; set; }
    public int ActiveLotCount { get; set; }
    public IReadOnlyList<BinsRunSizeDistributionPoint> SizeDistribution { get; set; } = [];
    public IReadOnlyList<BinsRunGradeSummaryPoint> GradeSummary { get; set; } = [];
    public int SizeDataLotCount { get; set; }
    public int GradeDataLotCount { get; set; }
    public BinsRunProjectionViewModel Projection { get; set; } = new();
}

public sealed record BinsRunSizeDistributionPoint(int Size, decimal Percentage);
public sealed record BinsRunGradeSummaryPoint(string Grade, decimal EstimatedBins);

public sealed class BinsRunProjectionViewModel
{
    public bool IsSelection { get; set; }
    public string Label { get; set; } = "Room summary";
    public int LotCount { get; set; }
    public int AvailableBins { get; set; }
    public int SizeDataLotCount { get; set; }
    public int GradeDataLotCount { get; set; }
    public int SizeRepresentedBins { get; set; }
    public int SizeMissingBins { get; set; }
    public decimal SizeCoveragePercent { get; set; }
    public decimal SizeUnclassifiedPercent { get; set; }
    public int GradeRepresentedBins { get; set; }
    public int GradeMissingBins { get; set; }
    public IReadOnlyList<BinsRunSizeDistributionPoint> SizeDistribution { get; set; } = [];
    public IReadOnlyList<BinsRunGradeSummaryPoint> GradeSummary { get; set; } = [];
}

public sealed class BinsRunHistoryItemViewModel
{
    public long Id { get; set; }
    public string InventoryKey { get; set; } = "";
    public int WarehouseId { get; set; }
    public int RoomId { get; set; }
    public string Room { get; set; } = "";
    public string Inventory { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Variety { get; set; } = "";
    public string Lot { get; set; } = "";
    public int PreviousAvailableBins { get; set; }
    public int BinsRun { get; set; }
    public int NewAvailableBins { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public bool IsReversed { get; set; }
    public string? ReverseReason { get; set; }
    public string? Notes { get; set; }
}

public sealed class ReverseBinsRunForm
{
    public long Id { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class CurrentInventorySourceRowViewModel
{
    public string SourceType { get; set; } = "";
    public long? SourceId { get; set; }
    public int? RowNumber { get; set; }
    public string RoomCode { get; set; } = "";
    public string CompuTechRoomCode { get; set; } = "";
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public int Bins { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset? Date { get; set; }
    public bool IsIncluded { get; set; }
    public string DecisionReason { get; set; } = "";
}

public sealed class VoidRoomDepletionForm
{
    public long DepletionId { get; set; }
    public int RoomId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record MasterDataPageViewModel(
    string Title,
    string? DataWarning,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string Type = "index",
    bool CanEdit = false,
    IReadOnlyList<MasterDataEditItem> Items = null!,
    MasterDataEditForm? EditForm = null)
{
    public IReadOnlyList<MasterDataEditItem> Items { get; init; } = Items ?? [];
    public GrowerLotImportPreviewViewModel? ImportPreview { get; init; }
    public IReadOnlyList<UnmappedGrowerSourceViewModel> UnmappedGrowers { get; init; } = [];
}

public sealed record MasterDataEditItem(int Id, IReadOnlyList<string> Cells, bool IsActive, MasterDataVarietyColorViewModel? VarietyColor = null);

public sealed class MasterDataVarietyColorViewModel
{
    public string VarietyKey { get; set; } = "";
    public string VarietyName { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string HexColor { get; set; } = "";
    public string FallbackColor { get; set; } = "";
    public bool IsConfigured { get; set; }
}
public sealed class GrowerLotImportForm
{
    public IFormFile? CsvFile { get; set; }
    public string? CsvText { get; set; }
    public bool ConfirmImport { get; set; }
}

public sealed class GrowerLotImportPreviewViewModel
{
    public int AddCount { get; set; }
    public int UpdateCount { get; set; }
    public int UnchangedCount { get; set; }
    public int ConflictCount { get; set; }
    public int InvalidCount { get; set; }
    public int InactiveCount { get; set; }
    public string CsvText { get; set; } = "";
    public IReadOnlyList<GrowerLotImportPreviewRow> Rows { get; set; } = [];
    public bool CanApply => ConflictCount == 0 && InvalidCount == 0 && !string.IsNullOrWhiteSpace(CsvText);
}

public sealed record GrowerLotImportPreviewRow(int RowNumber, string Grower, string LotNumber, string PoolStart, string Action, string Message, bool IsInactive);
public sealed class UnmappedGrowerSourceViewModel
{
    public string SourceGrowerName { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Facility { get; set; } = "";
    public IReadOnlyList<int> CropYears { get; set; } = [];
    public int ReceiptCount { get; set; }
    public int LotCount { get; set; }
    public int BinsReceived { get; set; }
}

public sealed class GrowerMappingPageViewModel
{
    public GrowerMappingForm Form { get; set; } = new();
    public UnmappedGrowerSourceViewModel Source { get; set; } = new();
    public IReadOnlyList<CanonicalGrowerOptionViewModel> ExistingGrowers { get; set; } = [];
    public IReadOnlyList<CanonicalGrowerOptionViewModel> SuggestedGrowers { get; set; } = [];
    public string? AlreadyMappedTo { get; set; }
}

public sealed class CanonicalGrowerOptionViewModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string GrowerNumbers { get; set; } = "";
    public bool IsSuggested { get; set; }
    public string SuggestionReason { get; set; } = "";
}

public sealed class GrowerMappingForm
{
    public string SourceGrowerName { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Facility { get; set; } = "";
    public int? CropYear { get; set; }
    public string ReturnUrl { get; set; } = "/CropYearReview";
    public string MappingMode { get; set; } = "Existing";
    public int? CanonicalGrowerId { get; set; }
    public string NewCanonicalGrowerName { get; set; } = "";
    public bool ConfirmMapping { get; set; }
}
public sealed record AdminDownloadItem(string Name, string FileName, string Description, string Url, string Notes, bool IsAvailable = true, bool OpensInNewTab = false, string ActionText = "Open");

public sealed class AdminDownloadsViewModel
{
    public IReadOnlyList<AdminDownloadItem> Downloads { get; set; } = [];
}

public sealed class BackupStatusViewModel
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "GoogleDrive";
    public bool GoogleDriveFolderConfigured { get; set; }
    public string? GoogleDriveFolderId { get; set; }
    public string? GoogleDriveFolderDisplay { get; set; }
    public bool DatabaseBackupEnabled { get; set; }
    public bool ConfigBackupEnabled { get; set; }
    public bool PhotoManifestEnabled { get; set; }
    public int DailyRetentionDays { get; set; }
    public int WeeklyRetentionWeeks { get; set; }
    public int NightlyPacificHour { get; set; } = 1;
    public string BusinessTimeZone { get; set; } = "America/Los_Angeles";
    public DateTimeOffset? NextScheduledBackupUtc { get; set; }
    public DateTimeOffset? LastDatabaseBackupAt { get; set; }
    public DateTimeOffset? LastConfigBackupAt { get; set; }
    public DateTimeOffset? LastPhotoManifestBackupAt { get; set; }
    public string? LastDatabaseBackupFileName { get; set; }
    public string? LastConfigBackupFileName { get; set; }
    public string? LastPhotoManifestBackupFileName { get; set; }
    public string? LastError { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public BackupRunListItem? LastAttempt { get; set; }
    public BackupRunListItem? LastSuccessful { get; set; }
    public BackupRunListItem? LastSuccessfulNightly { get; set; }
    public BackupRunListItem? LastFailedNightly { get; set; }
    public IReadOnlyList<BackupRunListItem> RecentRuns { get; set; } = [];
    public IReadOnlyList<BackupNotificationListItem> RecentNotifications { get; set; } = [];
    public BackupSettingsForm SettingsForm { get; set; } = new();
}

public sealed class BackupSettingsForm
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "GoogleDrive";
    public string? GoogleDriveFolder { get; set; }
    public int DailyRetentionDays { get; set; } = 30;
    public int WeeklyRetentionWeeks { get; set; } = 52;
    public bool DatabaseBackupEnabled { get; set; } = true;
    public bool ConfigBackupEnabled { get; set; } = true;
    public bool PhotoManifestEnabled { get; set; } = true;
}

public sealed record BackupRunListItem(
    long Id,
    string BackupType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string DatabaseProvider,
    string? DeployedCommit,
    string RetentionCategory,
    string? PackageFileName,
    long? FileSizeBytes,
    string? Sha256,
    DateTimeOffset? VerifiedAt,
    string? ErrorSummary,
    string? PackageWebUrl);

public sealed record BackupNotificationListItem(
    long Id,
    long BackupRunId,
    string NotificationType,
    string Recipient,
    string Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? SentAt,
    string? ErrorSummary);

public sealed class QcStationsPageViewModel
{
    public IReadOnlyList<QcStationListItemViewModel> Stations { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public QcStationForm Form { get; set; } = new();
    public string? Search { get; set; }
    public string? WarehouseCode { get; set; }
    public string ActiveFilter { get; set; } = "Active";
}

public sealed record QcStationListItemViewModel(
    int Id,
    string StationName,
    string StationCode,
    string WarehouseCode,
    string? Description,
    bool IsActive,
    string? ApiKeyLastFour,
    DateTimeOffset? ApiKeyCreatedAt,
    DateTimeOffset? ApiKeyRotatedAt,
    DateTimeOffset? LastSeenAt,
    string? LastSeenIp,
    DateTimeOffset? LastSyncAt);

public sealed class QcStationForm
{
    public int? Id { get; set; }
    public string StationName { get; set; } = "";
    public string StationCode { get; set; } = "";
    public string WarehouseCode { get; set; } = "";
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MasterDataEditForm
{
    public string Type { get; set; } = "";
    public int? Id { get; set; }
    public int? WarehouseId { get; set; }
    public int? EndOfDayFillReportGroupId { get; set; }
    public IReadOnlyList<EndOfDayFillGroupOption> EndOfDayFillReportGroups { get; set; } = [];
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CompuTechCode { get; set; }
    public int CapacityBins { get; set; }
    public string FruitType { get; set; } = "Apple";
    public string ProductionType { get; set; } = "Conventional";
    public bool IsOrganic { get; set; }
    public decimal? Value { get; set; }
    public int? SortOrder { get; set; }
    public int? SizeCategory { get; set; }
    public decimal? MinimumWeightGrams { get; set; }
    public string? PoolStart { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<string> CommodityOptions { get; set; } = [];
    public string VarietyColorKey { get; set; } = "";
    public string CanonicalVarietyName { get; set; } = "";
    public string VarietyAliases { get; set; } = "";
    public string VarietyHexColor { get; set; } = "";
    public string VarietyFallbackColor { get; set; } = "";
    public bool VarietyColorIsConfigured { get; set; }
    public bool ResetVarietyColor { get; set; }
    public string GrowerAliases { get; set; } = "";
    public string GrowerNumbers { get; set; } = "";
    public string BlockAliases { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? CommonName { get; set; }
    public string Crop { get; set; } = "Apples";
    public string ApplicationLevel { get; set; } = string.Empty;
    public decimal? Volume { get; set; }
    public string Unit { get; set; } = "BIN";
    public decimal? UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
}

public sealed class ConfigurationPageViewModel
{
    public bool CanEdit { get; set; }
    public string? DataWarning { get; set; }
    public EmailStatusViewModel EmailStatus { get; set; } = new();
    public IReadOnlyList<ConfigurationItemViewModel> Items { get; set; } = [];
}

public sealed record ConfigurationItemViewModel(int Id, string Key, string Value, string Description, string ValueType);

public sealed class EmailStatusViewModel
{
    public string Provider { get; set; } = "None";
    public string ExpectedProviderEnvironmentVariable { get; set; } = "Email__Provider";
    public bool GmailUserEnabled { get; set; }
    public bool DefaultQcRecipientsConfigured { get; set; }
    public string? CurrentUserEmail { get; set; }
    public string? CurrentUserDomain { get; set; }
    public bool CurrentUserDomainAllowed { get; set; }
    public bool GmailCredentialPresent { get; set; }
    public bool GmailSendPermissionGranted { get; set; }
    public bool CurrentUserNeedsReconnect { get; set; }
    public string DefaultQcRecipientsSource { get; set; } = "Not configured";
    public IReadOnlyList<string> DefaultQcRecipients { get; set; } = [];
}

public sealed class ConfigurationEditForm
{
    public Dictionary<int, string> Values { get; set; } = [];
}

public sealed class UserAdminPageViewModel
{
    public string? DataWarning { get; set; }
    public IReadOnlyList<UserAdminListItem> Users { get; set; } = [];
    public IReadOnlyList<ApplicationAreaViewModel> Areas { get; set; } = [];
    public IReadOnlyList<RoleAdminListItemViewModel> Roles { get; set; } = [];
    public RoleAdminDetailViewModel? SelectedRole { get; set; }
    public RoleComparisonViewModel? RoleComparison { get; set; }
    public IReadOnlyList<EndOfDayFillGroupOption> EndOfDayFillGroups { get; set; } = [];
    public AddUserForm AddUserForm { get; set; } = new();
    public IReadOnlyList<string> EmploymentOptions { get; set; } =
        [EmploymentFacilities.Wp, EmploymentFacilities.Ebs, EmploymentFacilities.Shared, EmploymentFacilities.Unassigned];
}

public sealed record UserEmploymentHistoryViewModel(
    long Id,
    string PreviousEmploymentFacility,
    string EmploymentFacility,
    DateTimeOffset EffectiveAt,
    string ChangedBy,
    DateTimeOffset ChangedAt);
public sealed record UserAdminListItem(
    int Id,
    string Email,
    string DisplayName,
    string Domain,
    string Role,
    string RoleSummary,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    string EmploymentFacility,
    DateTimeOffset? EmploymentEffectiveAt,
    string EmploymentChangedBy,
    DateTimeOffset? EmploymentChangedAt,
    IReadOnlyList<UserEmploymentHistoryViewModel> EmploymentHistory,
    IReadOnlyList<int> EndOfDayFillGroupIds,
    bool IsOwner,
    int? RoleId);
public sealed record ApplicationAreaViewModel(string Key, string Name, string Group, string Route);
public sealed record RoleAdminListItemViewModel(
    int Id,
    string Name,
    string Description,
    bool IsSystemRole,
    bool IsActive,
    int AssignedActiveUserCount,
    bool IsAdmin,
    bool HasCompleteMatrix,
    bool IsImportedMigrationRole,
    IReadOnlyList<string> AssignedUsers);
public sealed record RoleAdminDetailViewModel(
    int Id,
    string Name,
    string Description,
    bool IsSystemRole,
    bool IsActive,
    bool IsAdmin,
    bool IsImportedMigrationRole,
    IReadOnlyList<string> AssignedUsers,
    IReadOnlyDictionary<string, PageAccessLevel> Access)
{
    public bool IsProtectedBuiltInRole =>
        IsSystemRole || BuiltInRoleNames.All.Contains(Name);

    public bool CanDelete =>
        !IsProtectedBuiltInRole && AssignedUsers.Count == 0;
}

public sealed record RoleComparisonDifferenceViewModel(
    string AreaKey,
    string AreaName,
    string Group,
    PageAccessLevel CurrentAccess,
    PageAccessLevel ComparedAccess,
    string Change);

public sealed record RoleComparisonViewModel(
    int CurrentRoleId,
    string CurrentRoleName,
    int ComparedRoleId,
    string ComparedRoleName,
    int AreasGained,
    int AreasLost,
    int UnchangedAreas,
    IReadOnlyList<RoleComparisonDifferenceViewModel> Differences);

public sealed class AddUserForm
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateUserAccessForm
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class UpdateUserEmploymentForm
{
    public int UserId { get; set; }
    public string EmploymentFacility { get; set; } = EmploymentFacilities.Unassigned;
    public DateTimeOffset? EffectiveAt { get; set; }
}

public sealed class CreateRoleForm
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? CopyFromRoleId { get; set; }
}

public sealed class UpdateRoleForm
{
    public int RoleId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DeleteRoleForm
{
    public int RoleId { get; set; }
}

public sealed record DeleteRoleResult(string? DeletedRoleName, string? Error)
{
    public bool Succeeded => Error is null;

    public static DeleteRoleResult Success(string roleName) => new(roleName, null);
    public static DeleteRoleResult Failure(string error) => new(null, error);
}

public sealed class RoleAccessMatrixForm
{
    public int RoleId { get; set; }
    public Dictionary<string, string> Access { get; set; } = [];
}

public sealed class ReceiptListViewModel
{
    public string? DataWarning { get; set; }
    public ReceiptSearchForm Search { get; set; } = new();
    public IReadOnlyList<ReceiptListItemViewModel> Receipts { get; set; } = [];
    public IReadOnlyList<ReceiptTypeCountViewModel> ReceiptTypeCounts { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public IReadOnlyList<GrowerLot> GrowerLots { get; set; } = [];
    public IReadOnlyList<int> AvailableCropYears { get; set; } = [];
    public int CurrentCropYear { get; set; }
    public string CropYearHelpText { get; set; } = "";
    public DeviceCaptureSettingsViewModel DeviceCapture { get; set; } = DeviceCaptureSettingsViewModel.Disabled;
    public bool CanQuickAddVariety { get; set; }
}

public sealed class ReceiptSearchForm
{
    public string Facility { get; set; } = "All";
    public int? CropYear { get; set; }
    public bool AllCropYears { get; set; }
    public string? DateFilter { get; set; }
    public string? SampleType { get; set; }
    public string? ReceiptType { get; set; }
    public string? ReceiptId { get; set; }
    public string? Grower { get; set; }
    public string? Lot { get; set; }
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public int? FruitProfileId { get; set; }
}

public sealed record ReceiptTypeCountViewModel(string Key, string Label, int Count, string Href);

public sealed record ReceiptListItemViewModel(
    long Id,
    int CropYear,
    DateTimeOffset ReceivedAt,
    string CompuTechReceiptId,
    string ReceiptType,
    string Warehouse,
    int RoomId,
    string Room,
    string GrowerNumber,
    string PoolStart,
    string GrowerName,
    string LotCode,
    string VarietyCode,
    int BinCount,
    int SampleCount = 0,
    string QcStatus = "",
    DateTimeOffset? LastUpdatedAt = null,
    string ProductionType = "",
    bool? IsOrganic = null,
    IReadOnlyList<VarietyBinPresentationViewModel>? Varieties = null);

public class CreateReceiptForm
{
    public int CropYear { get; set; } = DateTimeOffset.UtcNow.Year;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool ConfirmCropYear { get; set; }
    public string CompuTechReceiptId { get; set; } = "";
    public string ReceiptType { get; set; } = "Truck receipt";
    public int WarehouseId { get; set; }
    public int RoomId { get; set; }
    public int FruitProfileId { get; set; }
    public int? GrowerLotId { get; set; }
    public string GrowerNumber { get; set; } = "";
    public string PoolStart { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public string LotCode { get; set; } = "";
    public int BinCount { get; set; }
}

public sealed class StagedReceiptPhotoForm
{
    public IFormFile? PhotoFile { get; set; }
    public string PhotoType { get; set; } = "";
    public string PhotoSource { get; set; } = "Upload File";
}

public sealed record CreateReceiptResult(long? ReceiptId, string? ReceiptNumber, string? Error)
{
    public bool Succeeded => ReceiptId is not null && Error is null;
}

public sealed class EditReceiptPageViewModel
{
    public string? DataWarning { get; set; }
    public UpdateReceiptForm Form { get; set; } = new();
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public IReadOnlyList<GrowerLot> GrowerLots { get; set; } = [];
    public bool CanAdminOverride { get; set; }
    public ReceiptInventoryOverridePreviewViewModel? AdminOverridePreview { get; set; }
}

public class UpdateReceiptForm : CreateReceiptForm
{
    public long Id { get; set; }
}

public sealed class AdminReceiptInventoryOverrideForm : UpdateReceiptForm
{
    public long ExpectedConcurrencyVersion { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string Reason { get; set; } = "";
    public bool ConfirmInventoryChange { get; set; }
    public bool AcknowledgeNegativeInventory { get; set; }
}

public sealed class ReceiptInventoryOverridePreviewViewModel
{
    public long ReceiptId { get; set; }
    public long ConcurrencyVersion { get; set; }
    public int ReceiptBinCount { get; set; }
    public int CurrentInventory { get; set; }
    public int ConsumedBins { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<ReceiptInventoryBalanceViewModel> Balances { get; set; } = [];
    public int BinsRunCount { get; set; }
    public int ActualRunCount { get; set; }
    public int TransferCount { get; set; }
    public bool HasPriorOverride { get; set; }
}

public sealed record ReceiptInventoryBalanceViewModel(
    int WarehouseId,
    string Warehouse,
    int RoomId,
    string Room,
    int? CropYear,
    int? GrowerLotId,
    int? FruitProfileId,
    string Grower,
    string Lot,
    string Variety,
    string InventoryStatus,
    int CurrentBins);

public sealed record ReceiptInventoryOverrideResult(Guid? OverrideId, string? Error, bool IsConflict = false, bool WasIdempotent = false)
{
    public bool Succeeded => Error is null;
}

public sealed class ReceiptInventoryOverrideAuditViewModel
{
    public Guid Id { get; set; }
    public long ReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public bool ReceiptIsVoided { get; set; }
    public string Action { get; set; } = "";
    public string Administrator { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public int OldReceiptBins { get; set; }
    public int NewReceiptBins { get; set; }
    public int InventoryDelta { get; set; }
    public int CurrentInventoryBefore { get; set; }
    public int CurrentInventoryAfter { get; set; }
    public bool NegativeInventoryAcknowledged { get; set; }
    public string BeforeSnapshotJson { get; set; } = "";
    public string AfterSnapshotJson { get; set; } = "";
    public IReadOnlyList<ReceiptInventoryOverrideAdjustmentViewModel> Adjustments { get; set; } = [];
}

public sealed record ReceiptInventoryOverrideAdjustmentViewModel(
    long Id,
    string Facility,
    string Room,
    int? CropYear,
    string Lot,
    string Variety,
    int OldBins,
    int Change,
    int NewBins);

public sealed record VoidedReceiptAdminViewModel(
    long ReceiptId,
    string ReceiptNumber,
    int CropYear,
    DateTimeOffset? VoidedAt,
    string Reason,
    Guid OverrideId,
    string Administrator,
    int RemovedInventory);

public sealed class DeleteReceiptForm
{
    public long Id { get; set; }
    public string Reason { get; set; } = "";
    public string ConfirmationValue { get; set; } = "";
    public bool ConfirmDeletion { get; set; }
    public bool ConfirmInventoryChange { get; set; }
    public bool AcknowledgeNegativeInventory { get; set; }
    public string OperationToken { get; set; } = "";
    public long ExpectedConcurrencyVersion { get; set; }
}

public sealed class ReceiptDeletionConfirmationViewModel
{
    public long Id { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public int CropYear { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Grower { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Variety { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Room { get; set; } = "";
    public int GrossBins { get; set; }
    public int CurrentInventory { get; set; }
    public int ConsumedBins { get; set; }
    public long ConcurrencyVersion { get; set; }
    public IReadOnlyList<ReceiptInventoryBalanceViewModel> CurrentBalances { get; set; } = [];
    public ReceiptDependencyCountsViewModel Dependencies { get; set; } = new();
    public bool HasBlockingOperationalHistory { get; set; }
    public IReadOnlyList<string> BlockingReasons { get; set; } = [];
    public DeleteReceiptForm Form { get; set; } = new();
}

public sealed class ReceiptDependencyCountsViewModel
{
    public int QcSamples { get; set; }
    public int FruitReadings { get; set; }
    public int Defects { get; set; }
    public int Photos { get; set; }
    public int EmailLogs { get; set; }
    public int InventoryAdjustments { get; set; }
    public int Transfers { get; set; }
    public int DepletionsAndTrueUps { get; set; }
    public int BinsRun { get; set; }
    public int AuditRecords { get; set; }
    public int OfflineSyncItems { get; set; }
}

public sealed class StorageFacilitySummaryViewModel
{
    public string Facility { get; set; } = "";
    public int CurrentBins { get; set; }
    public int CurrentGrowerLots { get; set; }
    public int CurrentRooms { get; set; }
}

public sealed class CurrentGrowerLotsPageViewModel
{
    public string? DataWarning { get; set; }
    public CurrentGrowerLotsFilterForm Filter { get; set; } = new();
    public IReadOnlyList<CurrentGrowerLotViewModel> Lots { get; set; } = [];
    public IReadOnlyList<CurrentStorageGrowerViewModel> Growers { get; set; } = [];
    public int TotalCurrentBins => Growers.Sum(x => x.CurrentBins);
    public IReadOnlyList<int> CropYears { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public IReadOnlyList<string> GrowerOptions { get; set; } = [];
    public IReadOnlyList<string> Varieties { get; set; } = [];
}

public sealed class CurrentStorageGrowerViewModel
{
    public string GrowerNumber { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public int CurrentBins { get; set; }
    public int CurrentLotCount { get; set; }
    public int CurrentRoomCount { get; set; }
    public decimal? WeightedPressureLbs { get; set; }
    public int PressureRepresentedBins { get; set; }
    public decimal? WeightedStarch { get; set; }
    public int StarchRepresentedBins { get; set; }
    public IReadOnlyList<VarietyBinPresentationViewModel> Varieties { get; set; } = [];
    public IReadOnlyList<CurrentGrowerLotViewModel> Lots { get; set; } = [];
}

public sealed class CurrentGrowerLotsFilterForm
{
    public string Facility { get; set; } = "All";
    public int? CropYear { get; set; }
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public string? Grower { get; set; }
    public string? Variety { get; set; }
    public string? Search { get; set; }
}

public sealed class CurrentGrowerLotViewModel
{
    public int? GrowerLotId { get; set; }
    public int? CropYear { get; set; }
    public string Grower { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool? IsOrganic { get; set; }
    public string VarietyHexColor { get; set; } = "#607D8B";
    public string Warehouse { get; set; } = "";
    public string Room { get; set; } = "";
    public int CurrentBins { get; set; }
    public DateTimeOffset? FirstReceivedAt { get; set; }
    public DateTimeOffset? LastQcSampleAt { get; set; }
    public string LatestQcSource { get; set; } = "";
    public decimal? LatestAveragePressure { get; set; }
    public decimal? LatestStarch { get; set; }
    public IReadOnlyList<RoomReceiptEvidenceLinkViewModel> ReceiptEvidence { get; set; } = [];
    public int ReceiptEvidenceCount { get; set; }
}

public sealed class CropYearReviewPageViewModel
{
    public string? DataWarning { get; set; }
    public CropYearReviewFilterForm Filter { get; set; } = new();
    public IReadOnlyList<CropYearReviewGrowerViewModel> Growers { get; set; } = [];
    public IReadOnlyList<CropYearReviewRowViewModel> Rows => Growers.SelectMany(x => x.Rows).ToList();
    public IReadOnlyList<int> CropYears { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<string> GrowerOptions { get; set; } = [];
    public IReadOnlyList<string> Varieties { get; set; } = [];
}

public sealed class CropYearReviewFilterForm
{
    public int? CropYear { get; set; }
    public int? WarehouseId { get; set; }
    public string? Grower { get; set; }
    public string? Variety { get; set; }
}

public sealed class CropYearReviewGrowerViewModel
{
    public string CanonicalGrowerKey { get; set; } = "";
    public string CanonicalGrowerName { get; set; } = "";
    public bool IsMapped { get; set; }
    public IReadOnlyList<string> GrowerNumbers { get; set; } = [];
    public IReadOnlyList<string> SourceGrowerNames { get; set; } = [];
    public string SourceGrowerName { get; set; } = "";
    public string SourceGrowerNumber { get; set; } = "";
    public string SourceFacility { get; set; } = "";
    public int SourceIdentityCount { get; set; }
    public int TotalReceipts { get; set; }
    public int TotalLots { get; set; }
    public int TotalBinsReceived { get; set; }
    public int QcSampleCount { get; set; }
    public IReadOnlyList<string> Varieties { get; set; } = [];
    public IReadOnlyList<string> Warehouses { get; set; } = [];
    public DateTimeOffset? FirstSampleDate { get; set; }
    public DateTimeOffset? LastSampleDate { get; set; }
    public decimal? AveragePressure { get; set; }
    public decimal? LatestPressure { get; set; }
    public decimal? StarchAverage { get; set; }
    public IReadOnlyList<CropYearReviewRowViewModel> Rows { get; set; } = [];
}

public sealed class CropYearReviewRowViewModel
{
    public DateTimeOffset SampleDate { get; set; }
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Room { get; set; } = "";
    public string SampleType { get; set; } = "";
    public decimal? AveragePressure { get; set; }
    public decimal? PressureStdDev { get; set; }
    public decimal? StarchAverage { get; set; }
    public int EnteredFruitCount { get; set; }
    public decimal? EarliestPressure { get; set; }
    public decimal? LatestPressure { get; set; }
    public decimal? PressureChange { get; set; }
    public int? DaysBetweenSamples { get; set; }
    public decimal? PressureLossPerWeek { get; set; }
}

public sealed class ReceiptDetailViewModel
{
    public string? DataWarning { get; set; }
    public ReceiptListItemViewModel? Receipt { get; set; }
    public IReadOnlyList<SampleListItemViewModel> Samples { get; set; } = [];
    public IReadOnlyList<SampleType> SampleTypes { get; set; } = [];
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
    public bool CanDeleteSamples { get; set; }
    public DeviceCaptureSettingsViewModel DeviceCapture { get; set; } = DeviceCaptureSettingsViewModel.Disabled;
    public IReadOnlyList<ReceiptInventoryOverrideHistoryViewModel> InventoryOverrides { get; set; } = [];
    public IReadOnlyList<RoomInventoryLossHistoryViewModel> InventoryLosses { get; set; } = [];
    public int ActiveDroppedBins => InventoryLosses.Where(x => !x.IsReversed).Sum(x => x.BinCount);
    public int? CurrentPackableBins { get; set; }
    public IReadOnlyList<RoomTreatmentApplicationHistoryViewModel> TreatmentApplications { get; set; } = [];
    public bool CanApplyReceivingTreatment { get; set; }
    public bool CanReverseReceivingTreatment { get; set; }
}

public sealed record ReceiptInventoryOverrideHistoryViewModel(
    Guid Id,
    string Action,
    string Administrator,
    DateTimeOffset CreatedAt,
    string Reason,
    int OldReceiptBins,
    int NewReceiptBins,
    int InventoryDelta,
    int CurrentInventoryBefore,
    int CurrentInventoryAfter,
    bool NegativeInventoryAcknowledged,
    int LedgerAdjustmentCount);

public sealed class SampleListItemViewModel
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public int CropYear { get; set; }
    public string DisplayReceiptId { get; set; } = "";
    public string ReceiptIdText { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string SampleType { get; set; } = "";
    public string Status { get; set; } = "";
    public string StarchStatus { get; set; } = "";
    public string PhotoStatus { get; set; } = "";
    public string EmailStatus { get; set; } = "";
    public DateTimeOffset? EmailSentAt { get; set; }
    public string? EmailSentBy { get; set; }
    public string? TakenBy { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; }
    public int? ActualSampleSize { get; set; }
    public bool IsReady { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
    public IReadOnlyList<string> ReviewReasons { get; set; } = [];
    public IReadOnlyList<ReadinessChecklistItem> Checklist { get; set; } = [];
    public int CompletedFruitCount { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class SampleDetailViewModel
{
    public string? DataWarning { get; set; }
    public SampleListItemViewModel? Sample { get; set; }
    public IReadOnlyList<SampleType> SampleTypes { get; set; } = [];
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public ReadinessViewModel Readiness { get; set; } = new();
    public IReadOnlyList<Grade> Grades { get; set; } = [];
    public IReadOnlyList<StarchScaleValue> StarchScaleValues { get; set; } = [];
    public IReadOnlyList<DefectType> DefectTypes { get; set; } = [];
    public IReadOnlyList<FieldSampleSizeThreshold> SizeThresholds { get; set; } = [];
    public IReadOnlyList<int> AllowedSampleSizes { get; set; } = [];
    public int TargetSampleSize { get; set; } = 10;
    public int EnteredFruitCount { get; set; }
    public long AutosaveVersion { get; set; }
    public IReadOnlyList<QcPhotoRequirementViewModel> AvailablePhotoTypes { get; set; } = [];
    public string FruitType { get; set; } = "";
    public string DefectInspectionStatus { get; set; } = "No defects found";
    public string? RecipientEmail { get; set; }
    public SaveFruitReadingsForm FruitReadingForm { get; set; } = new();
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
    public DeviceCaptureSettingsViewModel DeviceCapture { get; set; } = DeviceCaptureSettingsViewModel.Disabled;
}

public sealed class ReceiptReportPreviewViewModel
{
    public long SampleId { get; set; }
    public long ReceiptId { get; set; }
    public string DisplayReceiptId { get; set; } = "";
    public string Recipients { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public bool CanSend { get; set; }
    public bool IsResend { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
    public IReadOnlyList<ReceiptReportSendHistoryItem> SendHistory { get; set; } = [];
}

public sealed record ReceiptReportSendHistoryItem(
    string Status,
    DateTimeOffset? SentAt,
    string Sender,
    string Recipients,
    string Subject,
    bool IsResend,
    bool IsOverride);

public sealed record ReadinessChecklistItem(string Category, string Label, string Status, string CssClass);
public sealed record QcPhotoRequirementViewModel(string PhotoType, string FriendlyName, bool IsRequired);

public sealed class StarchTestViewModel
{
    public string? DataWarning { get; set; }
    public string FruitType { get; set; } = "";
    public SampleListItemViewModel? Sample { get; set; }
    public ReceiptListItemViewModel? Receipt { get; set; }
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<StarchScaleValue> StarchScaleValues { get; set; } = [];
    public ReadinessViewModel Readiness { get; set; } = new();
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
    public SaveStarchTestForm StarchForm { get; set; } = new();
    public DeviceCaptureSettingsViewModel DeviceCapture { get; set; } = DeviceCaptureSettingsViewModel.Disabled;
    public FieldSampleQcStationStatusViewModel QcStationStatus { get; set; } = new();
}

public sealed record DeviceCaptureSettingsViewModel(bool Enabled, bool BrioEnabled, bool ObsbotEnabled, bool ScaleEnabled)
{
    public static DeviceCaptureSettingsViewModel Disabled { get; } = new(false, false, false, false);
    public bool AnyCameraEnabled => Enabled && (BrioEnabled || ObsbotEnabled);
    public bool AnyEnabled => Enabled && (BrioEnabled || ObsbotEnabled || ScaleEnabled);
}

public sealed record DeviceCapturePanelViewModel(
    DeviceCaptureSettingsViewModel Settings,
    string? ReceiptPhotoAction = null,
    string? SamplePhotoAction = null,
    string? StarchPhotoAction = null,
    bool ShowTruckPhotos = false,
    bool ShowApplePhotos = false,
    bool ShowScale = false,
    bool StageReceiptPhotos = false,
    string? RequiresSavedTargetMessage = null,
    string FruitCameraLabel = "Apple camera",
    string WholeSampleLabel = "Whole Apple Sample",
    string CutFruitLabel = "Cut Apple");

public sealed class FruitReadingRowViewModel
{
    public int RowNumber { get; set; }
    public decimal? Pressure1Lbs { get; set; }
    public decimal? Pressure2Lbs { get; set; }
    public decimal? PressureAverageLbs { get; set; }
    public decimal? WeightGrams { get; set; }
    public int? GradeId { get; set; }
    public string? Grade { get; set; }
    public int? StarchScaleValueId { get; set; }
    public string? Starch { get; set; }
    public int? SizeCategory { get; set; }
    public string SizeStatus { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string EntryStatus { get; set; } = "Empty";
    public IReadOnlyList<int> DefectTypeIds { get; set; } = [];
    public IReadOnlyList<string> Defects { get; set; } = [];
    public string? OtherDefectNotes { get; set; }
    public bool DefectsInspected { get; set; }
    public long FieldVersion { get; set; }
}

public sealed class SaveFruitReadingsForm
{
    public long SampleId { get; set; }
    public int TargetSampleSize { get; set; } = 10;
    public List<FruitReadingEditRow> Rows { get; set; } = [];
}

public sealed record SampleRefreshRowViewModel(
    int RowNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs,
    decimal? PressureAverageLbs,
    decimal? WeightGrams,
    int? GradeId,
    string? Grade,
    int? StarchScaleValueId,
    int? SizeCategory,
    string SizeStatus,
    string EntryStatus,
    IReadOnlyList<int> DefectTypeIds,
    IReadOnlyList<string> Defects,
    bool DefectsInspected,
    string? OtherDefectNotes,
    long FieldVersion);

public sealed record SampleRefreshViewModel(
    long SampleId,
    int TargetSampleSize,
    int EnteredFruitCount,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SampleRefreshRowViewModel> Rows,
    FieldSampleQcStationStatusViewModel QcStation);

public sealed class FruitReadingEditRow
{
    public int RowNumber { get; set; }
    public decimal? Pressure1Lbs { get; set; }
    public decimal? Pressure2Lbs { get; set; }
    public decimal? OriginalPressure1Lbs { get; set; }
    public decimal? OriginalPressure2Lbs { get; set; }
    public decimal? WeightGrams { get; set; }
    public decimal? OriginalWeightGrams { get; set; }
    public int? SizeCategory { get; set; }
    public int? OriginalSizeCategory { get; set; }
    public int? GradeId { get; set; }
    public int? StarchScaleValueId { get; set; }
    public List<int> DefectTypeIds { get; set; } = [];
    public string? OtherDefectNotes { get; set; }
    public bool DefectsInspected { get; set; }
}

public sealed class SaveStarchTestForm
{
    public long SampleId { get; set; }
    public List<StarchTestEditRow> Rows { get; set; } = [];
}

public sealed class StarchTestEditRow
{
    public int RowNumber { get; set; }
    public int? StarchScaleValueId { get; set; }
}

public sealed class AddPhotoMetadataForm
{
    public long? ReceiptId { get; set; }
    public long? QcSampleId { get; set; }
    public IFormFile? PhotoFile { get; set; }
    public string PhotoType { get; set; } = "";
    public string PhotoSource { get; set; } = "Upload File";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public long? FileSizeBytes { get; set; }
    public string SharePointDriveId { get; set; } = "placeholder-drive";
    public string SharePointItemId { get; set; } = "placeholder-item";
    public string? WebUrl { get; set; }
}

public sealed class CreateReceiptSampleForm
{
    public int SampleTypeId { get; set; }
}

public sealed class UpdateSampleTypeForm
{
    public long SampleId { get; set; }
    public int SampleTypeId { get; set; }
}

public sealed class OverrideSendViewModel
{
    public string? DataWarning { get; set; }
    public SampleListItemViewModel? Sample { get; set; }
    public ReceiptListItemViewModel? Receipt { get; set; }
    public ReadinessViewModel Readiness { get; set; } = new();
    public IReadOnlyList<ReadinessChecklistItem> Checklist { get; set; } = [];
    public string? SenderEmail { get; set; }
    public string? SenderDomain { get; set; }
    public bool SenderDomainAllowed { get; set; }
    public string? RecipientEmail { get; set; }
    public bool GmailReconnectRequired { get; set; }
    public bool GmailCredentialPresent { get; set; }
    public bool GmailSendPermissionGranted { get; set; }
    public bool GmailUserProviderEnabled { get; set; }
    public string AllowedGoogleDomains { get; set; } = "";
    public OverrideSendForm Form { get; set; } = new();
}

public sealed class DeleteSampleConfirmationViewModel
{
    public string? DataWarning { get; set; }
    public long SampleId { get; set; }
    public long ReceiptId { get; set; }
    public int CropYear { get; set; }
    public string DisplayReceiptId { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public string LotCode { get; set; } = "";
    public string VarietyCode { get; set; } = "";
    public string SampleType { get; set; } = "";
    public int PhotoCount { get; set; }
    public string EmailStatus { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class DataCleanupViewModel
{
    public string? DataWarning { get; set; }
    public DataCleanupFilterForm Filter { get; set; } = new();
    public DataCleanupPreviewViewModel Preview { get; set; } = new();
    public IReadOnlyList<int> AvailableCropYears { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<SampleType> SampleTypes { get; set; } = [];
    public string EnvironmentName { get; set; } = "";
    public string DatabaseProvider { get; set; } = "";
}

public sealed class DataCleanupFilterForm
{
    public int? CropYear { get; set; }
    public bool AllCropYears { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? WarehouseId { get; set; }
    public int? SampleTypeId { get; set; }
    public string? ReceiptId { get; set; }
    public bool IncludeEmailedSamples { get; set; }
    public bool IncludeDeletedSamples { get; set; }
    public bool IncludePhotoMetadata { get; set; }
    public bool IncludeReceiptsWithoutSamples { get; set; }
    public string CleanupMode { get; set; } = "Soft";
    public string ConfirmationText { get; set; } = "";
    public string? Reason { get; set; }
}

public sealed class DataCleanupPreviewViewModel
{
    public int ReceiptsAffected { get; set; }
    public int SamplesAffected { get; set; }
    public int FruitRowsAffected { get; set; }
    public int PhotoRecordsAffected { get; set; }
    public int EmailLogsAffected { get; set; }
    public int DriveFilesAffected { get; set; }
    public bool IsProduction { get; set; }
    public bool IsAllCropYears { get; set; }
}

public sealed class VarietyColorsAdminViewModel
{
    public string? DataWarning { get; set; }
    public IReadOnlyList<VarietyColorRowViewModel> Varieties { get; set; } = [];
    public bool CanManage { get; set; }
}

public sealed class VarietyColorRowViewModel
{
    public string VarietyKey { get; set; } = "";
    public string VarietyName { get; set; } = "";
    public string HexColor { get; set; } = "";
    public string FallbackColor { get; set; } = "";
    public bool IsConfigured { get; set; }
    public int HistoricalProfileCount { get; set; }
    public int CurrentBins { get; set; }
}

public sealed class VarietyColorForm
{
    public string VarietyKey { get; set; } = "";
    public string VarietyName { get; set; } = "";
    public string HexColor { get; set; } = "";
}

public sealed class OverrideSendForm
{
    public long SampleId { get; set; }
    public string OverrideReason { get; set; } = "";
    public bool ConfirmOverride { get; set; }
}

public sealed class PhotoPlaceholderFormViewModel
{
    public string FormAction { get; set; } = "";
    public string Title { get; set; } = "Add Photo";
    public string DefaultPhotoType { get; set; } = "";
    public IReadOnlyList<string> PhotoTypes { get; set; } = [];
    public int CropYear { get; set; }
    public string Warehouse { get; set; } = "";
    public string ReceiptId { get; set; } = "";
    public bool AllowMultiple { get; set; }
    public string FileInputName { get; set; } = "PhotoFile";
    public string WholeSampleLabel { get; set; } = "Whole Apple Sample";
    public string CutFruitLabel { get; set; } = "Cut Apple";
}

public sealed record PhotoMetadataViewModel(long Id, long? QcSampleId, long? DeleteFromSampleId, string PhotoType, string PhotoSource, string FileName, string ContentType, long? FileSizeBytes, string? WebUrl, DateTimeOffset CapturedAt, bool CanDelete, string? DeleteAction = null, bool DisplayAsThumbnail = false, string? ThumbnailUrl = null);
public sealed record PhotoGroupViewModel(string PhotoType, IReadOnlyList<PhotoMetadataViewModel> Photos);

public sealed class ReadinessViewModel
{
    public bool IsReady { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
    public IReadOnlyList<ReadinessChecklistItem> Checklist { get; set; } = [];
    public int CompletedFruitCount { get; set; }
    public int StarchMissingCount { get; set; }
    public bool HasBinTruck { get; set; }
    public bool HasSampleBeforeCutting { get; set; }
    public bool HasCutFruit { get; set; }
    public bool HasFruitAfterStarch { get; set; }
    public IReadOnlyList<ReadinessChecklistItem> RequiredPhotoChecklist { get; set; } = [];
}

public sealed class DailyQcDashboardViewModel
{
    public string? DataWarning { get; set; }
    public string Facility { get; set; } = "All";
    public int? WarehouseId { get; set; }
    public string? Status { get; set; }
    public string? StatusDescription { get; set; }
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<SampleListItemViewModel> Samples { get; set; } = [];
}
