namespace CropQc.Data.Entities;

public static class ProcessorPricingBases
{
    public const string PerTon = "PerTon";
    public const string PerBin = "PerBin";

    public static bool IsValid(string? value) => value is PerTon or PerBin;
    public static string Display(string value) => value == PerBin ? "Per Bin" : "Per Ton";
    public static string Suffix(string value) => value == PerBin ? "/bin" : "/ton";
}

public static class ProcessorShipmentAdjustmentTypes
{
    public const string Shipment = "ProcessorShipment";
    public const string Reversal = "ProcessorShipmentReversal";
}

public sealed class Processor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<ProcessorShipment> Shipments { get; } = new List<ProcessorShipment>();
}

public sealed class ProcessorShipment
{
    public long Id { get; set; }
    public required string OperationKey { get; set; }
    public int ProcessorId { get; set; }
    public Processor Processor { get; set; } = null!;
    public required string ProcessorNameSnapshot { get; set; }
    public DateTimeOffset ShippedAt { get; set; }
    public decimal OriginalSaleRate { get; set; }
    public required string OriginalPricingBasis { get; set; }
    public decimal SaleRate { get; set; }
    public required string PricingBasis { get; set; }
    public required string Currency { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public int? ReversedByUserId { get; set; }
    public User? ReversedByUser { get; set; }
    public string? ReversalReason { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public ICollection<ProcessorShipmentLine> Lines { get; } = new List<ProcessorShipmentLine>();
    public ICollection<ProcessorShipmentPriceCorrection> PriceCorrections { get; } = new List<ProcessorShipmentPriceCorrection>();
}

public sealed class ProcessorShipmentLine
{
    public long Id { get; set; }
    public long ProcessorShipmentId { get; set; }
    public ProcessorShipment ProcessorShipment { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int? CropYear { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? SourceInventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment? SourceInventoryAdjustment { get; set; }
    public int? GrowerLotId { get; set; }
    public int? FruitProfileId { get; set; }
    public string? GrowerNumberSnapshot { get; set; }
    public required string GrowerNameSnapshot { get; set; }
    public required string LotNumberSnapshot { get; set; }
    public required string VarietyCodeSnapshot { get; set; }
    public required string ProductionTypeSnapshot { get; set; }
    public bool? IsOrganicSnapshot { get; set; }
    public string? InventoryStatusSnapshot { get; set; }
    public required string TreatmentStateSnapshot { get; set; }
    public required string TreatmentSignatureSnapshot { get; set; }
    public required string TreatmentSummarySnapshot { get; set; }
    public int BinsSent { get; set; }
    public decimal? PoundsPerBinSnapshot { get; set; }
    public ICollection<RoomInventoryAdjustment> InventoryAdjustments { get; } = new List<RoomInventoryAdjustment>();
    public ICollection<TreatmentLineageMovement> TreatmentLineageMovements { get; } = new List<TreatmentLineageMovement>();
}

public sealed class ProcessorShipmentPriceCorrection
{
    public long Id { get; set; }
    public long ProcessorShipmentId { get; set; }
    public ProcessorShipment ProcessorShipment { get; set; } = null!;
    public required string OperationKey { get; set; }
    public decimal OriginalSaleRate { get; set; }
    public required string OriginalPricingBasis { get; set; }
    public decimal CorrectedSaleRate { get; set; }
    public required string CorrectedPricingBasis { get; set; }
    public required string Reason { get; set; }
    public int CorrectedByUserId { get; set; }
    public User CorrectedByUser { get; set; } = null!;
    public DateTimeOffset CorrectedAt { get; set; }
}
