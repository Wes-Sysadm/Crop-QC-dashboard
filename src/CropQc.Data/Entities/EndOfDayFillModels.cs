namespace CropQc.Data.Entities;

public static class EndOfDayFillSendStatuses
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public sealed class EndOfDayFillReportGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Facility { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<EndOfDayFillReportGroupRoom> Rooms { get; } = new List<EndOfDayFillReportGroupRoom>();
    public ICollection<EndOfDayFillUserGroupAssignment> UserAssignments { get; } = new List<EndOfDayFillUserGroupAssignment>();
    public ICollection<EndOfDayFillReportSend> Sends { get; } = new List<EndOfDayFillReportSend>();
}

public sealed class EndOfDayFillReportGroupRoom
{
    public int Id { get; set; }
    public int ReportGroupId { get; set; }
    public EndOfDayFillReportGroup ReportGroup { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}

public sealed class EndOfDayFillReportRecipient
{
    public int Id { get; set; }
    public required string EmailAddress { get; set; }
    public required string NormalizedEmailAddress { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class EndOfDayFillUserGroupAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ReportGroupId { get; set; }
    public EndOfDayFillReportGroup ReportGroup { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}

public sealed class EndOfDayFillReportSend
{
    public long Id { get; set; }
    public int ReportGroupId { get; set; }
    public EndOfDayFillReportGroup ReportGroup { get; set; } = null!;
    public required string ReportGroupName { get; set; }
    public required string Facility { get; set; }
    public DateOnly PacificReportDate { get; set; }
    public int RevisionNumber { get; set; }
    public int? SenderUserId { get; set; }
    public User? SenderUser { get; set; }
    public required string SenderEmail { get; set; }
    public required string SenderDisplayName { get; set; }
    public required string RecipientsJson { get; set; }
    public bool PhysicalCountConfirmed { get; set; }
    public required string SnapshotHash { get; set; }
    public required string SnapshotJson { get; set; }
    public string? SuccessRevisionKey { get; set; }
    public string? SuccessSnapshotKey { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public required string TextBody { get; set; }
    public required string Status { get; set; }
    public string? FailureReason { get; set; }
    public string? GmailMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

public sealed class EndOfDayFillSendReservation
{
    public int ReportGroupId { get; set; }
    public EndOfDayFillReportGroup ReportGroup { get; set; } = null!;
    public DateOnly PacificReportDate { get; set; }
    public int RevisionNumber { get; set; }
    public required string SnapshotHash { get; set; }
    public long SendAttemptId { get; set; }
    public EndOfDayFillReportSend SendAttempt { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
