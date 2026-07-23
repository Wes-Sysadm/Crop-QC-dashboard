using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropQc.Web.Services;

public interface IBackupService
{
    Task<BackupStatusViewModel> GetStatusAsync(CancellationToken cancellationToken);
    Task<string?> SaveSettingsAsync(BackupSettingsForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<BackupRunResult> RunBackupNowAsync(string requestedByEmail, CancellationToken cancellationToken);
    Task<BackupRunResult> RunBackupAsync(string backupType, string requestedBy, CancellationToken cancellationToken);
    Task<BackupRunResult> RunScheduledCandidateAsync(CancellationToken cancellationToken);
    Task<BackupRunResult> TestGoogleDriveAccessAsync(string requestedByEmail, CancellationToken cancellationToken);
}

public sealed class BackupService(
    CropQcDbContext dbContext,
    IConfiguration configuration,
    BackupOptions options,
    GoogleDriveStorageOptions googleDriveOptions,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    IBackupNotificationService notificationService,
    ILogger<BackupService> logger) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(2);

    public async Task<BackupStatusViewModel> GetStatusAsync(CancellationToken cancellationToken)
    {
        var effective = await GetEffectiveOptionsAsync(cancellationToken);
        var runs = await dbContext.BackupRunRecords.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(25).ToListAsync(cancellationToken);
        var items = runs.Select(ToListItem).ToList();
        var notifications = await dbContext.BackupNotificationRecords.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(25)
            .Select(x => new BackupNotificationListItem(x.Id, x.BackupRunId, x.NotificationType, x.Recipient, x.Status, x.AttemptCount, x.CreatedAt, x.LastAttemptedAt, x.SentAt, x.ErrorSummary))
            .ToListAsync(cancellationToken);
        var lastSuccessfulNightly = runs.FirstOrDefault(x => x.ScheduledPacificDate != null && x.Status == BackupRunStatuses.Succeeded);
        var lastFailedNightly = runs.FirstOrDefault(x => x.ScheduledPacificDate != null && x.Status == BackupRunStatuses.Failed);
        return new BackupStatusViewModel
        {
            Enabled = effective.Enabled,
            Provider = effective.Provider,
            GoogleDriveFolderConfigured = effective.GoogleDriveFolderConfigured,
            GoogleDriveFolderId = effective.GoogleDriveFolderId,
            GoogleDriveFolderDisplay = MaskFolderId(effective.GoogleDriveFolderId),
            DatabaseBackupEnabled = effective.DatabaseBackupEnabled,
            ConfigBackupEnabled = effective.ConfigBackupEnabled,
            PhotoManifestEnabled = effective.PhotoManifestEnabled,
            DailyRetentionDays = effective.DailyRetentionDays,
            WeeklyRetentionWeeks = effective.WeeklyRetentionWeeks,
            NightlyPacificHour = effective.NightlyPacificHour,
            BusinessTimeZone = effective.BusinessTimeZone,
            NextScheduledBackupUtc = businessTime.NextNightlyBackupUtc(),
            LastAttempt = items.FirstOrDefault(),
            LastSuccessful = items.FirstOrDefault(x => x.Status == BackupRunStatuses.Succeeded),
            LastSuccessfulNightly = lastSuccessfulNightly is null ? null : ToListItem(lastSuccessfulNightly),
            LastFailedNightly = lastFailedNightly is null ? null : ToListItem(lastFailedNightly),
            RecentRuns = items,
            RecentNotifications = notifications,
            LastDatabaseBackupAt = await GetConfigDateAsync(BackupStatusKeys.LastDatabaseBackupAt, cancellationToken),
            LastDatabaseBackupFileName = await GetConfigValueAsync(BackupStatusKeys.LastDatabaseBackupFileName, cancellationToken),
            LastError = await GetConfigValueAsync(BackupStatusKeys.LastError, cancellationToken),
            Warnings = BuildSafetyWarnings(effective),
            SettingsForm = new BackupSettingsForm
            {
                Enabled = effective.Enabled,
                Provider = effective.Provider,
                GoogleDriveFolder = effective.GoogleDriveFolderId,
                DailyRetentionDays = effective.DailyRetentionDays,
                WeeklyRetentionWeeks = effective.WeeklyRetentionWeeks,
                DatabaseBackupEnabled = effective.DatabaseBackupEnabled,
                ConfigBackupEnabled = effective.ConfigBackupEnabled,
                PhotoManifestEnabled = effective.PhotoManifestEnabled
            }
        };
    }

