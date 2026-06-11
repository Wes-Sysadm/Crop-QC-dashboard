using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IBackupService
{
    Task<BackupStatusViewModel> GetStatusAsync(CancellationToken cancellationToken);
    Task<BackupRunResult> RunBackupNowAsync(string requestedByEmail, CancellationToken cancellationToken);
    Task<BackupRunResult> TestGoogleDriveAccessAsync(string requestedByEmail, CancellationToken cancellationToken);
}

public sealed class BackupService(
    CropQcDbContext dbContext,
    IConfiguration configuration,
    BackupOptions options,
    GoogleDriveStorageOptions googleDriveOptions,
    AppEnvironmentOptions appEnvironment,
    ILogger<BackupService> logger) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<BackupStatusViewModel> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = new BackupStatusViewModel
        {
            Enabled = options.Enabled,
            Provider = options.Provider,
            GoogleDriveFolderConfigured = options.GoogleDriveFolderConfigured,
            DatabaseBackupEnabled = options.DatabaseBackupEnabled,
            ConfigBackupEnabled = options.ConfigBackupEnabled,
            PhotoManifestEnabled = options.PhotoManifestEnabled,
            RetentionDays = options.RetentionDays,
            ScheduleUtcHour = options.ScheduleUtcHour
        };

        status.LastDatabaseBackupAt = await GetConfigDateAsync(BackupStatusKeys.LastDatabaseBackupAt, cancellationToken);
        status.LastConfigBackupAt = await GetConfigDateAsync(BackupStatusKeys.LastConfigBackupAt, cancellationToken);
        status.LastPhotoManifestBackupAt = await GetConfigDateAsync(BackupStatusKeys.LastPhotoManifestBackupAt, cancellationToken);
        status.LastDatabaseBackupFileName = await GetConfigValueAsync(BackupStatusKeys.LastDatabaseBackupFileName, cancellationToken);
        status.LastConfigBackupFileName = await GetConfigValueAsync(BackupStatusKeys.LastConfigBackupFileName, cancellationToken);
        status.LastPhotoManifestBackupFileName = await GetConfigValueAsync(BackupStatusKeys.LastPhotoManifestBackupFileName, cancellationToken);
        status.LastError = await GetConfigValueAsync(BackupStatusKeys.LastError, cancellationToken);
        status.Warnings = BuildSafetyWarnings();
        return status;
    }

    public async Task<BackupRunResult> TestGoogleDriveAccessAsync(string requestedByEmail, CancellationToken cancellationToken)
    {
        await AddAuditAsync("BackupTestRequested", "Backup", "GoogleDrive", null, new { requestedByEmail }, cancellationToken);
        if (!options.Enabled)
        {
            return BackupRunResult.Failed("Backups are disabled. Set Backups__Enabled=true before testing Google Drive backup access.");
        }

        if (!options.IsGoogleDrive || !options.GoogleDriveFolderConfigured)
        {
            return BackupRunResult.Failed("Google Drive backup folder is not configured. Set Backups__Provider=GoogleDrive and Backups__GoogleDriveFolderId.");
        }

        try
        {
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes($"Crop QC backup access test {DateTimeOffset.UtcNow:O}"));
            var storage = CreateBackupStorage();
            var reference = await storage.SaveAsync(new FileStorageSaveRequest(
                content,
                "Backup Access Tests",
                BackupFileNames.AccessTest(DateTimeOffset.UtcNow),
                "text/plain",
                content.Length), cancellationToken);
            await AddAuditAsync("BackupTestCompleted", "Backup", reference.FileName, null, new { reference.WebUrl }, cancellationToken);
            return BackupRunResult.Succeeded($"Google Drive backup access succeeded. Uploaded {reference.FileName}.", [reference]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Drive backup access test failed.");
            await AddAuditAsync("BackupTestFailed", "Backup", "GoogleDrive", null, new { error = ex.Message }, cancellationToken);
            return BackupRunResult.Failed($"Google Drive backup access failed: {ex.Message}");
        }
    }

    public async Task<BackupRunResult> RunBackupNowAsync(string requestedByEmail, CancellationToken cancellationToken)
    {
        await AddAuditAsync("BackupStarted", "Backup", "Manual", null, new { requestedByEmail }, cancellationToken);
        if (!options.Enabled)
        {
            return await FinishFailureAsync("Backups are disabled. Set Backups__Enabled=true.", cancellationToken);
        }

        if (!options.IsGoogleDrive || !options.GoogleDriveFolderConfigured)
        {
            return await FinishFailureAsync("Google Drive backup folder is not configured. Set Backups__GoogleDriveFolderId.", cancellationToken);
        }

        var uploaded = new List<FileStorageReference>();
        var messages = new List<string>();
        var timestamp = DateTimeOffset.UtcNow;
        try
        {
            var storage = CreateBackupStorage();

            if (options.DatabaseBackupEnabled)
            {
                var dbResult = await TryRunDatabaseBackupAsync(storage, timestamp, cancellationToken);
                messages.Add(dbResult.Message);
                uploaded.AddRange(dbResult.UploadedFiles);
            }

            if (options.ConfigBackupEnabled)
            {
                var config = BuildSafeConfigurationSnapshot();
                var fileName = BackupFileNames.Config(timestamp);
                var reference = await UploadJsonAsync(storage, "Config", fileName, config, cancellationToken);
                await SetConfigValueAsync(BackupStatusKeys.LastConfigBackupAt, timestamp.ToString("O"), cancellationToken);
                await SetConfigValueAsync(BackupStatusKeys.LastConfigBackupFileName, fileName, cancellationToken);
                uploaded.Add(reference);
                messages.Add($"Config snapshot uploaded: {fileName}");
            }

            if (options.PhotoManifestEnabled)
            {
                var manifest = await BuildPhotoManifestAsync(cancellationToken);
                var fileName = BackupFileNames.PhotoManifest(timestamp);
                var reference = await UploadJsonAsync(storage, "Photo Manifests", fileName, manifest, cancellationToken);
                await SetConfigValueAsync(BackupStatusKeys.LastPhotoManifestBackupAt, timestamp.ToString("O"), cancellationToken);
                await SetConfigValueAsync(BackupStatusKeys.LastPhotoManifestBackupFileName, fileName, cancellationToken);
                uploaded.Add(reference);
                messages.Add($"Photo manifest uploaded: {fileName}");
            }

            await SetConfigValueAsync(BackupStatusKeys.LastError, "", cancellationToken);
            await AddAuditAsync("BackupCompleted", "Backup", "Manual", null, new { files = uploaded.Select(x => x.FileName).ToArray() }, cancellationToken);
            return BackupRunResult.Succeeded(string.Join(" ", messages.Where(x => !string.IsNullOrWhiteSpace(x))), uploaded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual backup failed.");
            return await FinishFailureAsync($"Backup failed: {ex.Message}", cancellationToken);
        }
    }

    private async Task<BackupRunResult> FinishFailureAsync(string message, CancellationToken cancellationToken)
    {
        await SetConfigValueAsync(BackupStatusKeys.LastError, message, cancellationToken);
        await AddAuditAsync("BackupFailed", "Backup", "Manual", null, new { error = message }, cancellationToken);
        return BackupRunResult.Failed(message);
    }

    private async Task<BackupRunResult> TryRunDatabaseBackupAsync(IFileStorageService storage, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        if (!await IsPgDumpAvailableAsync(cancellationToken))
        {
            var message = "pg_dump is not available in this runtime. Configure external Render/Postgres backup or run the backup job from a worker with PostgreSQL tools.";
            await SetConfigValueAsync(BackupStatusKeys.LastError, message, cancellationToken);
            return BackupRunResult.Failed(message);
        }

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return BackupRunResult.Failed("Database connection string is not available for pg_dump.");
        }

        var fileName = BackupFileNames.Database(timestamp);
        await using var sql = await RunPgDumpAsync(connectionString, cancellationToken);
        await using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await sql.CopyToAsync(gzip, cancellationToken);
        }

        compressed.Position = 0;
        var reference = await storage.SaveAsync(new FileStorageSaveRequest(
            compressed,
            "Database",
            fileName,
            "application/gzip",
            compressed.Length), cancellationToken);
        await SetConfigValueAsync(BackupStatusKeys.LastDatabaseBackupAt, timestamp.ToString("O"), cancellationToken);
        await SetConfigValueAsync(BackupStatusKeys.LastDatabaseBackupFileName, fileName, cancellationToken);
        return BackupRunResult.Succeeded($"Database backup uploaded: {fileName}", [reference]);
    }

    private static async Task<bool> IsPgDumpAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = StartProcess("pg_dump", "--version");
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Stream> RunPgDumpAsync(string connectionString, CancellationToken cancellationToken)
    {
        using var process = StartProcess("pg_dump", $"--no-owner --no-privileges \"{connectionString.Replace("\"", "\\\"")}\"")
            ?? throw new InvalidOperationException("pg_dump could not be started.");
        var output = new MemoryStream();
        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var readError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await copyOutput;
        var error = await readError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"pg_dump failed: {error}");
        }

        output.Position = 0;
        return output;
    }

    private static Process? StartProcess(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        return Process.Start(startInfo);
    }

    private async Task<FileStorageReference> UploadJsonAsync(IFileStorageService storage, string targetPath, string fileName, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reference = await storage.SaveAsync(new FileStorageSaveRequest(content, targetPath, fileName, "application/json", content.Length), cancellationToken);
        await AddAuditAsync("BackupFileUploaded", "Backup", fileName, null, new { reference.WebUrl, reference.FileSizeBytes }, cancellationToken);
        return reference;
    }

    private IFileStorageService CreateBackupStorage()
    {
        var backupDriveOptions = new GoogleDriveStorageOptions
        {
            UseSharedDrive = googleDriveOptions.UseSharedDrive,
            RootFolderId = options.GoogleDriveFolderId ?? "",
            SharedDriveId = googleDriveOptions.SharedDriveId,
            ServiceAccountJson = googleDriveOptions.ServiceAccountJson,
            ServiceAccountJsonPath = googleDriveOptions.ServiceAccountJsonPath,
            ApplicationName = googleDriveOptions.ApplicationName,
            BaseFolderName = "Backups"
        };

        return new GoogleDriveStorageService(backupDriveOptions);
    }

    private object BuildSafeConfigurationSnapshot() =>
        new
        {
            createdAt = DateTimeOffset.UtcNow,
            environment = new
            {
                appEnvironment.Kind,
                appEnvironment.DisplayName
            },
            appVersion = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"],
            email = new
            {
                provider = configuration["Email:Provider"],
                qcDefaultRecipientsConfigured = !string.IsNullOrWhiteSpace(configuration["Email:QcDefaultRecipients"])
            },
            authentication = new
            {
                allowedGoogleDomains = configuration["Authentication:AllowedGoogleDomains"]
            },
            storage = new
            {
                provider = configuration["FileStorage:Provider"],
                googleDriveRootFolderId = configuration["GoogleDrive:RootFolderId"],
                googleDriveSharedDriveId = configuration["GoogleDrive:SharedDriveId"],
                googleDriveBaseFolderName = configuration["GoogleDrive:BaseFolderName"]
            },
            backups = new
            {
                options.Enabled,
                options.Provider,
                googleDriveFolderId = options.GoogleDriveFolderId,
                options.RetentionDays,
                options.ScheduleUtcHour,
                options.DatabaseBackupEnabled,
                options.PhotoManifestEnabled,
                options.ConfigBackupEnabled
            },
            downloads = new
            {
                masterFolderConfigured = !string.IsNullOrWhiteSpace(configuration["Downloads:MasterFolderUrl"]),
                qcStationInstallerConfigured = !string.IsNullOrWhiteSpace(configuration["Downloads:QcStationInstallerUrl"])
            }
        };

    private async Task<IReadOnlyList<object>> BuildPhotoManifestAsync(CancellationToken cancellationToken)
    {
        var manifest = await dbContext.QcPhotos.AsNoTracking()
            .Include(x => x.Receipt)
            .Include(x => x.QcSample)
                .ThenInclude(x => x!.Receipt)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                photoId = x.Id,
                sampleId = x.QcSampleId,
                receiptId = x.ReceiptId ?? x.QcSample!.ReceiptId,
                receiptDisplayId = x.Receipt != null ? x.Receipt.CompuTechReceiptId : x.QcSample!.Receipt.CompuTechReceiptId,
                photoType = x.PhotoType,
                storageProvider = x.StorageProvider,
                driveId = x.DriveId,
                fileId = x.FileId,
                folderId = x.FolderId,
                fileName = x.FileName,
                contentType = x.ContentType,
                fileSizeBytes = x.FileSizeBytes,
                webUrl = x.WebUrl,
                capturedAt = x.CapturedAt,
                uploadedAt = x.UploadedAt
            })
            .ToListAsync(cancellationToken);

        return manifest.Cast<object>().ToList();
    }

    private IReadOnlyList<string> BuildSafetyWarnings()
    {
        var warnings = new List<string>();
        if (appEnvironment.IsProduction && !options.GoogleDriveFolderConfigured)
        {
            warnings.Add("Production backup folder is not configured. Set Backups__GoogleDriveFolderId before live use.");
        }

        if (appEnvironment.IsProduction && configuration.GetValue("Database:SeedMasterDataOnStartup", false))
        {
            warnings.Add("Production has Database__SeedMasterDataOnStartup enabled. Disable test/seed behavior for live data.");
        }

        if (appEnvironment.IsProduction && configuration.GetValue("Database:EnsureCreatedOnStartup", false))
        {
            warnings.Add("Production has Database__EnsureCreatedOnStartup enabled. Use reviewed migrations/backups instead of runtime schema reset patterns.");
        }

        var connectionString = dbContext.Database.GetConnectionString() ?? "";
        if (appEnvironment.IsProduction
            && (connectionString.Contains("dev", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("test", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("staging", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Production database connection string appears to reference dev/test/staging. Verify Render is using the production Postgres database.");
        }

        if (appEnvironment.IsStagingLike && appEnvironment.DisplayName.Contains("Production", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Staging/Test environment display name includes Production. Rename AppEnvironment__DisplayName to prevent confusion.");
        }

        return warnings;
    }

    private async Task<string?> GetConfigValueAsync(string key, CancellationToken cancellationToken) =>
        (await dbContext.DashboardConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, cancellationToken))?.Value;

    private async Task<DateTimeOffset?> GetConfigDateAsync(string key, CancellationToken cancellationToken)
    {
        var value = await GetConfigValueAsync(key, cancellationToken);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task SetConfigValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var item = await dbContext.DashboardConfigurations.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (item is null)
        {
            dbContext.DashboardConfigurations.Add(new DashboardConfiguration
            {
                Key = key,
                Value = value,
                Description = "Backup status value managed by the backup service.",
                ValueType = "String",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            item.Value = value;
            item.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AddAuditAsync(string action, string entityName, string entityKey, object? before, object? after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class BackupFileNames
{
    public static string Database(DateTimeOffset timestamp) => $"cropqc-prod-db-{Format(timestamp)}.sql.gz";
    public static string Config(DateTimeOffset timestamp) => $"cropqc-prod-config-{Format(timestamp)}.json";
    public static string PhotoManifest(DateTimeOffset timestamp) => $"cropqc-prod-photo-manifest-{Format(timestamp)}.json";
    public static string AccessTest(DateTimeOffset timestamp) => $"cropqc-backup-access-test-{Format(timestamp)}.txt";
    private static string Format(DateTimeOffset timestamp) => timestamp.UtcDateTime.ToString("yyyyMMdd-HHmmss");
}

public static class BackupStatusKeys
{
    public const string LastDatabaseBackupAt = "BackupLastDatabaseBackupAt";
    public const string LastConfigBackupAt = "BackupLastConfigBackupAt";
    public const string LastPhotoManifestBackupAt = "BackupLastPhotoManifestBackupAt";
    public const string LastDatabaseBackupFileName = "BackupLastDatabaseBackupFileName";
    public const string LastConfigBackupFileName = "BackupLastConfigBackupFileName";
    public const string LastPhotoManifestBackupFileName = "BackupLastPhotoManifestBackupFileName";
    public const string LastError = "BackupLastError";
}

public sealed record BackupRunResult(bool Success, string Message, IReadOnlyList<FileStorageReference> UploadedFiles)
{
    public static BackupRunResult Succeeded(string message, IReadOnlyList<FileStorageReference> uploadedFiles) => new(true, message, uploadedFiles);
    public static BackupRunResult Failed(string message) => new(false, message, []);
}
