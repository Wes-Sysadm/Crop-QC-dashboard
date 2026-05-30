using CropQc.Data.Entities;

namespace CropQc.Web.Models;

public sealed record StatusCountCard(string Label, int Count, string Href, string CssClass);

public sealed class HomeDashboardViewModel
{
    public string? DataWarning { get; set; }
    public IReadOnlyList<StatusCountCard> Cards { get; set; } = [];
    public IReadOnlyList<SampleListItemViewModel> TodaySamples { get; set; } = [];
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
}

public sealed record MasterDataEditItem(int Id, IReadOnlyList<string> Cells, bool IsActive);
public sealed record AdminDownloadItem(string Name, string FileName, string Description, string Url, string Notes);

public sealed class AdminDownloadsViewModel
{
    public IReadOnlyList<AdminDownloadItem> Downloads { get; set; } = [];
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
    public int CapacityBins { get; set; }
    public string FruitType { get; set; } = "Apple";
    public string ProductionType { get; set; } = "Conventional";
    public bool IsOrganic { get; set; }
    public decimal? Value { get; set; }
    public int? SortOrder { get; set; }
    public int? SizeCategory { get; set; }
    public decimal? MinimumWeightGrams { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<string> CommodityOptions { get; set; } = [];
}

public sealed class ConfigurationPageViewModel
{
    public bool CanEdit { get; set; }
    public string? DataWarning { get; set; }
    public IReadOnlyList<ConfigurationItemViewModel> Items { get; set; } = [];
}

public sealed record ConfigurationItemViewModel(int Id, string Key, string Value, string Description, string ValueType);

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
}

public sealed class ReceiptSearchForm
{
    public int? CropYear { get; set; }
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
    string Warehouse,
    string Room,
    string GrowerName,
    string LotCode,
    string VarietyCode,
    int BinCount);

public sealed class CreateReceiptForm
{
    public int CropYear { get; set; } = DateTimeOffset.Now.Year;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;
    public string CompuTechReceiptId { get; set; } = "";
    public int WarehouseId { get; set; }
    public int RoomId { get; set; }
    public int FruitProfileId { get; set; }
    public string GrowerName { get; set; } = "";
    public string LotCode { get; set; } = "";
    public int BinCount { get; set; }
}

public sealed class ReceiptDetailViewModel
{
    public string? DataWarning { get; set; }
    public ReceiptListItemViewModel? Receipt { get; set; }
    public IReadOnlyList<SampleListItemViewModel> Samples { get; set; } = [];
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
}

public sealed class SampleListItemViewModel
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public int CropYear { get; set; }
    public string DisplayReceiptId { get; set; } = "";
    public string ReceiptIdText { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Status { get; set; } = "";
    public string StarchStatus { get; set; } = "";
    public string PhotoStatus { get; set; } = "";
    public string EmailStatus { get; set; } = "";
    public string? TakenBy { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; }
    public int? ActualSampleSize { get; set; }
    public bool IsReady { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
    public IReadOnlyList<ReadinessChecklistItem> Checklist { get; set; } = [];
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
    public SaveFruitReadingsForm FruitReadingForm { get; set; } = new();
    public AddPhotoMetadataForm AddPhotoForm { get; set; } = new();
}

public sealed record ReadinessChecklistItem(string Category, string Label, string Status, string CssClass);

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
    public IReadOnlyList<int> DefectTypeIds { get; set; } = [];
    public IReadOnlyList<string> Defects { get; set; } = [];
    public string? OtherDefectNotes { get; set; }
}

public sealed class SaveFruitReadingsForm
{
    public long SampleId { get; set; }
    public List<FruitReadingEditRow> Rows { get; set; } = [];
}

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

public sealed class OverrideSendViewModel
{
    public string? DataWarning { get; set; }
    public SampleListItemViewModel? Sample { get; set; }
    public ReceiptListItemViewModel? Receipt { get; set; }
    public ReadinessViewModel Readiness { get; set; } = new();
    public IReadOnlyList<ReadinessChecklistItem> Checklist { get; set; } = [];
    public OverrideSendForm Form { get; set; } = new();
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

public sealed record PhotoMetadataViewModel(string PhotoType, string PhotoSource, string FileName, string ContentType, long? FileSizeBytes, string? WebUrl, DateTimeOffset CapturedAt);
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
}

public sealed class DailyQcDashboardViewModel
{
    public string? DataWarning { get; set; }
    public int? WarehouseId { get; set; }
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<SampleListItemViewModel> Samples { get; set; } = [];
}
