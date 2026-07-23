namespace CropQc.Data.Entities;

public sealed class ReceiptDeletionAudit
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public long DeletedReceiptId { get; set; }
    public required string ReceiptNumber { get; set; }
    public int CropYear { get; set; }
    public required string IdentifyingFieldsJson { get; set; }
    public required string DependencyCountsJson { get; set; }
    public required string DeletedByEmail { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
    public required string Reason { get; set; }
    public long? BackupRunId { get; set; }
    public required string Result { get; set; }
}

public sealed class ReceiptPurgeOperation
{
    public Guid Id { get; set; }
    public int TargetCropYear { get; set; }
    public long BackupRunId { get; set; }
    public required string RequestedByEmail { get; set; }
    public required string Reason { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public required string PreflightJson { get; set; }
    public required string PreservationBaselineJson { get; set; }
    public string? DeletedCountsJson { get; set; }
    public string? ErrorSummary { get; set; }
}

public static class ReceiptPurgeStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
