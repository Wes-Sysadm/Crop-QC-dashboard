namespace CropQc.Web.Models;

public sealed class RoomInventoryReconciliationFilter
{
    public int? WarehouseId { get; set; }
    public int? RoomId { get; set; }
    public string? Lot { get; set; }
    public string? Variety { get; set; }
    public bool WarningsOnly { get; set; }
}

public sealed class RoomInventoryReconciliationPageViewModel
{
    public RoomInventoryReconciliationFilter Filter { get; set; } = new();
    public IReadOnlyList<RoomInventoryReconciliationOption> Warehouses { get; set; } = [];
    public IReadOnlyList<RoomInventoryReconciliationOption> Rooms { get; set; } = [];
    public IReadOnlyList<RoomInventoryReconciliationRowViewModel> Rows { get; set; } = [];
    public IReadOnlyList<string> GlobalWarnings { get; set; } = [];
    public int LedgerBalance => Rows.Sum(x => x.LedgerBalance);
    public int InboundReceiptBins => Rows.Sum(x => x.InboundReceiptBins);
    public int UnledgeredInboundBins => Rows.Sum(x => x.UnledgeredInboundBins);
}

public sealed record RoomInventoryReconciliationOption(int Id, string Label);

public sealed class RoomInventoryReconciliationRowViewModel
{
    public int WarehouseId { get; set; }
    public string Facility { get; set; } = "";
    public int RoomId { get; set; }
    public string Room { get; set; } = "";
    public int? CropYear { get; set; }
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string StoredVariety { get; set; } = "";
    public string CanonicalVariety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public int InboundReceiptBins { get; set; }
    public int UnledgeredInboundBins { get; set; }
    public int PositiveLedgerBins { get; set; }
    public int NegativeLedgerBins { get; set; }
    public int LegacyBinsRunDepletionBins { get; set; }
    public int ActualRunDepletionBins { get; set; }
    public int ActualRunReversalBins { get; set; }
    public int TransferInBins { get; set; }
    public int TransferOutBins { get; set; }
    public int TrueUpBins { get; set; }
    public int OtherAdjustmentBins { get; set; }
    public int LedgerBalance { get; set; }
    public int TransactionCount { get; set; }
    public DateTimeOffset? FirstTransactionAt { get; set; }
    public DateTimeOffset? LastTransactionAt { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
