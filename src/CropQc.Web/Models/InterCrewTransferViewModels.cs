namespace CropQc.Web.Models;

public sealed class InterCrewDispatchForm
{
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceKey { get; set; } = "";
    public int ExpectedAvailableBins { get; set; }
    public string DestinationCustodyGroup { get; set; } = "";
    public int BinsLoaded { get; set; }
    public DateTime LoadedAt { get; set; } = DateTime.Now;
    public string? TruckLoadBolNumber { get; set; }
    public string? Notes { get; set; }
    public bool ConfirmedReview { get; set; }
}

public sealed class InterCrewReceiveForm
{
    public long TransferId { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int? DestinationRoomId { get; set; }
    public int BinsReceived { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
    public string? Note { get; set; }
}

public sealed class InterCrewReviewForm
{
    public long TransferId { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? Note { get; set; }
}

public sealed class InterCrewReversalForm
{
    public long TransferId { get; set; }
    public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? Reason { get; set; }
}

public sealed record InterCrewTransferListItemViewModel(
    long Id, DateTimeOffset LoadedAt, string Source, string DestinationGroup, string Grower,
    string Lot, string Variety, string Treatment, int BinsLoaded, int? BinsReceived,
    int? Variance, string? Bol, string Status, bool CanReceive)
{
    public string StatusLabel => InterCrewStatusLabel.Format(Status);
}

public sealed record InterCrewDestinationRoomViewModel(int Id, string Facility, string Room);

public sealed class InterCrewTransferPageViewModel
{
    public InterCrewDispatchForm Form { get; set; } = new();
    public IReadOnlyList<OutsideWarehouseInventoryOptionViewModel> Inventory { get; set; } = [];
    public IReadOnlyList<InterCrewTransferListItemViewModel> Queue { get; set; } = [];
    public IReadOnlyList<InterCrewTransferListItemViewModel> History { get; set; } = [];
    public bool CanCreate { get; set; }
    public bool CanAdmin { get; set; }
    public string CurrentCustodyGroup { get; set; } = "";
    public int InTransitLoads { get; set; }
    public int InTransitBins { get; set; }
}

public sealed class InterCrewTransferDetailViewModel
{
    public long Id { get; set; }
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public string DestinationGroup { get; set; } = "";
    public string? Destination { get; set; }
    public string Grower { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Variety { get; set; } = "";
    public string Treatment { get; set; } = "";
    public int BinsLoaded { get; set; }
    public int? BinsReceived { get; set; }
    public int? Variance { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? Bol { get; set; }
    public string? Notes { get; set; }
    public string LoadedBy { get; set; } = "";
    public string? ReceivedBy { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReversalReason { get; set; }
    public IReadOnlyList<InterCrewDestinationRoomViewModel> DestinationRooms { get; set; } = [];
    public bool CanReceive { get; set; }
    public bool CanAdmin { get; set; }
    public string StatusLabel => InterCrewStatusLabel.Format(Status);
}

public static class InterCrewStatusLabel
{
    public static string Format(string status) => status switch
    {
        "InTransit" => "In Transit",
        "ReceivedNeedsReview" => "Received — Needs Review",
        _ => status
    };
}
