using CropQc.Data.Entities;

namespace CropQc.Web.Models;

public sealed class ProcessorShipmentForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int? ProcessorId { get; set; }
    public decimal? SaleRate { get; set; }
    public string PricingBasis { get; set; } = ProcessorPricingBases.PerTon;
    public string Currency { get; set; } = "USD";
    public DateTime ShippedAt { get; set; } = DateTime.Now;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public bool ConfirmedReview { get; set; }
    public List<ProcessorShipmentLineForm> Lines { get; set; } = [];
}

public sealed class ProcessorShipmentLineForm
{
    public string SourceKey { get; set; } = "";
    public int ExpectedAvailableBins { get; set; }
    public int BinsSent { get; set; }
}

public sealed class ProcessorShipmentPriceCorrectionForm
{
    public long ShipmentId { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public decimal? SaleRate { get; set; }
    public string PricingBasis { get; set; } = ProcessorPricingBases.PerTon;
    public string? Reason { get; set; }
}

public sealed class ProcessorShipmentReversalForm
{
    public long ShipmentId { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? Reason { get; set; }
}

public sealed record ProcessorOptionViewModel(int Id, string Name, string? Code);

public sealed record ProcessorInventoryOptionViewModel(
    string SourceKey, int WarehouseId, string Facility, int RoomId, string Room,
    int? CropYear, int? GrowerLotId, int? FruitProfileId, string GrowerName,
    string? GrowerNumber, string LotNumber, string VarietyCode, string VarietyName,
    string FruitType, string ProductionType, bool? IsOrganic, string InventoryStatus,
    string TreatmentState, string TreatmentSignature, string TreatmentSummary,
    int AvailableBins, long SourceInventoryAdjustmentId, long? ReceiptId,
    decimal? PoundsPerBin,
    bool IsRoomSealed = false);

public sealed record ProcessorShipmentHistoryViewModel(
    long Id, DateTimeOffset ShippedAt, string Processor, int TotalBins,
    decimal SaleRate, string PricingBasis, string Currency, string? ReferenceNumber,
    string CreatedBy, bool IsReversed);

public sealed record ProcessorShipmentLineViewModel(
    long Id, long? ReceiptId, int? GrowerLotId, string Facility, string Room, string? GrowerNumber, string Grower,
    string Lot, string Variety, string Production, string OrganicStatus,
    string InventoryStatus, string Treatment, int Bins, decimal? PoundsPerBin,
    decimal? EstimatedPounds, decimal? EstimatedTons, decimal? EstimatedValue);

public sealed class ProcessorShipmentPageViewModel
{
    public ProcessorShipmentForm Form { get; set; } = new();
    public IReadOnlyList<ProcessorOptionViewModel> Processors { get; set; } = [];
    public IReadOnlyList<ProcessorOptionViewModel> ReportProcessors { get; set; } = [];
    public IReadOnlyList<ProcessorInventoryOptionViewModel> Inventory { get; set; } = [];
    public IReadOnlyList<ProcessorShipmentHistoryViewModel> History { get; set; } = [];
    public IReadOnlyList<ProcessorShipmentLineViewModel> ReviewLines { get; set; } = [];
    public bool IsReview { get; set; }
    public bool CanCreate { get; set; }
    public bool CanAdmin { get; set; }
    public string? Error { get; set; }
    public string? FilterFrom { get; set; }
    public string? FilterTo { get; set; }
    public int? FilterProcessorId { get; set; }
    public int? FilterWarehouseId { get; set; }
}

public sealed class ProcessorShipmentDetailViewModel
{
    public long Id { get; set; }
    public string Processor { get; set; } = "";
    public DateTimeOffset ShippedAt { get; set; }
    public decimal OriginalSaleRate { get; set; }
    public string OriginalPricingBasis { get; set; } = "";
    public decimal SaleRate { get; set; }
    public string PricingBasis { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = "";
    public bool IsReversed { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversalReason { get; set; }
    public bool CanAdmin { get; set; }
    public IReadOnlyList<ProcessorShipmentLineViewModel> Lines { get; set; } = [];
    public IReadOnlyList<ProcessorShipmentPriceCorrectionViewModel> Corrections { get; set; } = [];
    public int TotalBins => Lines.Sum(x => x.Bins);
    public decimal? EstimatedPounds => Lines.All(x => x.EstimatedPounds is not null) ? Lines.Sum(x => x.EstimatedPounds) : null;
    public decimal? EstimatedTons => Lines.All(x => x.EstimatedTons is not null) ? Lines.Sum(x => x.EstimatedTons) : null;
    public decimal? EstimatedValue => PricingBasis == ProcessorPricingBases.PerBin ? TotalBins * SaleRate : Lines.All(x => x.EstimatedValue is not null) ? Lines.Sum(x => x.EstimatedValue) : null;
}

public sealed record ProcessorShipmentPriceCorrectionViewModel(
    decimal OriginalRate, string OriginalBasis, decimal CorrectedRate, string CorrectedBasis,
    string Reason, string CorrectedBy, DateTimeOffset CorrectedAt);
