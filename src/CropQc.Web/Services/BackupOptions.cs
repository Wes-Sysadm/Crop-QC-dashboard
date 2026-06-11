namespace CropQc.Web.Services;

public sealed class BackupOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = BackupProviders.GoogleDrive;
    public string? GoogleDriveFolderId { get; init; }
    public int RetentionDays { get; init; } = 90;
    public int ScheduleUtcHour { get; init; } = 10;
    public bool DatabaseBackupEnabled { get; init; } = true;
    public bool PhotoManifestEnabled { get; init; } = true;
    public bool ConfigBackupEnabled { get; init; } = true;

    public bool IsGoogleDrive => string.Equals(Provider, BackupProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase);
    public bool GoogleDriveFolderConfigured => !string.IsNullOrWhiteSpace(GoogleDriveFolderId);

    public static BackupOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            Enabled = configuration.GetValue("Backups:Enabled", false),
            Provider = configuration["Backups:Provider"] ?? BackupProviders.GoogleDrive,
            GoogleDriveFolderId = configuration["Backups:GoogleDriveFolderId"],
            RetentionDays = configuration.GetValue("Backups:RetentionDays", 90),
            ScheduleUtcHour = configuration.GetValue("Backups:ScheduleUtcHour", 10),
            DatabaseBackupEnabled = configuration.GetValue("Backups:DatabaseBackupEnabled", true),
            PhotoManifestEnabled = configuration.GetValue("Backups:PhotoManifestEnabled", true),
            ConfigBackupEnabled = configuration.GetValue("Backups:ConfigBackupEnabled", true)
        };
}

public static class BackupProviders
{
    public const string GoogleDrive = "GoogleDrive";
}
