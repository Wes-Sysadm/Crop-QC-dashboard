namespace CropQc.Web.Models;

public sealed record EndOfDayFillGroupOption(int Id, string Name, string Facility);

public sealed class EndOfDayFillPreviewViewModel
{
    public int? SelectedGroupId { get; set; }
    public IReadOnlyList<EndOfDayFillGroupOption> Groups { get; set; } = [];
    public string GroupName { get; set; } = "";
    public string Facility { get; set; } = "";
    public IReadOnlyList<string> Recipients { get; set; } = [];
    public IReadOnlyList<EndOfDayFillRoomViewModel> Rooms { get; set; } = [];
    public IReadOnlyList<EndOfDayFillValidationIssue> Issues { get; set; } = [];
    public string? PreviewToken { get; set; }
    public bool GmailReady { get; set; }
    public EndOfDayFillPendingAttemptViewModel? PendingAttempt { get; set; }
    public bool CanSend => SelectedGroupId is not null && Issues.Count == 0 && GmailReady && !string.IsNullOrWhiteSpace(PreviewToken);
    public EndOfDayFillSendForm Form { get; set; } = new();
}

public sealed record EndOfDayFillPendingAttemptViewModel(
    long SendAttemptId,
    string GroupName,
    string Sender,
    DateTimeOffset AttemptedAt,
    string Subject,
    string Recipients,
    int RevisionNumber,
    string SnapshotHash,
    bool IsStale);

public sealed record EndOfDayFillValidationIssue(string Code, string Message, int? RoomId = null);

public sealed class EndOfDayFillRoomViewModel
{
    public int RoomId { get; set; }
    public string RoomCode { get; set; } = "";
    public string RoomName { get; set; } = "";
    public int CurrentBins { get; set; }
    public int CapacityBins { get; set; }
    public decimal PercentFull => CapacityBins > 0 ? decimal.Round(CurrentBins * 100m / CapacityBins, 1) : 0;
    public IReadOnlyList<EndOfDayFillVarietyViewModel> Varieties { get; set; } = [];
}

public sealed class EndOfDayFillVarietyViewModel
{
    public string CanonicalKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProductionType { get; set; } = "";
    public bool IsOrganic { get; set; }
    public string HexColor { get; set; } = "#607D8B";
    public int Bins { get; set; }
    public IReadOnlyList<EndOfDayFillGrowerViewModel> Growers { get; set; } = [];
}

public sealed record EndOfDayFillGrowerViewModel(string GrowerNumber, string GrowerName, int Bins);

public sealed class EndOfDayFillSendForm
{
    public int GroupId { get; set; }
    public string PreviewToken { get; set; } = "";
    public bool PhysicalCountConfirmed { get; set; }
}

public sealed record EndOfDayFillSendResult(bool Success, bool StalePreview, string Message, long? SendId = null);

public sealed class EndOfDayFillHistoryPageViewModel
{
    public IReadOnlyList<EndOfDayFillHistoryItemViewModel> Sends { get; set; } = [];
}

public sealed record EndOfDayFillHistoryItemViewModel(
    long Id,
    string GroupName,
    string Facility,
    DateOnly ReportDate,
    int RevisionNumber,
    string Sender,
    string Recipients,
    string Status,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? SentAt,
    string? FailureReason);

public sealed class EndOfDayFillHistoryDetailViewModel
{
    public long Id { get; set; }
    public string GroupName { get; set; } = "";
    public string Facility { get; set; } = "";
    public DateOnly ReportDate { get; set; }
    public int RevisionNumber { get; set; }
    public string Sender { get; set; } = "";
    public string Recipients { get; set; } = "";
    public string Status { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string TextBody { get; set; } = "";
    public string? GmailMessageId { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class EndOfDayFillAdminPageViewModel
{
    public IReadOnlyList<EndOfDayFillAdminGroupViewModel> Groups { get; set; } = [];
    public IReadOnlyList<EndOfDayFillAdminRoomViewModel> Rooms { get; set; } = [];
    public IReadOnlyList<EndOfDayFillAdminRecipientViewModel> Recipients { get; set; } = [];
    public IReadOnlyList<EndOfDayFillPendingAttemptViewModel> StaleAttempts { get; set; } = [];
}

public sealed record EndOfDayFillAdminGroupViewModel(int Id, string Name, string Facility, bool IsActive, IReadOnlyList<int> RoomIds);
public sealed record EndOfDayFillAdminRoomViewModel(int Id, string Facility, string Code, string Name, string? Location, int CapacityBins, int? ActiveGroupId);
public sealed record EndOfDayFillAdminRecipientViewModel(int Id, string Email, bool IsActive, int SortOrder);

public sealed class EndOfDayFillGroupForm
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public string Facility { get; set; } = "";
    public bool IsActive { get; set; }
    public List<int> RoomIds { get; set; } = [];
}

public sealed class EndOfDayFillRecipientForm
{
    public int? Id { get; set; }
    public string EmailAddress { get; set; } = "";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class EndOfDayFillUserAssignmentsForm
{
    public int UserId { get; set; }
    public List<int> GroupIds { get; set; } = [];
}

public sealed class EndOfDayFillRecoveryForm
{
    public long SendAttemptId { get; set; }
    public string Resolution { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? GmailMessageId { get; set; }
    public bool Confirmed { get; set; }
}
