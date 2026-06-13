using CropQc.Data.Entities;
using Microsoft.AspNetCore.Http;

namespace CropQc.Web.Models;

public sealed record StatusCountCard(string Label, int Count, string Href, string CssClass, string HelperText);

public sealed class HomeDashboardViewModel
{
    public string? DataWarning { get; set; }
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
    public string Status { get; set; } = "Empty";
    public int CurrentLotsCount { get; set; }
    public int? CurrentBinsCount { get; set; }
    public string LotSummary { get; set; } = "Empty";
    public decimal? AveragePressureLbs { get; set; }
    public decimal? PressureStdDevLbs { get; set; }
    public decimal? MonthOverMonthPressureChangeLbs { get; set; }
    public decimal? AverageStarch { get; set; }
    public string DefectSummary { get; set; } = "None";
    public DateTimeOffset? LastSampleDate { get; set; }
    public int SampleCount { get; set; }
    public int EnteredFruitCount { get; set; }
    public IReadOnlyList<string> ReviewFlags { get; set; } = [];
    public string? WeakestLotLabel { get; set; }
    public string? WeakestLotReason { get; set; }
    public long? WeakestLotReceiptId { get; set; }
}

public sealed class RoomDetailViewModel
{
    public string? DataWarning { get; set; }
    public RoomSummaryItemViewModel? Summary { get; set; }
    public IReadOnlyList<RoomLotSummaryViewModel> CurrentLots { get; set; } = [];
    public IReadOnlyList<RoomLotSummaryViewModel> DepletedLots { get; set; } = [];
    public IReadOnlyList<RoomDepletionListItemViewModel> Depletions { get; set; } = [];
    public IReadOnlyList<RoomInventoryAdjustmentListItemViewModel> InventoryAdjustments { get; set; } = [];
    public IReadOnlyList<RoomReceiptOptionViewModel> DepletionReceiptOptions { get; set; } = [];
    public RoomDepletionForm DepletionForm { get; set; } = new();
    public RoomInventoryTrueUpForm TrueUpForm { get; set; } = new();
    public bool CanManageDepletions { get; set; }
}

public sealed class RoomLotSummaryViewModel
{
    public long? ReceiptId { get; set; }
    public long? InventoryAdjustmentId { get; set; }
    public int RoomId { get; set; }
    public string Warehouse { get; set; } = "";
    public string Facility { get; set; } = "";
    public string LocationGroup { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string DisplayReceiptId { get; set; } = "";
    public string GrowerNumber { get; set; } = "";
    public string PoolStart { get; set; } = "";
    public string GrowerName { get; set; } = "";
    public string LotCode { get; set; } = "";
    public string VarietyCode { get; set; } = "";
    public int OriginalBins { get; set; }
    public int DepletedBins { get; set; }
    public int CurrentBins { get; set; }
    public decimal? AveragePressureLbs { get; set; }
    public decimal? PressureStdDevLbs { get; set; }
    public decimal? MonthOverMonthPressureChangeLbs { get; set; }
    public decimal? AverageStarch { get; set; }
    public string DefectSummary { get; set; } = "None";
    public DateTimeOffset? LastSampleDate { get; set; }
    public int SampleCount { get; set; }
    public int EnteredFruitCount { get; set; }
    public string DepletionStatus { get; set; } = "Current";
    public IReadOnlyList<string> ReviewFlags { get; set; } = [];
    public string? WeakestReason { get; set; }
    public IReadOnlyList<RoomSampleLinkViewModel> Samples { get; set; } = [];
}

public sealed record RoomReceiptOptionViewModel(long ReceiptId, string Label, int CurrentBins);
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
    public DateTimeOffset DepletedAt { get; set; } = DateTimeOffset.Now;
    public string? Destination { get; set; }
    public string? Notes { get; set; }
    public bool ConfirmOverDepletion { get; set; }
}

public sealed class RoomInventoryTrueUpForm
{
    public int RoomId { get; set; }
    public long ReceiptId { get; set; }
    public int NewBinCount { get; set; }
    public DateTimeOffset AdjustmentAt { get; set; } = DateTimeOffset.Now;
    public string Reason { get; set; } = "";
    public string? Notes { get; set; }
}

public sealed class RoomInventoryImportPageViewModel
{
    public RoomInventoryImportForm Form { get; set; } = new();
    public RoomInventoryImportPreviewViewModel? ImportPreview { get; set; }
    public IReadOnlyList<RoomInventoryCurrentLotViewModel> CurrentLots { get; set; } = [];
    public IReadOnlyList<string> FacilityOptions { get; set; } = ["All", "MCD", "WP", "EBS", "DH"];
    public IReadOnlyList<string> EbsLocationOptions { get; set; } = ["All EBS", "Evans", "Lamb", "BM"];
}

