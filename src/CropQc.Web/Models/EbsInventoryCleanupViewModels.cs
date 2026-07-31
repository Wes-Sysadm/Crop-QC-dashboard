namespace CropQc.Web.Models;

public sealed class EbsInventoryCleanupPageViewModel
{
    public IReadOnlyList<EbsInventoryCleanupRowViewModel> Rows { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalRows { get; set; }
    public int ProtectedRoomId { get; set; }
    public string ProtectedRoom { get; set; } = "";
    public int CandidateCurrentBins { get; set; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page * PageSize < TotalRows;
}

public sealed class EbsInventoryCleanupRowViewModel
{
    public long InventorySnapshotId { get; set; }
    public int WarehouseId { get; set; }
    public int RoomId { get; set; }
    public string Room { get; set; } = "";
    public int? CropYear { get; set; }
    public int? FruitProfileId { get; set; }
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public int CurrentBins { get; set; }
    public int PositiveLedgerOrigin { get; set; }
    public int BinsRunDeductions { get; set; }
    public int TransferActivity { get; set; }
    public int TrueUps { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public string EvidenceSource { get; set; } = "";
    public string WarningReason { get; set; } = "";
}
