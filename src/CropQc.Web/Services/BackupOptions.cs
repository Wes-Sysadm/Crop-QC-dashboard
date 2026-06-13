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

    public BackupOptions WithOverrides(IReadOnlyDictionary<string, string> overrides) =>
        new()
        {
            Enabled = Bool(overrides, "Backups:Enabled", Enabled),
            Provider = overrides.GetValueOrDefault("Backups:Provider") ?? Provider,
            GoogleDriveFolderId = NormalizeGoogleDriveFolderId(overrides.GetValueOrDefault("Backups:GoogleDriveFolderId") ?? GoogleDriveFolderId),
            RetentionDays = Int(overrides, "Backups:RetentionDays", RetentionDays),
            ScheduleUtcHour = Int(overrides, "Backups:ScheduleUtcHour", ScheduleUtcHour),
            DatabaseBackupEnabled = Bool(overrides, "Backups:DatabaseBackupEnabled", DatabaseBackupEnabled),
            PhotoManifestEnabled = Bool(overrides, "Backups:PhotoManifestEnabled", PhotoManifestEnabled),
            ConfigBackupEnabled = Bool(overrides, "Backups:ConfigBackupEnabled", ConfigBackupEnabled)
        };

    public static string? NormalizeGoogleDriveFolderId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var folderIndex = Array.FindIndex(segments, x => x.Equals("folders", StringComparison.OrdinalIgnoreCase));
            if (folderIndex >= 0 && folderIndex + 1 < segments.Length)
            {
                return segments[folderIndex + 1];
            }
        }

        return trimmed;
    }

    private static bool Bool(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int Int(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
}

public static class BackupProviders
{
    public const string GoogleDrive = "GoogleDrive";
}