    public async Task<string?> SaveSettingsAsync(BackupSettingsForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var folderId = BackupOptions.NormalizeGoogleDriveFolderId(form.GoogleDriveFolder);
        if (string.IsNullOrWhiteSpace(folderId)) return "Google Drive Backup Folder is required.";
        var values = new Dictionary<string, string>
        {
            ["Backups:Enabled"] = form.Enabled.ToString(),
            ["Backups:Provider"] = BackupProviders.GoogleDrive,
            ["Backups:GoogleDriveFolderId"] = folderId,
            ["Backups:DailyRetentionDays"] = Math.Clamp(form.DailyRetentionDays, 30, 3650).ToString(CultureInfo.InvariantCulture),
            ["Backups:WeeklyRetentionWeeks"] = Math.Clamp(form.WeeklyRetentionWeeks, 52, 520).ToString(CultureInfo.InvariantCulture),
            ["Backups:DatabaseBackupEnabled"] = bool.TrueString,
            ["Backups:ConfigBackupEnabled"] = bool.TrueString,
            ["Backups:PhotoManifestEnabled"] = bool.TrueString
        };
        foreach (var value in values) await SetConfigValueAsync(value.Key, value.Value, cancellationToken);
        await AddAuditAsync("BackupSettingsUpdated", "Settings", new { changedByEmail, folderConfigured = true, form.Enabled, form.DailyRetentionDays, form.WeeklyRetentionWeeks }, cancellationToken);
        return null;
    }

    public Task<BackupRunResult> RunBackupNowAsync(string requestedByEmail, CancellationToken cancellationToken) =>
        RunBackupAsync(BackupRunTypes.Manual, requestedByEmail, cancellationToken);

