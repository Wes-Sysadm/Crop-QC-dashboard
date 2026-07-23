using CropQc.Web.Services;
using CropQc.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
                ["Backups:DailyRetentionDays"] = "30",
                ["Backups:WeeklyRetentionWeeks"] = "52",
                ["Backups:NightlyPacificHour"] = "1",
                ["Backups:NotificationRecipient"] = "wes@fruitandland.com",
                ["Backups:DatabaseBackupEnabled"] = "true",
                ["Backups:PhotoManifestEnabled"] = "true",
                ["Backups:ConfigBackupEnabled"] = "true"
            })
            .Build();

        var options = BackupOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal("GoogleDrive", options.Provider);
        Assert.Equal("backup-folder", options.GoogleDriveFolderId);
        Assert.Equal(30, options.DailyRetentionDays);
        Assert.Equal(52, options.WeeklyRetentionWeeks);
        Assert.Equal(1, options.NightlyPacificHour);
        Assert.Equal("wes@fruitandland.com", options.NotificationRecipient);
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
        Assert.True(staging.IsStaging);
        Assert.False(staging.IsProduction);
        Assert.True(production.IsProduction);
        Assert.False(production.IsStagingLike);
        Assert.False(production.IsStaging);
    }

    [Fact]
    public void Backup_file_names_use_expected_timestamp_format()
    {
        var timestamp = new DateTimeOffset(2026, 8, 1, 10, 11, 12, TimeSpan.Zero);

        Assert.Equal("cropqc-prod-db-20260801-101112.sql.gz", BackupFileNames.Database(timestamp));
        Assert.Equal("cropqc-prod-config-20260801-101112.json", BackupFileNames.Config(timestamp));
        Assert.Equal("cropqc-prod-photo-manifest-20260801-101112.json", BackupFileNames.PhotoManifest(timestamp));
        Assert.Equal("cropqc-production-daily-20260801-101112.zip", BackupFileNames.Package(BackupRunTypes.Daily, timestamp));
    }

    [Fact]
    public void Admin_backups_are_admin_only_and_linked_from_layout()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "BackupsController.cs"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("AccessPolicyNames.BackupsAdmin", controller);
        Assert.Contains("/Admin/Backups", layout);
        Assert.Contains("STAGING - Non-production data", layout);
    }

    [Fact]
    public void Backup_service_excludes_secret_values_from_config_snapshot()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BackupService.cs"));
        var snapshotStart = service.IndexOf("private object BuildSafeConfigurationSnapshot", StringComparison.Ordinal);
        var snapshotEnd = service.IndexOf("private async Task<object> BuildSchemaManifestAsync", StringComparison.Ordinal);
        Assert.True(snapshotStart >= 0, "Could not find backup configuration snapshot method.");
        Assert.True(snapshotEnd > snapshotStart, "Could not find backup photo manifest method after snapshot method.");
        var snapshot = service[snapshotStart..snapshotEnd];

        Assert.Contains("BuildSafeConfigurationSnapshot", snapshot);
        Assert.DoesNotContain("ServiceAccountJson", snapshot);
        Assert.DoesNotContain("ClientSecret", snapshot);
        Assert.DoesNotContain("AccessToken", snapshot);
        Assert.Contains("qcDefaultRecipientsConfigured", snapshot);
        Assert.Contains("googleDriveRootConfigured", snapshot);
    }

    [Fact]
    public void Backup_service_creates_photo_manifest_and_pg_dump_warning()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BackupService.cs"));

        Assert.Contains("BuildPhotoManifestAsync", service);
        Assert.Contains("PhotoType", service);
        Assert.Contains("FileId", service);
        Assert.Contains("ReceiptId", service);
        Assert.Contains("pg_dump is not installed", service);
        Assert.Contains("VerifyUploadedPackageAsync", service);
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
        Assert.Contains("--run-backup=predeployment", releaseChecklist);
        Assert.Contains("STAGING - Non-production data", stagingChecklist);
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
        Assert.Contains("0AJIU41AM__WNUk9PVA", view);
        Assert.Contains("Parsed folder ID", view);
    }

    [Fact]
    public void Backup_folder_url_parses_folder_id_and_discards_query_string()
    {
        var folder = BackupOptions.NormalizeGoogleDriveFolderId("https://drive.google.com/drive/folders/0AJIU41AM__WNUk9PVA?dmr=1&ec=wgc-drive-%5Bmodule%5D-goto");

        Assert.Equal("0AJIU41AM__WNUk9PVA", folder);
        Assert.Equal("0AJIU41AM__WNUk9PVA", BackupOptions.NormalizeGoogleDriveFolderId("0AJIU41AM__WNUk9PVA"));
    }

    [Fact]
    public void Retention_keeps_last_verified_and_one_weekly_per_iso_week()
    {
        var now = new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero);
        var runs = new List<BackupRunRecord>
        {
            Run(1, BackupRunTypes.Daily, now.AddDays(-60)),
            Run(2, BackupRunTypes.Weekly, new DateTimeOffset(2026, 12, 27, 12, 0, 0, TimeSpan.Zero)),
            Run(3, BackupRunTypes.Weekly, new DateTimeOffset(2026, 12, 28, 12, 0, 0, TimeSpan.Zero)),
            Run(4, BackupRunTypes.Weekly, new DateTimeOffset(2026, 12, 29, 12, 0, 0, TimeSpan.Zero)),
            Run(5, BackupRunTypes.Daily, now)
        };

        var prune = BackupRetentionPolicy.SelectForPruning(runs, now, 30, 52, 5);

        Assert.Contains(prune, x => x.Id == 1);
        Assert.Contains(prune, x => x.Id == 3);
        Assert.DoesNotContain(prune, x => x.Id == 2);
        Assert.DoesNotContain(prune, x => x.Id == 4);
        Assert.DoesNotContain(prune, x => x.Id == 5);
    }

    [Fact]
    public void Retention_never_prunes_the_only_verified_backup()
    {
        var now = new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero);
        var only = Run(1, BackupRunTypes.Daily, now.AddYears(-2));
        Assert.Empty(BackupRetentionPolicy.SelectForPruning([only], now, 30, 52, 999));
    }

    [Fact]
    public void Retention_prunes_weekly_points_older_than_52_weeks()
    {
        var now = new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero);
        var old = Run(1, BackupRunTypes.Weekly, now.AddDays(-400));
        var current = Run(2, BackupRunTypes.Weekly, now);
        Assert.Contains(BackupRetentionPolicy.SelectForPruning([old, current], now, 30, 52, 2), x => x.Id == 1);
    }

    [Fact]
    public void Backup_package_verification_rejects_component_tampering()
    {
        var valid = CreateVerifiedPackage();
        BackupService.VerifyPackage(valid);
        using var stream = new MemoryStream(valid);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            using var config = zip.GetEntry("config.json")!.Open();
            config.Write(Encoding.UTF8.GetBytes("[]"));
        }
        Assert.ThrowsAny<Exception>(() => BackupService.VerifyPackage(stream.ToArray()));
    }

    [Fact]
    public void Render_declares_nightly_backup_and_predeployment_command_is_documented()
    {
        var render = File.ReadAllText(FindRepositoryFile("render.yaml"));
        var agents = File.ReadAllText(FindRepositoryFile("AGENTS.md"));
        var dockerfile = File.ReadAllText(FindRepositoryFile("Dockerfile"));
        var backupService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BackupService.cs"));
        Assert.Contains("crop-qc-production-nightly-backup", render);
        Assert.Contains("0 8,9 * * *", render);
        Assert.Contains("--run-backup=scheduled", render);
        Assert.Contains("RunScheduledCandidateAsync", backupService);
        Assert.Contains("--run-backup=predeployment", agents);
        Assert.Contains("SHA-256", agents);
        Assert.Contains("Stop", agents);
        Assert.Contains("aspnet:9.0-noble", dockerfile);
        Assert.Contains("postgresql-client-18", dockerfile);
        Assert.Contains("info.Environment[\"PGPASSWORD\"]", backupService);
        Assert.DoesNotContain("--dbname={connectionString}", backupService);
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

    private static BackupRunRecord Run(long id, string category, DateTimeOffset startedAt) => new()
    {
        Id = id,
        BackupType = category,
        RetentionCategory = category,
        Status = BackupRunStatuses.Succeeded,
        EnvironmentName = "Test",
        DatabaseProvider = "PostgreSQL",
        StartedAt = startedAt,
        VerifiedAt = startedAt
    };

    private static byte[] CreateVerifiedPackage()
    {
        var sql = Encoding.UTF8.GetBytes("--\n-- PostgreSQL database dump\n--\nSELECT 1;\n");
        byte[] dump;
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)) gzip.Write(sql);
            dump = output.ToArray();
        }
        var config = Encoding.UTF8.GetBytes("{}");
        var schema = Encoding.UTF8.GetBytes("{}");
        var photos = Encoding.UTF8.GetBytes("[]");
        var components = new[]
        {
            Component("db.sql.gz", dump), Component("config.json", config), Component("schema.json", schema), Component("photos.json", photos)
        };
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { components });
        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, true))
        {
            Write(zip, "db.sql.gz", dump); Write(zip, "config.json", config); Write(zip, "schema.json", schema); Write(zip, "photos.json", photos); Write(zip, "backup-manifest.json", manifest);
        }
        return package.ToArray();
    }

    private static object Component(string name, byte[] bytes) => new { name, sizeBytes = bytes.LongLength, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    private static void Write(ZipArchive zip, string name, byte[] bytes) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "CropQc.Web";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
