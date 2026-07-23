namespace CropQc.Web.Services;

public sealed class BackupOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = BackupProviders.GoogleDrive;
    public string? GoogleDriveFolderId { get; init; }
    public int DailyRetentionDays { get; init; } = 30;
    public int WeeklyRetentionWeeks { get; init; } = 52;
    public string BusinessTimeZone { get; init; } = "America/Los_Angeles";
    public int NightlyPacificHour { get; init; } = 1;
    public string NotificationRecipient { get; init; } = "wes@fruitandland.com";
    public string NotificationSender { get; init; } = "wes@fruitandland.com";
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
            DailyRetentionDays = configuration.GetValue("Backups:DailyRetentionDays", configuration.GetValue("Backups:RetentionDays", 30)),
            WeeklyRetentionWeeks = configuration.GetValue("Backups:WeeklyRetentionWeeks", 52),
            BusinessTimeZone = configuration["Backups:BusinessTimeZone"] ?? "America/Los_Angeles",
            NightlyPacificHour = configuration.GetValue("Backups:NightlyPacificHour", 1),
            NotificationRecipient = configuration["Backups:NotificationRecipient"] ?? "wes@fruitandland.com",
            NotificationSender = configuration["Backups:NotificationSender"] ?? "wes@fruitandland.com",
            DatabaseBackupEnabled = true,
            PhotoManifestEnabled = true,
            ConfigBackupEnabled = true
        };

    public BackupOptions WithOverrides(IReadOnlyDictionary<string, string> overrides) =>
        new()
        {
            Enabled = Bool(overrides, "Backups:Enabled", Enabled),
            Provider = overrides.GetValueOrDefault("Backups:Provider") ?? Provider,
            GoogleDriveFolderId = NormalizeGoogleDriveFolderId(overrides.GetValueOrDefault("Backups:GoogleDriveFolderId") ?? GoogleDriveFolderId),
            DailyRetentionDays = Int(overrides, "Backups:DailyRetentionDays", DailyRetentionDays),
            WeeklyRetentionWeeks = Int(overrides, "Backups:WeeklyRetentionWeeks", WeeklyRetentionWeeks),
            BusinessTimeZone = overrides.GetValueOrDefault("Backups:BusinessTimeZone") ?? BusinessTimeZone,
            NightlyPacificHour = Int(overrides, "Backups:NightlyPacificHour", NightlyPacificHour),
            NotificationRecipient = overrides.GetValueOrDefault("Backups:NotificationRecipient") ?? NotificationRecipient,
            NotificationSender = overrides.GetValueOrDefault("Backups:NotificationSender") ?? NotificationSender,
            DatabaseBackupEnabled = true,
            PhotoManifestEnabled = true,
            ConfigBackupEnabled = true
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