    public async Task<BackupRunResult> RunScheduledCandidateAsync(CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        if (!businessTime.IsNightlyCandidate(now))
        {
            return BackupRunResult.Skipped($"Scheduled candidate skipped because {businessTime.FormatPacific(now, "yyyy-MM-dd h:mm tt")} is outside the 1:00 AM Pacific window.");
        }

        var pacificDate = businessTime.PacificDate(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (await dbContext.BackupNightlyRunGuards.AsNoTracking().AnyAsync(x => x.PacificDate == pacificDate, cancellationToken))
        {
            return BackupRunResult.Skipped($"Nightly backup for Pacific date {pacificDate} already ran or is in progress.");
        }

        var guard = new BackupNightlyRunGuard
        {
            PacificDate = pacificDate,
            CreatedAt = now,
            Result = "Running"
        };
        dbContext.BackupNightlyRunGuards.Add(guard);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(guard).State = EntityState.Detached;
            if (!await dbContext.BackupNightlyRunGuards.AsNoTracking().AnyAsync(x => x.PacificDate == pacificDate, cancellationToken))
            {
                throw;
            }

            return BackupRunResult.Skipped($"Nightly backup for Pacific date {pacificDate} was claimed by another candidate.");
        }

        var result = await RunBackupAsync(BackupRunTypes.Daily, $"scheduled:{pacificDate}", cancellationToken);
        guard.BackupRunId = result.RunId;
        guard.CompletedAt = businessTime.UtcNow;
        guard.Result = result.Success ? "Succeeded" : "Failed";
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<BackupRunResult> RunBackupAsync(string backupType, string requestedBy, CancellationToken cancellationToken)
    {
        var effective = await GetEffectiveOptionsAsync(cancellationToken);
        if (!effective.Enabled) return BackupRunResult.Failed("Backups are disabled.");
        if (!effective.IsGoogleDrive || !effective.GoogleDriveFolderConfigured) return BackupRunResult.Failed("Google Drive backup folder is not configured.");

        var now = businessTime.UtcNow;
        backupType = NormalizeBackupType(backupType, now, effective.BusinessTimeZone);
        var leaseId = Guid.NewGuid();
        if (!await TryAcquireLeaseAsync(leaseId, now, cancellationToken))
        {
            return BackupRunResult.Failed("Another backup is already running.");
        }

        BackupRunRecord? run = null;
        BackupRunResult result;
        string? notificationType = null;
        var failureStage = "Initialization";
        var incompleteObjectCreated = false;
        try
        {
            run = new BackupRunRecord
            {
                BackupType = backupType,
                Status = BackupRunStatuses.Running,
                EnvironmentName = appEnvironment.DisplayName,
                DatabaseProvider = dbContext.Database.ProviderName ?? configuration["DATABASE_PROVIDER"] ?? "Unknown",
                DeployedCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"],
                RequestedBy = string.IsNullOrWhiteSpace(requestedBy) ? null : requestedBy,
                RetentionCategory = backupType,
                StartedAt = now,
                ScheduledPacificDate = requestedBy.StartsWith("scheduled:", StringComparison.OrdinalIgnoreCase)
                    ? requestedBy["scheduled:".Length..]
                    : null
            };
            dbContext.BackupRunRecords.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddAuditAsync("BackupStarted", run.Id.ToString(CultureInfo.InvariantCulture), new { run.BackupType, run.RequestedBy }, cancellationToken);

            failureStage = "Package creation";
            var storage = CreateBackupStorage(effective);
            var package = await BuildPackageAsync(run, effective, cancellationToken);
            await using var packageStream = package.Content;
            var targetPath = $"Crop QC Backups/Production/{BackupFolder(run.BackupType)}";
            package.Content.Position = 0;
            failureStage = "Google Drive package upload";
            var uploaded = await storage.SaveAsync(new FileStorageSaveRequest(package.Content, targetPath, package.FileName, "application/zip", package.Size), cancellationToken);
            incompleteObjectCreated = true;
            failureStage = "Uploaded package read-back verification";
            await VerifyUploadedPackageAsync(storage, uploaded, package, cancellationToken);

            var sidecar = JsonSerializer.SerializeToUtf8Bytes(new
            {
                backupRunId = run.Id,
                package = package.FileName,
                packageSizeBytes = package.Size,
                packageSha256 = package.Sha256,
                verifiedAt = businessTime.UtcNow,
                uploaded.StorageProvider,
                uploaded.StorageKey,
                uploaded.TargetPath
            }, JsonOptions);
            var manifestFileName = Path.ChangeExtension(package.FileName, ".manifest.json");
            await using var sidecarStream = new MemoryStream(sidecar);
            failureStage = "Manifest upload";
            var manifestRef = await storage.SaveAsync(new FileStorageSaveRequest(sidecarStream, "Crop QC Backups/Production/Manifests", manifestFileName, "application/json", sidecar.Length), cancellationToken);
            failureStage = "Manifest read-back verification";
            await VerifyUploadedBytesAsync(storage, manifestRef, sidecar, cancellationToken);

            var verified = businessTime.UtcNow;
            run.PackageFileName = package.FileName;
            run.PackageStorageKey = uploaded.StorageKey;
            run.PackageWebUrl = uploaded.WebUrl;
            run.ManifestFileName = manifestFileName;
            run.ManifestStorageKey = manifestRef.StorageKey;
            run.FileSizeBytes = package.Size;
            run.Sha256 = package.Sha256;
            run.VerifiedAt = verified;
            await dbContext.SaveChangesAsync(cancellationToken);
            failureStage = "Retention processing";
            await ApplyRetentionAsync(storage, effective, verified, run.Id, cancellationToken);
            run.RetentionProcessedAt = businessTime.UtcNow;
            await SetConfigValueAsync(BackupStatusKeys.LastDatabaseBackupAt, verified.ToString("O"), cancellationToken);
            await SetConfigValueAsync(BackupStatusKeys.LastDatabaseBackupFileName, package.FileName, cancellationToken);
            await SetConfigValueAsync(BackupStatusKeys.LastError, "", cancellationToken);
            run.Status = BackupRunStatuses.Succeeded;
            notificationType = BackupNotificationTypes.Success;
            result = BackupRunResult.Succeeded($"Verified {run.BackupType} backup completed: {package.FileName} ({package.Size} bytes, SHA-256 {package.Sha256}).", [uploaded], run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup run {BackupRunId} failed during {BackupType} backup.", run?.Id, backupType);
            var safeError = SafeError(ex);
            try
            {
                if (run is not null && run.Id > 0)
                {
                    run.Status = BackupRunStatuses.Failed;
                    run.FailureStage = failureStage;
                    run.IncompleteObjectCreated = incompleteObjectCreated;
                    run.ErrorSummary = safeError;
                    await SetConfigValueAsync(BackupStatusKeys.LastError, safeError, cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await AddAuditAsync("BackupFailed", run.Id.ToString(CultureInfo.InvariantCulture), new { error = safeError }, cancellationToken);
                    notificationType = BackupNotificationTypes.Failure;
                }
            }
            catch (Exception historyException)
            {
                logger.LogError(historyException, "Backup failure history could not be persisted for run {BackupRunId}.", run?.Id);
            }
            result = BackupRunResult.Failed(safeError, run?.Id);
        }
        finally
        {
            try
            {
                await ReleaseLeaseAsync(leaseId, CancellationToken.None);
                if (run is not null && run.Id > 0)
                {
                    run.LeaseReleasedAt = businessTime.UtcNow;
                    run.CompletedAt = run.LeaseReleasedAt;
                    run.DurationMilliseconds = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception leaseException)
            {
                logger.LogError(leaseException, "Backup run {BackupRunId} could not release its operation lease.", run?.Id);
                if (run is not null && run.Id > 0)
                {
                    run.Status = BackupRunStatuses.Failed;
                    run.FailureStage = "Lease release";
                    run.ErrorSummary = "Backup verification finished, but the operation lease could not be released. Production-changing operations remain blocked.";
                    run.CompletedAt = businessTime.UtcNow;
                    run.DurationMilliseconds = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
                    notificationType = BackupNotificationTypes.Failure;
                    result = BackupRunResult.Failed(run.ErrorSummary, run.Id);
                    try
                    {
                        await dbContext.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (Exception historyException)
                    {
                        logger.LogError(historyException, "Lease-release failure history could not be persisted for backup run {BackupRunId}.", run.Id);
                    }
                }
            }
        }

        if (run is not null && notificationType is not null)
        {
            try
            {
                if (notificationType == BackupNotificationTypes.Success)
                {
                    await AddAuditAsync("BackupCompleted", run.Id.ToString(CultureInfo.InvariantCulture), new { run.PackageFileName, run.FileSizeBytes, run.Sha256, verified = true, retentionProcessed = run.RetentionProcessedAt is not null, leaseReleased = run.LeaseReleasedAt is not null }, CancellationToken.None);
                }

                await notificationService.QueueAsync(run.Id, notificationType, CancellationToken.None);
            }
            catch (Exception notificationException)
            {
                logger.LogError(notificationException, "Backup notification could not be queued for run {BackupRunId}; the completed backup result is unchanged.", run.Id);
                try
                {
                    await AddAuditAsync("BackupNotificationQueueFailed", run.Id.ToString(CultureInfo.InvariantCulture), new { notificationType, error = SafeError(notificationException) }, CancellationToken.None);
                }
                catch (Exception auditException)
                {
                    logger.LogError(auditException, "Backup notification queue failure audit could not be recorded for run {BackupRunId}.", run.Id);
                }
            }
        }

        return result;
    }

    public async Task<BackupRunResult> TestGoogleDriveAccessAsync(string requestedByEmail, CancellationToken cancellationToken)
    {
        var effective = await GetEffectiveOptionsAsync(cancellationToken);
        if (!effective.Enabled || !effective.GoogleDriveFolderConfigured) return BackupRunResult.Failed("Backups and a Google Drive backup folder must be configured first.");
        try
        {
            var bytes = Encoding.UTF8.GetBytes($"Crop QC backup access verification {businessTime.UtcNow:O}");
            await using var stream = new MemoryStream(bytes);
            var storage = CreateBackupStorage(effective);
            var reference = await storage.SaveAsync(new FileStorageSaveRequest(stream, "Crop QC Backups/Failed/Access Tests", BackupFileNames.AccessTest(businessTime.UtcNow), "text/plain", bytes.Length), cancellationToken);
            await VerifyUploadedBytesAsync(storage, reference, bytes, cancellationToken);
            await AddAuditAsync("BackupAccessVerified", reference.FileName, new { requestedByEmail, reference.FileSizeBytes }, cancellationToken);
            return BackupRunResult.Succeeded("Google Drive backup write/read/checksum verification succeeded.", [reference]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Drive backup access verification failed.");
            return BackupRunResult.Failed(SafeError(ex));
        }
    }

    private async Task<BackupPackage> BuildPackageAsync(BackupRunRecord run, BackupOptions effective, CancellationToken cancellationToken)
    {
        if (!effective.DatabaseBackupEnabled) throw new InvalidOperationException("Database backup is mandatory for a full production backup.");
        var timestamp = run.StartedAt;
        var components = new List<BackupComponent>();
        var database = await CreateDatabaseDumpAsync(cancellationToken);
        components.Add(new BackupComponent(BackupFileNames.Database(timestamp), database));
        components.Add(JsonComponent(BackupFileNames.Config(timestamp), BuildSafeConfigurationSnapshot(effective)));
        components.Add(JsonComponent(BackupFileNames.Schema(timestamp), await BuildSchemaManifestAsync(cancellationToken)));
        components.Add(JsonComponent(BackupFileNames.PhotoManifest(timestamp), await BuildPhotoManifestAsync(cancellationToken)));

        var componentManifest = components.Select(x => new { name = x.Name, sizeBytes = x.Bytes.LongLength, sha256 = Hash(x.Bytes) }).ToList();
        components.Add(JsonComponent("backup-manifest.json", new
        {
            backupRunId = run.Id,
            backupType = run.BackupType,
            startedAt = run.StartedAt,
            completedPackagingAt = businessTime.UtcNow,
            status = "PackagedPendingUploadVerification",
            environment = run.EnvironmentName,
            databaseProvider = run.DatabaseProvider,
            deployedCommit = run.DeployedCommit,
            retentionCategory = run.RetentionCategory,
            components = componentManifest
        }));

        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var component in components)
            {
                var entry = archive.CreateEntry(component.Name, CompressionLevel.SmallestSize);
                entry.LastWriteTime = timestamp;
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(component.Bytes, cancellationToken);
            }
        }
        var bytes = output.ToArray();
        VerifyPackage(bytes);
        return new BackupPackage(BackupFileNames.Package(run.BackupType, timestamp), new MemoryStream(bytes), bytes.LongLength, Hash(bytes));
    }

    private async Task<byte[]> CreateDatabaseDumpAsync(CancellationToken cancellationToken)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Database connection is not configured for backup.");
        using var process = StartPgDump(connectionString) ?? throw new InvalidOperationException("pg_dump is not installed in the backup runtime.");
        await using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var copy = process.StandardOutput.BaseStream.CopyToAsync(gzip, cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await copy;
            _ = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("pg_dump failed. Review restricted server logs for the provider error.");
        }
        var bytes = compressed.ToArray();
        ValidateDatabaseDump(bytes);
        return bytes;
    }

    private static Process? StartPgDump(string connectionString)
    {
        var connection = ParsePostgreSqlConnection(connectionString);
        var info = new ProcessStartInfo("pg_dump")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--no-owner");
        info.ArgumentList.Add("--no-privileges");
        info.ArgumentList.Add("--serializable-deferrable");
        info.ArgumentList.Add("--format=plain");
        info.ArgumentList.Add($"--host={connection.Host}");
        info.ArgumentList.Add($"--port={connection.Port}");
        info.ArgumentList.Add($"--username={connection.Username}");
        info.ArgumentList.Add($"--dbname={connection.Database}");
        info.Environment["PGPASSWORD"] = connection.Password;
        if (!string.IsNullOrWhiteSpace(connection.SslMode)) info.Environment["PGSSLMODE"] = connection.SslMode;
        try { return Process.Start(info); } catch { return null; }
    }

    private static PgDumpConnection ParsePostgreSqlConnection(string connectionString)
    {
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
        {
            var credentials = uri.UserInfo.Split(':', 2);
            if (credentials.Length != 2) throw new InvalidOperationException("PostgreSQL backup credentials are incomplete.");
            var sslMode = ParseQueryValue(uri.Query, "sslmode");
            return new PgDumpConnection(
                uri.Host,
                uri.IsDefaultPort ? 5432 : uri.Port,
                Uri.UnescapeDataString(credentials[0]),
                Uri.UnescapeDataString(credentials[1]),
                Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                NormalizePgSslMode(sslMode));
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException("PostgreSQL backup connection details are incomplete.");
        }
        return new PgDumpConnection(
            builder.Host,
            builder.Port,
            builder.Username,
            builder.Password ?? "",
            builder.Database,
            NormalizePgSslMode(builder.SslMode.ToString()));
    }

    private static string? ParseQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static string? NormalizePgSslMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "prefer" => null,
        "verifyca" or "verify-ca" => "verify-ca",
        "verifyfull" or "verify-full" => "verify-full",
        var mode => mode
    };

    private object BuildSafeConfigurationSnapshot(BackupOptions effective) => new
    {
        createdAt = businessTime.UtcNow,
        environment = new { appEnvironment.Kind, appEnvironment.DisplayName },
        application = new { deployedCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"], framework = Environment.Version.ToString() },
        database = new { provider = dbContext.Database.ProviderName ?? configuration["DATABASE_PROVIDER"], connectionConfigured = !string.IsNullOrWhiteSpace(dbContext.Database.GetConnectionString()) },
        storage = new
        {
            provider = configuration["FileStorage:Provider"],
            googleDriveSharedDriveConfigured = !string.IsNullOrWhiteSpace(configuration["GoogleDrive:SharedDriveId"]),
            googleDriveRootConfigured = !string.IsNullOrWhiteSpace(configuration["GoogleDrive:RootFolderId"]),
            googleDriveBaseFolderName = configuration["GoogleDrive:BaseFolderName"]
        },
        email = new { provider = configuration["Email:Provider"], qcDefaultRecipientsConfigured = !string.IsNullOrWhiteSpace(configuration["Email:QcDefaultRecipients"]) },
        backups = new { effective.Provider, effective.DailyRetentionDays, effective.WeeklyRetentionWeeks, effective.BusinessTimeZone, effective.NightlyPacificHour }
    };

    private async Task<object> BuildSchemaManifestAsync(CancellationToken cancellationToken) => new
    {
        createdAt = businessTime.UtcNow,
        provider = dbContext.Database.ProviderName,
        canConnect = await dbContext.Database.CanConnectAsync(cancellationToken),
        appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken),
        pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken),
        rowCounts = new
        {
            receipts = await dbContext.Receipts.CountAsync(cancellationToken),
            samples = await dbContext.QcSamples.CountAsync(cancellationToken),
            fruitReadings = await dbContext.QcFruitReadings.CountAsync(cancellationToken),
            photos = await dbContext.QcPhotos.CountAsync(cancellationToken),
            auditLogs = await dbContext.AuditLogs.CountAsync(cancellationToken)
        }
    };

    private async Task<IReadOnlyList<object>> BuildPhotoManifestAsync(CancellationToken cancellationToken)
    {
        var photos = await dbContext.QcPhotos.AsNoTracking().OrderBy(x => x.Id).Select(x => new
        {
            x.Id,
            x.QcSampleId,
            x.ReceiptId,
            x.PhotoType,
            x.StorageProvider,
            x.DriveId,
            x.FileId,
            x.FolderId,
            x.FileName,
            x.ContentType,
            x.FileSizeBytes,
            x.CapturedAt,
            x.UploadedAt
        }).ToListAsync(cancellationToken);
        var photoStorage = new GoogleDriveStorageService(googleDriveOptions);
        var result = new List<object>(photos.Count);
        foreach (var photo in photos)
        {
            FileStorageReference? remote = null;
            if (string.Equals(photo.StorageProvider, FileStorageProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(photo.FileId))
            {
                remote = await photoStorage.GetMetadataAsync(photo.FileId, cancellationToken);
            }
            result.Add(new
            {
                photoId = photo.Id,
                photo.QcSampleId,
                photo.ReceiptId,
                photo.PhotoType,
                photo.StorageProvider,
                photo.DriveId,
                photo.FileId,
                photo.FolderId,
                photo.FileName,
                photo.ContentType,
                photo.FileSizeBytes,
                photo.CapturedAt,
                photo.UploadedAt,
                objectAccessible = remote is not null,
                remoteSizeBytes = remote?.FileSizeBytes,
                remoteChecksum = remote?.Checksum,
                remoteCreatedAt = remote?.CreatedAt,
                remoteModifiedAt = remote?.ModifiedAt
            });
        }
        return result;
    }

    private async Task VerifyUploadedPackageAsync(IFileStorageService storage, FileStorageReference reference, BackupPackage package, CancellationToken cancellationToken)
    {
        var stream = await storage.OpenReadAsync(reference.StorageKey, cancellationToken) ?? throw new InvalidOperationException("Uploaded backup could not be read back.");
        await using (stream)
        await using (var copy = new MemoryStream())
        {
            await stream.CopyToAsync(copy, cancellationToken);
            var bytes = copy.ToArray();
            if (bytes.LongLength != package.Size || !string.Equals(Hash(bytes), package.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException("Uploaded backup size or checksum verification failed.");
            VerifyPackage(bytes);
        }
    }

    private static async Task VerifyUploadedBytesAsync(IFileStorageService storage, FileStorageReference reference, byte[] expected, CancellationToken cancellationToken)
    {
        var stream = await storage.OpenReadAsync(reference.StorageKey, cancellationToken) ?? throw new InvalidOperationException("Uploaded backup artifact could not be read back.");
        await using (stream)
        await using (var copy = new MemoryStream())
        {
            await stream.CopyToAsync(copy, cancellationToken);
            var actual = copy.ToArray();
            if (actual.LongLength != expected.LongLength || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual), SHA256.HashData(expected)))
                throw new InvalidOperationException("Uploaded artifact checksum verification failed.");
        }
    }

    public static void VerifyPackage(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var required = new[] { "backup-manifest.json" };
        if (required.Any(name => archive.GetEntry(name) is null) || !archive.Entries.Any(x => x.Name.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Backup package is missing its manifest or database dump.");
        var manifestEntry = archive.GetEntry("backup-manifest.json")!;
        using var manifest = JsonDocument.Parse(manifestEntry.Open());
        if (!manifest.RootElement.TryGetProperty("components", out var components) || components.GetArrayLength() < 4)
            throw new InvalidDataException("Backup manifest is incomplete.");
        foreach (var component in components.EnumerateArray())
        {
            var name = component.GetProperty("name").GetString() ?? "";
            var expectedSize = component.GetProperty("sizeBytes").GetInt64();
            var expectedHash = component.GetProperty("sha256").GetString();
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Backup component {name} is missing.");
            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.LongLength != expectedSize || !string.Equals(Hash(bytes), expectedHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Backup component {name} failed checksum verification.");
        }
        var dbEntry = archive.Entries.Single(x => x.Name.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase));
        using var dbStream = dbEntry.Open();
        using var gzip = new GZipStream(dbStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var prefix = new char[256];
        var read = reader.Read(prefix, 0, prefix.Length);
        if (read == 0 || !new string(prefix, 0, read).Contains("PostgreSQL database dump", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Database dump header validation failed.");
    }

    private static void ValidateDatabaseDump(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var header = new char[256];
        var read = reader.Read(header, 0, header.Length);
        if (read == 0 || !new string(header, 0, read).Contains("PostgreSQL database dump", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("pg_dump output was empty or unreadable.");
    }

    private async Task ApplyRetentionAsync(IFileStorageService storage, BackupOptions effective, DateTimeOffset now, long currentRunId, CancellationToken cancellationToken)
    {
        var successful = await dbContext.BackupRunRecords.Where(x => x.Status == BackupRunStatuses.Succeeded && x.VerifiedAt != null && x.PrunedAt == null).OrderByDescending(x => x.StartedAt).ToListAsync(cancellationToken);
        foreach (var run in BackupRetentionPolicy.SelectForPruning(successful, now, effective.DailyRetentionDays, effective.WeeklyRetentionWeeks, currentRunId))
        {
            if (!string.IsNullOrWhiteSpace(run.PackageStorageKey)) await storage.DeleteOrVoidAsync(run.PackageStorageKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(run.ManifestStorageKey)) await storage.DeleteOrVoidAsync(run.ManifestStorageKey, cancellationToken);
            run.PrunedAt = businessTime.UtcNow;
            await AddAuditAsync("BackupPruned", run.Id.ToString(CultureInfo.InvariantCulture), new { run.PackageFileName, run.RetentionCategory }, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryAcquireLeaseAsync(Guid leaseId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var updated = await dbContext.BackupOperationLeases.Where(x => x.Id == 1 && (x.ExpiresAt == null || x.ExpiresAt <= now)).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.LeaseId, leaseId).SetProperty(x => x.ExpiresAt, now.Add(LeaseDuration)), cancellationToken);
        return updated == 1;
    }

    private Task ReleaseLeaseAsync(Guid leaseId, CancellationToken cancellationToken) =>
        dbContext.BackupOperationLeases.Where(x => x.Id == 1 && x.LeaseId == leaseId).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.ExpiresAt, (DateTimeOffset?)null), cancellationToken);

    private IFileStorageService CreateBackupStorage(BackupOptions effective) => new GoogleDriveStorageService(new GoogleDriveStorageOptions
    {
        UseSharedDrive = googleDriveOptions.UseSharedDrive,
        RootFolderId = effective.GoogleDriveFolderId ?? "",
        SharedDriveId = googleDriveOptions.SharedDriveId,
        ServiceAccountJson = googleDriveOptions.ServiceAccountJson,
        ServiceAccountJsonPath = googleDriveOptions.ServiceAccountJsonPath,
        ApplicationName = googleDriveOptions.ApplicationName,
        BaseFolderName = "Crop QC Backups"
    });

    private IReadOnlyList<string> BuildSafetyWarnings(BackupOptions effective)
    {
        var warnings = new List<string>();
        if (appEnvironment.IsProduction && !effective.GoogleDriveFolderConfigured) warnings.Add("Production backup folder is not configured. Production changes must stop until a verified backup is possible.");
        if (appEnvironment.IsProduction && configuration.GetValue("Database:EnsureCreatedOnStartup", false)) warnings.Add("Production has Database__EnsureCreatedOnStartup enabled. Disable it and use reviewed migrations.");
        if (appEnvironment.IsProduction && configuration.GetValue("Database:SeedMasterDataOnStartup", false)) warnings.Add("Production has master-data seeding enabled.");
        return warnings;
    }

    private async Task<BackupOptions> GetEffectiveOptionsAsync(CancellationToken cancellationToken)
    {
        var keys = new[] { "Backups:Enabled", "Backups:Provider", "Backups:GoogleDriveFolderId", "Backups:DailyRetentionDays", "Backups:WeeklyRetentionWeeks", "Backups:BusinessTimeZone", "Backups:NightlyPacificHour", "Backups:NotificationRecipient", "Backups:NotificationSender", "Backups:DatabaseBackupEnabled", "Backups:ConfigBackupEnabled", "Backups:PhotoManifestEnabled" };
        var overrides = await dbContext.DashboardConfigurations.AsNoTracking().Where(x => keys.Contains(x.Key) && x.Value != "").ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        return options.WithOverrides(overrides);
    }

    private async Task<string?> GetConfigValueAsync(string key, CancellationToken cancellationToken) =>
        (await dbContext.DashboardConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, cancellationToken))?.Value;

    private async Task<DateTimeOffset?> GetConfigDateAsync(string key, CancellationToken cancellationToken) =>
        DateTimeOffset.TryParse(await GetConfigValueAsync(key, cancellationToken), out var value) ? value : null;

    private async Task SetConfigValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var item = await dbContext.DashboardConfigurations.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (item is null) dbContext.DashboardConfigurations.Add(new DashboardConfiguration { Key = key, Value = value, Description = "Backup setting or verified status.", ValueType = "String", CreatedAt = businessTime.UtcNow, UpdatedAt = businessTime.UtcNow });
        else { item.Value = value; item.UpdatedAt = businessTime.UtcNow; }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AddAuditAsync(string action, string key, object after, CancellationToken cancellationToken)
    {
        var audit = new AuditLog { Action = action, EntityName = "Backup", EntityKey = key, AfterValuesJson = JsonSerializer.Serialize(after, JsonOptions), SourceApplication = "CropQc.Web", CreatedAt = businessTime.UtcNow };
        dbContext.AuditLogs.Add(audit);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            dbContext.Entry(audit).State = EntityState.Detached;
            throw;
        }
    }

    private static BackupRunListItem ToListItem(BackupRunRecord x) => new(x.Id, x.BackupType, x.Status, x.StartedAt, x.CompletedAt, x.DurationMilliseconds, x.DatabaseProvider, x.DeployedCommit, x.RetentionCategory, x.PackageFileName, x.FileSizeBytes, x.Sha256, x.VerifiedAt, x.ErrorSummary, x.PackageWebUrl);
    private static BackupComponent JsonComponent(string name, object value) => new(name, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string MaskFolderId(string? folderId) => string.IsNullOrWhiteSpace(folderId) ? "Missing" : folderId.Length <= 8 ? "Configured" : $"{folderId[..4]}...{folderId[^4..]}";
    private static string SafeError(Exception ex) => ex is InvalidDataException ? "Backup verification failed; the uploaded artifact was not accepted." : ex.Message.Contains("Google Drive", StringComparison.OrdinalIgnoreCase) ? "Google Drive backup upload or verification failed." : ex.Message.Contains("pg_dump", StringComparison.OrdinalIgnoreCase) ? "PostgreSQL dump creation failed." : "Backup failed. Review restricted server logs for details.";
    private static string BackupFolder(string type) => type switch { BackupRunTypes.Weekly => "Weekly", BackupRunTypes.PreDeployment => "PreDeployment", BackupRunTypes.Manual => "Manual", _ => "Daily" };
    private static string NormalizeBackupType(string type, DateTimeOffset now, string timeZone) => type.Equals(BackupRunTypes.Daily, StringComparison.OrdinalIgnoreCase) && LocalDate(now, timeZone).DayOfWeek == DayOfWeek.Sunday ? BackupRunTypes.Weekly : type switch { BackupRunTypes.Manual => BackupRunTypes.Manual, BackupRunTypes.PreDeployment => BackupRunTypes.PreDeployment, BackupRunTypes.Weekly => BackupRunTypes.Weekly, _ => BackupRunTypes.Daily };
    private static DateTime LocalDate(DateTimeOffset value, string timeZone)
    {
        try { return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime; }
        catch { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(value, "Pacific Standard Time").DateTime; }
    }
    private sealed record PgDumpConnection(string Host, int Port, string Username, string Password, string Database, string? SslMode);
    private sealed record BackupComponent(string Name, byte[] Bytes);
    private sealed record BackupPackage(string FileName, MemoryStream Content, long Size, string Sha256);
}

public static class BackupRetentionPolicy
{
    public static IReadOnlyList<BackupRunRecord> SelectForPruning(IReadOnlyList<BackupRunRecord> runs, DateTimeOffset now, int dailyDays, int weeklyWeeks, long newestRunId)
    {
        var newestVerifiedId = runs.OrderByDescending(x => x.StartedAt).Select(x => x.Id).FirstOrDefault();
        var dailyCutoff = now.UtcDateTime.Date.AddDays(-dailyDays);
        var weeklyCutoff = now.UtcDateTime.Date.AddDays(-(weeklyWeeks * 7));
        var seenWeeks = new HashSet<string>(StringComparer.Ordinal);
        var prune = new List<BackupRunRecord>();
        foreach (var run in runs.OrderByDescending(x => x.StartedAt))
        {
            var week = $"{ISOWeek.GetYear(run.StartedAt.UtcDateTime):0000}-{ISOWeek.GetWeekOfYear(run.StartedAt.UtcDateTime):00}";
            if (run.Id == newestRunId || run.Id == newestVerifiedId)
            {
                if (run.RetentionCategory == BackupRunTypes.Weekly) seenWeeks.Add(week);
                continue;
            }
            if (run.RetentionCategory == BackupRunTypes.Weekly)
            {
                if (!seenWeeks.Add(week) || run.StartedAt.UtcDateTime.Date < weeklyCutoff) prune.Add(run);
            }
            else if (run.StartedAt.UtcDateTime.Date < dailyCutoff) prune.Add(run);
        }
        return prune;
    }
}

public static class BackupFileNames
{
    public static string Package(string type, DateTimeOffset timestamp) => $"cropqc-production-{type.ToLowerInvariant()}-{Format(timestamp)}.zip";
    public static string Database(DateTimeOffset timestamp) => $"cropqc-prod-db-{Format(timestamp)}.sql.gz";
    public static string Config(DateTimeOffset timestamp) => $"cropqc-prod-config-{Format(timestamp)}.json";
    public static string Schema(DateTimeOffset timestamp) => $"cropqc-prod-schema-{Format(timestamp)}.json";
    public static string PhotoManifest(DateTimeOffset timestamp) => $"cropqc-prod-photo-manifest-{Format(timestamp)}.json";
    public static string AccessTest(DateTimeOffset timestamp) => $"cropqc-backup-access-test-{Format(timestamp)}.txt";
    private static string Format(DateTimeOffset timestamp) => timestamp.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}

public static class BackupStatusKeys
{
    public const string LastDatabaseBackupAt = "BackupLastDatabaseBackupAt";
    public const string LastDatabaseBackupFileName = "BackupLastDatabaseBackupFileName";
    public const string LastError = "BackupLastError";
}

public sealed record BackupRunResult(bool Success, string Message, IReadOnlyList<FileStorageReference> UploadedFiles, long? RunId, bool WasSkipped)
{
    public static BackupRunResult Succeeded(string message, IReadOnlyList<FileStorageReference> uploadedFiles, long? runId = null) => new(true, message, uploadedFiles, runId, false);
    public static BackupRunResult Failed(string message, long? runId = null) => new(false, message, [], runId, false);
    public static BackupRunResult Skipped(string message) => new(true, message, [], null, true);
}
