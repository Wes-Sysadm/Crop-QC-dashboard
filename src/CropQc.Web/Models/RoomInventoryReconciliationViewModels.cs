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
    public IReadOnlyList<RoomInventoryNegativeAdjustmentViewModel> NegativeAdjustments { get; set; } = [];
    public IReadOnlyList<string> GlobalWarnings { get; set; } = [];
    public InventoryDiagnosticOverviewViewModel InventoryDiagnostics { get; set; } = new();
    public int LedgerBalance => Rows.Sum(x => x.LedgerBalance);
    public int InboundReceiptBins => Rows.Sum(x => x.InboundReceiptBins);
    public int UnledgeredInboundBins => Rows.Sum(x => x.UnledgeredInboundBins);
}

public sealed class RoomInventoryNegativeAdjustmentViewModel
{
    public long AdjustmentId { get; set; }
    public string Facility { get; set; } = "";
    public string Room { get; set; } = "";
    public int? CropYear { get; set; }
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public int Quantity { get; set; }
    public string AdjustmentType { get; set; } = "";
    public string ParentType { get; set; } = "";
    public long? BinsRunId { get; set; }
    public long? TransferId { get; set; }
    public Guid? ReceiptInventoryOverrideId { get; set; }
    public long? RoomInventoryLossId { get; set; }
    public long? ActualRunId { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset AdjustmentAt { get; set; }
    public bool ParentMatches { get; set; }
    public bool CurrentlyAffectsInventory { get; set; }
    public int InvariantVersion { get; set; }
    public string RecordedSource { get; set; } = "";
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public IReadOnlyList<InventoryDiagnosticViewModel> ActiveDiagnostics { get; set; } = [];
    public int AcknowledgedDiagnosticCount { get; set; }
}

public sealed class InventoryDiagnosticOverviewViewModel
{
    public IReadOnlyList<InventoryDiagnosticViewModel> ActiveDiagnostics { get; set; } = [];
    public IReadOnlyList<DismissedInventoryDiagnosticViewModel> DismissedDiagnostics { get; set; } = [];
    public int BlockingCount => ActiveDiagnostics.Count(x => x.BlocksDeployment);
    public int HistoricalActiveCount => ActiveDiagnostics.Count(x => !x.BlocksDeployment);
}

public class InventoryDiagnosticViewModel
{
    public string DiagnosticKey { get; set; } = "";
    public string DiagnosticType { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public long AdjustmentId { get; set; }
    public int InvariantVersion { get; set; }
    public bool BlocksDeployment { get; set; }
    public string Facility { get; set; } = "";
    public int WarehouseId { get; set; }
    public string Room { get; set; } = "";
    public int RoomId { get; set; }
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public DateTimeOffset AdjustmentAt { get; set; }
    public string DiagnosticSnapshotJson { get; set; } = "";
    public bool CanDismiss => !BlocksDeployment;
}

public sealed class DismissedInventoryDiagnosticViewModel : InventoryDiagnosticViewModel
{
    public string Reason { get; set; } = "";
    public string DismissedByEmail { get; set; } = "";
    public DateTimeOffset DismissedAt { get; set; }
    public bool StillMatchesCurrentDiagnostic { get; set; }
}

public sealed class InventoryDiagnosticDismissForm
{
    public string DiagnosticKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed class InventoryDiagnosticRestoreForm
{
    public string DiagnosticKey { get; set; } = "";
    public string? ReturnUrl { get; set; }
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
    public int DroppedBins { get; set; }
    public int DroppedBinsRestored { get; set; }
    public int OtherAdjustmentBins { get; set; }
    public int LedgerBalance { get; set; }
    public int TransactionCount { get; set; }
    public DateTimeOffset? FirstTransactionAt { get; set; }
    public DateTimeOffset? LastTransactionAt { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
