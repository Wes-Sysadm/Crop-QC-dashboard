using CropQc.Data.Entities;

namespace CropQc.Web.Models;

public sealed class TruckReceiptActionForm
{
    public long ReceiptId { get; set; }
    public long TransferId { get; set; }
    public long ReceiptVersion { get; set; }
    public long TransferVersion { get; set; }
    public int? DestinationRoomId { get; set; }
    public string Reason { get; set; } = "";
    public List<TruckReceiptVarietyForm> Lines { get; set; } = [];
    public List<TruckReceiptAllocationForm> Allocations { get; set; } = [];
}

public sealed class TruckReceiptAllocationForm
{
    public long Id { get; set; }
    public int Quantity { get; set; }
}

public sealed class TruckReceiptVarietyForm
{
    public int FruitProfileId { get; set; }
    public int BinCount { get; set; }
}

public sealed class TransitEditForm
{
    public long TransferId { get; set; }
    public long TransferVersion { get; set; }
    public long? DispatchMovementId { get; set; }
    public string SourceKey { get; set; } = "";
    public int ExpectedAvailableBins { get; set; }
    public int Bins { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record VarietyReconciliation(int FruitProfileId, string Variety, int Transfer, int Receipt)
{
    public int Difference => Receipt - Transfer;
}

public sealed record TransitAllocation(TreatmentLineageMovement Movement, int Bins,
    CropQc.Shared.Inventory.InventoryCustodyAllocation? Canonical = null, string? CurrentGrowerName = null)
{
    public int? FruitProfileId => Canonical?.Identity.FruitProfileId ?? Movement.SourceSegment?.FruitProfileId;
    public int? CropYear => Canonical?.Identity.CropYear ?? Movement.SourceSegment?.CropYear;
}

public sealed class TruckReceiptPage
{
    public Receipt? Receipt { get; set; }
    public InterCrewTransfer? Transfer { get; set; }
    public IReadOnlyList<InterCrewTransfer> Candidates { get; set; } = [];
    public IReadOnlyList<FruitProfile> Profiles { get; set; } = [];
    public IReadOnlyList<VarietyReconciliation> Comparison { get; set; } = [];
    public IReadOnlyList<TransitAllocation> Allocations { get; set; } = [];
    public IReadOnlyList<OutsideWarehouseInventoryOptionViewModel> Available { get; set; } = [];
    public string? Error { get; set; }
    public bool WritesEnabled { get; set; }
    public bool CanAdmin { get; set; }
    public bool CanEditReceipt { get; set; }
    public bool CanEditTransfer { get; set; }
    public Dictionary<long, string> CandidateVarieties { get; set; } = [];
    public bool SupportsReceiptCustody { get; set; }
    public IReadOnlyList<ReceiptCustodyAcknowledgment> Acknowledgments { get; set; } = [];
    public int AcknowledgedBins => Acknowledgments.Sum(x => x.NetQuantity);
    public int ReceiptHeldBins => Acknowledgments.Sum(x => x.HeldQuantity);
    public int PlacedBins => Acknowledgments.Sum(x => x.PlacedQuantity);
    public IReadOnlyList<Room> PlacementRooms { get; set; } = [];
    public int UnresolvedBins => Math.Max(0, (Transfer?.BinsLoaded ?? 0) - AcknowledgedBins);
    public bool IsReconciled => Comparison.Count > 0 && Comparison.All(x => x.Difference == 0);
    public string Status => Transfer is { RequiresTruckReceipt: false } ? "Legacy transfer — existing receiving workflow"
        : Transfer?.Status == InterCrewTransferStatuses.Reversed ? "Cancelled - returned to source"
        : Transfer?.Status == InterCrewTransferStatuses.Received ? "Completed"
        : Acknowledgments.Count > 0 ? $"Incomplete — {AcknowledgedBins} acknowledged, {ReceiptHeldBins} receipt-held, {UnresolvedBins} unresolved"
        : Receipt is null || Transfer is null ? "Awaiting Receipt"
        : IsReconciled ? "Reconciled — ready to complete" : "Reconciliation Required";
}