public sealed class RoomInventoryImportForm
{
    public IFormFile? CsvFile { get; set; }
    public string? CsvText { get; set; }
    public bool UseBuiltInSeed { get; set; }
    public bool ConfirmImport { get; set; }
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
    public int UnchangedCount { get; set; }
    public int WarningCount { get; set; }
    public int DuplicateCount { get; set; }
    public int InvalidCount { get; set; }
    public string CsvText { get; set; } = "";
    public bool IsBuiltInSeed { get; set; }
    public IReadOnlyList<RoomInventoryImportPreviewRow> Rows { get; set; } = [];
    public bool CanApply => DuplicateCount == 0 && InvalidCount == 0 && Rows.Any(x => x.Action is "Add" or "Update");
}

public sealed class RoomInventoryImportPreviewRow
{
    public int RowNumber { get; set; }
    public string Facility { get; set; } = "";
    public string SubLocation { get; set; } = "";
    public string CropQcRoomName { get; set; } = "";
    public string CompuTechRoomCode { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string NormalizedRoomCode { get; set; } = "";
    public string Variety { get; set; } = "";
    public string LotNumber { get; set; } = "";
    public int? BinCount { get; set; }
    public string Source { get; set; } = "";
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

public sealed class RoomInventoryCurrentLotViewModel
{
    public int RoomId { get; set; }
    public string Facility { get; set; } = "";
    public string SubLocation { get; set; } = "";
    public string CropQcRoomName { get; set; } = "";
    public string CompuTechRoomCode { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string MasterRoomCode { get; set; } = "";
    public string Grower { get; set; } = "";
    public string LotNumber { get; set; } = "";
    public string PoolStart { get; set; } = "";
    public string Variety { get; set; } = "";
    public int CurrentBins { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset LastAdjustmentAt { get; set; }
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
}

public sealed record MasterDataEditItem(int Id, IReadOnlyList<string> Cells, bool IsActive);
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
    public int RetentionDays { get; set; }
    public int ScheduleUtcHour { get; set; }
    public DateTimeOffset? NextScheduledBackupUtc { get; set; }
    public DateTimeOffset? LastDatabaseBackupAt { get; set; }
    public DateTimeOffset? LastConfigBackupAt { get; set; }
    public DateTimeOffset? LastPhotoManifestBackupAt { get; set; }
    public string? LastDatabaseBackupFileName { get; set; }
    public string? LastConfigBackupFileName { get; set; }
    public string? LastPhotoManifestBackupFileName { get; set; }
    public string? LastError { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public BackupSettingsForm SettingsForm { get; set; } = new();
}

public sealed class BackupSettingsForm
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "GoogleDrive";
    public string? GoogleDriveFolder { get; set; }
    public int RetentionDays { get; set; } = 90;
    public int ScheduleUtcHour { get; set; } = 10;
    public bool DatabaseBackupEnabled { get; set; } = true;
    public bool ConfigBackupEnabled { get; set; } = true;
    public bool PhotoManifestEnabled { get; set; } = true;
}

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
    public IReadOnlyList<RoleOptionViewModel> Roles { get; set; } = [];
    public IReadOnlyList<RolePermissionViewModel> RolePermissions { get; set; } = [];
    public AddUserForm AddUserForm { get; set; } = new();
}

public sealed record UserAdminListItem(int Id, string Email, string DisplayName, string Domain, string Role, string RoleSummary, bool IsActive, DateTimeOffset? LastLoginAt);
public sealed record RoleOptionViewModel(int Id, string Name, string Summary);
public sealed record RolePermissionViewModel(string Permission, string Admin, string Manager, string QcUser, string Viewer);

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

public sealed class ReceiptListViewModel
{
    public string? DataWarning { get; set; }
    public ReceiptSearchForm Search { get; set; } = new();
    public IReadOnlyList<ReceiptListItemViewModel> Receipts { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public IReadOnlyList<FruitProfile> FruitProfiles { get; set; } = [];
    public IReadOnlyList<GrowerLot> GrowerLots { get; set; } = [];
    public IReadOnlyList<int> AvailableCropYears { get; set; } = [];
    public int CurrentCropYear { get; set; }
    public string CropYearHelpText { get; set; } = "";
}

public sealed class ReceiptSearchForm
{
    public int? CropYear { get; set; }
    public bool AllCropYears { get; set; }
    public string? DateFilter { get; set; }
    public string? SampleType { get; set; }
    public string? ReceiptId { get; set; }
    public string? Grower { get; set; }
    public string? Lot { get; set; }
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public int? FruitProfileId { get; set; }
}

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
    DateTimeOffset? LastUpdatedAt = null);

public sealed class CreateReceiptForm
{
    public int CropYear { get; set; } = DateTimeOffset.Now.Year;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;
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
    public IReadOnlyList<int> CropYears { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public IReadOnlyList<string> Growers { get; set; } = [];
    public IReadOnlyList<string> Varieties { get; set; } = [];
}

public sealed class CurrentGrowerLotsFilterForm
{
    public int? CropYear { get; set; }
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public string? Grower { get; set; }
    public string? Variety { get; set; }
    public string? Search { get; set; }
}

public sealed class CurrentGrowerLotViewModel
{
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Room { get; set; } = "";
    public int CurrentBins { get; set; }
    public DateTimeOffset? FirstReceivedAt { get; set; }
    public DateTimeOffset? LastQcSampleAt { get; set; }
    public decimal? LatestAveragePressure { get; set; }
    public decimal? LatestStarch { get; set; }
}

public sealed class CropYearReviewPageViewModel
{
    public string? DataWarning { get; set; }
    public CropYearReviewFilterForm Filter { get; set; } = new();
    public IReadOnlyList<CropYearReviewRowViewModel> Rows { get; set; } = [];
    public IReadOnlyList<int> CropYears { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<string> Growers { get; set; } = [];
    public IReadOnlyList<string> Varieties { get; set; } = [];
}

public sealed class CropYearReviewFilterForm
{
    public int? CropYear { get; set; }
    public int? WarehouseId { get; set; }
    public string? Grower { get; set; }
    public string? Variety { get; set; }
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
}

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
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public ReadinessViewModel Readiness { get; set; } = new();
    public IReadOnlyList<Grade> Grades { get; set; } = [];
    public IReadOnlyList<StarchScaleValue> StarchScaleValues { get; set; } = [];
    public IReadOnlyList<DefectType> DefectTypes { get; set; } = [];
    public IReadOnlyList<int> AllowedSampleSizes { get; set; } = [];
    public int TargetSampleSize { get; set; } = 10;
    public int EnteredFruitCount { get; set; }
    public IReadOnlyList<QcPhotoRequirementViewModel> AvailablePhotoTypes { get; set; } = [];
    public string? RecipientEmail { get; set; }
    public SaveFruitReadingsForm FruitReadingForm { get; set; } = new();
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
}

public sealed record ReadinessChecklistItem(string Category, string Label, string Status, string CssClass);
public sealed record QcPhotoRequirementViewModel(string PhotoType, string FriendlyName, bool IsRequired);

public sealed class StarchTestViewModel
{
    public string? DataWarning { get; set; }
    public SampleListItemViewModel? Sample { get; set; }
    public ReceiptListItemViewModel? Receipt { get; set; }
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<StarchScaleValue> StarchScaleValues { get; set; } = [];
    public ReadinessViewModel Readiness { get; set; } = new();
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
    public SaveStarchTestForm StarchForm { get; set; } = new();
}

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
    int? SizeCategory,
    string SizeStatus,
    string EntryStatus,
    IReadOnlyList<string> Defects);

public sealed record SampleRefreshViewModel(
    long SampleId,
    int TargetSampleSize,
    int EnteredFruitCount,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SampleRefreshRowViewModel> Rows);

public sealed class FruitReadingEditRow
{
    public int RowNumber { get; set; }
    public decimal? Pressure1Lbs { get; set; }
    public decimal? Pressure2Lbs { get; set; }
    public decimal? WeightGrams { get; set; }
    public int? GradeId { get; set; }
    public int? StarchScaleValueId { get; set; }
    public List<int> DefectTypeIds { get; set; } = [];
    public string? OtherDefectNotes { get; set; }
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
}

public sealed record PhotoMetadataViewModel(long Id, long? QcSampleId, long? DeleteFromSampleId, string PhotoType, string PhotoSource, string FileName, string ContentType, long? FileSizeBytes, string? WebUrl, DateTimeOffset CapturedAt, bool CanDelete);
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
    public int? WarehouseId { get; set; }
    public string? Status { get; set; }
    public string? StatusDescription { get; set; }
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<SampleListItemViewModel> Samples { get; set; } = [];
}
