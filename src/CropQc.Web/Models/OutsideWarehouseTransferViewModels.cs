namespace CropQc.Web.Models;

public sealed class OutsideWarehouseTransferForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int? OutsideWarehouseId { get; set; }
    public string SourceKey { get; set; } = "";
    public int ExpectedAvailableBins { get; set; }
    public int BinCount { get; set; }
    public DateTime TransferredAt { get; set; } = DateTime.Now;
    public string? TruckLoadBolNumber { get; set; }
    public string? Notes { get; set; }
    public bool ConfirmedReview { get; set; }
}

public sealed class OutsideWarehouseTransferReversalForm
{
    public long TransferId { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? Reason { get; set; }
}

public sealed record OutsideWarehouseOptionViewModel(int Id, string Code, string Name, bool IsActive);

public sealed record OutsideWarehouseInventoryOptionViewModel(
    string SourceKey,
    int WarehouseId,
    string Facility,
    int RoomId,
    string Room,
    int? CropYear,
    int? GrowerLotId,
    int? FruitProfileId,
    string GrowerName,
    string? GrowerNumber,
    string LotNumber,
    string VarietyCode,
    string VarietyName,
    string ProductionType,
    bool? IsOrganic,
    string InventoryStatus,
    string TreatmentState,
    string TreatmentSignature,
    string TreatmentSummary,
    int AvailableBins,
    long SourceInventoryAdjustmentId,
    long? ReceiptId,
    bool IsRoomSealed,
    long? TreatmentSegmentId);

public sealed record OutsideWarehouseTransferHistoryViewModel(
    long Id,
    DateTimeOffset TransferredAt,
    string OutsideWarehouse,
    string OutsideWarehouseCode,
    string Facility,
    string Room,
    string? GrowerNumber,
    string GrowerName,
    string Lot,
    string Variety,
    string ProductionType,
    int Bins,
    string? TruckLoadBolNumber,
    string RecordedBy,
    bool IsReversed);

public sealed class OutsideWarehouseTransferPageViewModel
{
    public OutsideWarehouseTransferForm Form { get; set; } = new();
    public IReadOnlyList<OutsideWarehouseOptionViewModel> OutsideWarehouses { get; set; } = [];
    public IReadOnlyList<OutsideWarehouseOptionViewModel> ReportOutsideWarehouses { get; set; } = [];
    public IReadOnlyList<OutsideWarehouseInventoryOptionViewModel> Inventory { get; set; } = [];
    public IReadOnlyList<OutsideWarehouseTransferHistoryViewModel> History { get; set; } = [];
    public OutsideWarehouseInventoryOptionViewModel? ReviewSource { get; set; }
    public bool CanCreate { get; set; }
    public bool CanAdmin { get; set; }
    public string? Error { get; set; }
}

public sealed class OutsideWarehouseTransferDetailViewModel
{
    public long Id { get; set; }
    public DateTimeOffset TransferredAt { get; set; }
    public string OutsideWarehouse { get; set; } = "";
    public string OutsideWarehouseCode { get; set; } = "";
    public string? OutsideWarehouseAddress { get; set; }
    public string Facility { get; set; } = "";
    public string Room { get; set; } = "";
    public long? ReceiptId { get; set; }
    public long? SourceInventoryAdjustmentId { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public int? CropYear { get; set; }
    public string? GrowerNumber { get; set; }
    public string GrowerName { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public string OrganicStatus { get; set; } = "";
    public string InventoryStatus { get; set; } = "";
    public string Treatment { get; set; } = "";
    public int Bins { get; set; }
    public string? TruckLoadBolNumber { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = "";
    public bool IsReversed { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReverseReason { get; set; }
    public bool CanAdmin { get; set; }
}
