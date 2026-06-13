using CropQc.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CropQc.Api.Tests;

public sealed class ProductionReadinessTests
{
    [Fact]
    public void Backup_options_bind_from_backups_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backups:Enabled"] = "true",
                ["Backups:Provider"] = "GoogleDrive",
                ["Backups:GoogleDriveFolderId"] = "backup-folder",
                ["Backups:RetentionDays"] = "120",
                ["Backups:ScheduleUtcHour"] = "11",
                ["Backups:DatabaseBackupEnabled"] = "true",
                ["Backups:PhotoManifestEnabled"] = "true",
                ["Backups:ConfigBackupEnabled"] = "true"
            })
            .Build();

        var options = BackupOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal("GoogleDrive", options.Provider);
        Assert.Equal("backup-folder", options.GoogleDriveFolderId);
        Assert.Equal(120, options.RetentionDays);
        Assert.Equal(11, options.ScheduleUtcHour);
        Assert.True(options.DatabaseBackupEnabled);
        Assert.True(options.PhotoManifestEnabled);
        Assert.True(options.ConfigBackupEnabled);
    }

    [Fact]
    public void App_environment_options_show_staging_banner_and_production_badge_inputs()
    {
        var staging = AppEnvironmentOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppEnvironment:Kind"] = "Staging",
                ["AppEnvironment:DisplayName"] = "Crop QC Staging"
            })
            .Build(), new FakeHostEnvironment("Production"));
        var production = AppEnvironmentOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppEnvironment:Kind"] = "Production",
                ["AppEnvironment:DisplayName"] = "Production"
            })
            .Build(), new FakeHostEnvironment("Production"));

        Assert.True(staging.IsStagingLike);
        Assert.False(staging.IsProduction);
        Assert.True(production.IsProduction);
        Assert.False(production.IsStagingLike);
    }

    [Fact]
    public void Backup_file_names_use_expected_timestamp_format()
    {
        var timestamp = new DateTimeOffset(2026, 8, 1, 10, 11, 12, TimeSpan.Zero);

        Assert.Equal("cropqc-prod-db-20260801-101112.sql.gz", BackupFileNames.Database(timestamp));
        Assert.Equal("cropqc-prod-config-20260801-101112.json", BackupFileNames.Config(timestamp));
        Assert.Equal("cropqc-prod-photo-manifest-20260801-101112.json", BackupFileNames.PhotoManifest(timestamp));
    }

    [Fact]
    public void Admin_backups_are_admin_only_and_linked_from_layout()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "BackupsController.cs"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("[Authorize(Policy = \"RequireAdmin\")]", controller);
        Assert.Contains("/Admin/Backups", layout);
        Assert.Contains("TEST SITE — DO NOT ENTER REAL QC DATA", layout);
    }

    [Fact]
    public void Backup_service_excludes_secret_values_from_config_snapshot()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BackupService.cs"));
        var snapshotStart = service.IndexOf("private object BuildSafeConfigurationSnapshot", StringComparison.Ordinal);
        var snapshotEnd = service.IndexOf("private async Task<IReadOnlyList<object>> BuildPhotoManifestAsync", StringComparison.Ordinal);
        Assert.True(snapshotStart >= 0, "Could not find backup configuration snapshot method.");
        Assert.True(snapshotEnd > snapshotStart, "Could not find backup photo manifest method after snapshot method.");
        var snapshot = service[snapshotStart..snapshotEnd];

        Assert.Contains("BuildSafeConfigurationSnapshot", snapshot);
        Assert.DoesNotContain("ServiceAccountJson", snapshot);
        Assert.DoesNotContain("ClientSecret", snapshot);
        Assert.DoesNotContain("AccessToken", snapshot);
        Assert.Contains("qcDefaultRecipientsConfigured", snapshot);
        Assert.Contains("googleDriveRootFolderId", snapshot);
        Assert.Contains("effectiveOptions.GoogleDriveFolderId", snapshot);
    }

    [Fact]
    public void Backup_service_creates_photo_manifest_and_pg_dump_warning()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BackupService.cs"));

        Assert.Contains("BuildPhotoManifestAsync", service);
        Assert.Contains("photoType", service);
        Assert.Contains("fileId", service);
        Assert.Contains("receiptId", service);
        Assert.Contains("pg_dump is not available", service);
    }

    [Fact]
    public void Production_docs_cover_environment_separation_and_restore()
    {
        var renderDocs = File.ReadAllText(FindRepositoryFile("docs", "render-deployment.md"));
        var restoreDocs = File.ReadAllText(FindRepositoryFile("docs", "backup-restore.md"));
        var releaseChecklist = File.ReadAllText(FindRepositoryFile("docs", "production-release-checklist.md"));
        var stagingChecklist = File.ReadAllText(FindRepositoryFile("docs", "staging-test-checklist.md"));

        Assert.Contains("AppEnvironment__Kind=Production", renderDocs);
        Assert.Contains("AppEnvironment__Kind=Staging", renderDocs);
        Assert.Contains("Backups__GoogleDriveFolderId", renderDocs);
        Assert.Contains("restore", restoreDocs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Production backup completed before deploy", releaseChecklist);
        Assert.Contains("TEST SITE", stagingChecklist);
    }

    [Fact]
    public void Backup_page_allows_configuring_google_drive_folder()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "BackupsController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Backups", "Index.cshtml"));
        var options = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BackupOptions.cs"));

        Assert.Contains("[HttpPost(\"Settings\")]", controller);
        Assert.Contains("Google Drive Backup Folder", view);
        Assert.Contains("Test Google Drive Backup Access", view);
        Assert.Contains("Run Backup Now", view);
        Assert.Contains("NormalizeGoogleDriveFolderId", options);
        Assert.Contains("folders", options);
    }

    [Fact]
    public void Configuration_removes_static_qc_summary_from_address()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Configuration", "Index.cshtml"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));

        Assert.Contains("Current logged-in Gmail user. No static From address is configured.", view);
        Assert.Contains("DefaultQcSummaryFromAddress", view);
        Assert.DoesNotContain("(\"DefaultQcSummaryFromAddress\"", service);
    }

    [Fact]
    public void Offline_sync_design_documents_station_queue_and_conflicts()
    {
        var design = File.ReadAllText(FindRepositoryFile("docs", "offline-sync-design.md"));

        Assert.Contains("SQLite", design);
        Assert.Contains("Pending Sync", design);
        Assert.Contains("Sync Failed", design);
        Assert.Contains("idempotency key", design);
        Assert.Contains("server remains the source of truth", design, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never delete local pending data until the server confirms it synced", design);
    }

    [Fact]
    public void Data_cleanup_remains_restricted_to_allowed_emails()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "AdminController.cs"));

        Assert.Contains("DataCleanup:AllowedEmails", controller);
        Assert.Contains("IsDataCleanupAllowed()", controller);
        Assert.Contains("return Forbid();", controller);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(parts)}");
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "CropQc.Web";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
