namespace CropQc.Web.Models;

public sealed class InventoryByVarietyPageViewModel
{
    public string Facility { get; set; } = "All";
    public IReadOnlyList<string> FacilityOptions { get; set; } = [];
    public IReadOnlyList<InventoryVarietyCardViewModel> Varieties { get; set; } = [];
    public int TotalCurrentBins => Varieties.Sum(x => x.CurrentBins);
}

public sealed record InventoryVarietyCardViewModel(
    string VarietyKey,
    string VarietyName,
    string HexColor,
    int CurrentBins,
    int RoomCount,
    IReadOnlyList<string> GrowerNumbers,
    IReadOnlyList<InventoryVarietyBreakdownViewModel> Breakdowns);

public sealed record InventoryVarietyBreakdownViewModel(
    string ProductionType,
    string OrganicStatus,
    int CurrentBins);

public sealed class InventoryVarietyDetailPageViewModel
{
    public string Facility { get; set; } = "All";
    public string VarietyKey { get; set; } = "";
    public string VarietyName { get; set; } = "";
    public string HexColor { get; set; } = "#607D8B";
    public IReadOnlyList<InventoryVarietyDetailLineViewModel> Lines { get; set; } = [];
    public int TotalCurrentBins => Lines.Sum(x => x.CurrentBins);
}

public sealed record InventoryVarietyDetailLineViewModel(
    int WarehouseId,
    string Facility,
    int RoomId,
    string Room,
    string? GrowerNumber,
    string GrowerName,
    int? GrowerLotId,
    long? ReceiptId,
    string? ReceiptNumber,
    string SourceReference,
    string ProductionType,
    string OrganicStatus,
    string InventoryStatus,
    string TreatmentStatus,
    int CurrentBins);
