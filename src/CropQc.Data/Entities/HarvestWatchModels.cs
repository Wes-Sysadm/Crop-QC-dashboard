namespace CropQc.Data.Entities;

public static class HarvestWatchStatuses
{
    public const string PendingVerification = "PendingVerification";
    public const string Working = "Working";
    public const string ErrorFailedToRead = "ErrorFailedToRead";
    public const string ErrorLowReading = "ErrorLowReading";
    public const string Removed = "Removed";

    public static readonly IReadOnlySet<string> Active = new HashSet<string>(StringComparer.Ordinal)
    {
        PendingVerification, Working, ErrorFailedToRead, ErrorLowReading
    };

    public static bool IsError(string status) => status is ErrorFailedToRead or ErrorLowReading;
}

public sealed class HarvestWatchDeployment
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public required string HarvestWatchCode { get; set; }
    public required string Status { get; set; } = HarvestWatchStatuses.PendingVerification;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset DeployedAt { get; set; }
    public int DeployedByUserId { get; set; }
    public User DeployedByUser { get; set; } = null!;
    public required string DeployerEmailSnapshot { get; set; }
    public required string WarehouseCodeSnapshot { get; set; }
    public required string RoomCodeSnapshot { get; set; }
    public required string VarietySnapshot { get; set; }
    public required string CorrelationToken { get; set; }
    public DateTimeOffset? VerificationEmailSentAt { get; set; }
    public string? VerificationEmailMessageId { get; set; }
    public string? VerificationEmailError { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifiedByEmail { get; set; }
    public string? LastReplyMessageId { get; set; }
    public DateTimeOffset? ErrorNotificationSentAt { get; set; }
    public string? ErrorNotificationMessageId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public int? RemovedByUserId { get; set; }
    public User? RemovedByUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<HarvestWatchStatusHistory> StatusHistory { get; } = new List<HarvestWatchStatusHistory>();
}

public sealed class HarvestWatchStatusHistory
{
    public long Id { get; set; }
    public long HarvestWatchDeploymentId { get; set; }
    public HarvestWatchDeployment HarvestWatchDeployment { get; set; } = null!;
    public string? PreviousStatus { get; set; }
    public required string NewStatus { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? InboundMessageId { get; set; }
    public string? ChangedByEmail { get; set; }
    public string? Note { get; set; }
}

public sealed class HarvestWatchInboundMessage
{
    public long Id { get; set; }
    public required string GmailMessageId { get; set; }
    public long? HarvestWatchDeploymentId { get; set; }
    public HarvestWatchDeployment? HarvestWatchDeployment { get; set; }
    public required string SenderEmail { get; set; }
    public required string Subject { get; set; }
    public required string BodyExcerpt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string Outcome { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class HarvestWatchMailboxCursor
{
    public int Id { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
