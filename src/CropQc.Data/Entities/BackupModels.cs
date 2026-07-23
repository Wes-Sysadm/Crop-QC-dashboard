namespace CropQc.Data.Entities;

public sealed class BackupRunRecord
{
    public long Id { get; set; }
    public required string BackupType { get; set; }
    public required string Status { get; set; }
    public required string EnvironmentName { get; set; }
    public required string DatabaseProvider { get; set; }
    public string? DeployedCommit { get; set; }
    public string? RequestedBy { get; set; }
    public required string RetentionCategory { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string? PackageFileName { get; set; }
    public string? PackageStorageKey { get; set; }
    public string? PackageWebUrl { get; set; }
    public string? ManifestFileName { get; set; }
    public string? ManifestStorageKey { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset? PrunedAt { get; set; }
}

public sealed class BackupOperationLease
{
    public int Id { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public static class BackupRunStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class BackupRunTypes
{
    public const string Manual = "Manual";
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";
    public const string PreDeployment = "PreDeployment";
}
