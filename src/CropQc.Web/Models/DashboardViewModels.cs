using CropQc.Data.Entities;

namespace CropQc.Web.Models;

public sealed record StatusCountCard(string Label, int Count, string Href, string CssClass);

public sealed class HomeDashboardViewModel
{
    public string? DataWarning { get; set; }
    public IReadOnlyList<StatusCountCard> Cards { get; set; } = [];
    public IReadOnlyList<SampleListItemViewModel> TodaySamples { get; set; } = [];
}

public sealed record MasterDataPageViewModel(string Title, string? DataWarning, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

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
}

public sealed class SampleListItemViewModel
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
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
}

public sealed class SampleDetailViewModel
{
    public string? DataWarning { get; set; }
    public SampleListItemViewModel? Sample { get; set; }
    public IReadOnlyList<FruitReadingRowViewModel> FruitRows { get; set; } = [];
    public IReadOnlyList<PhotoGroupViewModel> PhotoGroups { get; set; } = [];
    public ReadinessViewModel Readiness { get; set; } = new();
}

public sealed record FruitReadingRowViewModel(
    int RowNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs,
    decimal? PressureAverageLbs,
    decimal? WeightGrams,
    string? Grade,
    string? Starch,
    int? SizeCategory,
    string SizeStatus,
    bool IsCompleted,
    IReadOnlyList<string> Defects);

public sealed record PhotoMetadataViewModel(string PhotoType, string PhotoSource, string FileName, string ContentType, long? FileSizeBytes, string? WebUrl, DateTimeOffset CapturedAt);
public sealed record PhotoGroupViewModel(string PhotoType, IReadOnlyList<PhotoMetadataViewModel> Photos);

public sealed class ReadinessViewModel
{
    public bool IsReady { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
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
