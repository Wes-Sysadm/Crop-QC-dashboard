using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IBackupNotificationService
{
    Task QueueAsync(long backupRunId, string notificationType, CancellationToken cancellationToken);
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken);
    Task<string?> RetryAsync(long notificationId, string requestedBy, CancellationToken cancellationToken);
}

public sealed class BackupNotificationService(
    CropQcDbContext dbContext,
    BackupOptions options,
    IQcEmailSender emailSender,
    IBusinessTimeService businessTime,
    ILogger<BackupNotificationService> logger) : IBackupNotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task QueueAsync(long backupRunId, string notificationType, CancellationToken cancellationToken)
    {
        var run = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == backupRunId, cancellationToken);
        var validOutcome = notificationType switch
        {
            BackupNotificationTypes.Success => run is
            {
                Status: BackupRunStatuses.Succeeded,
                VerifiedAt: not null,
                RetentionProcessedAt: not null,
                LeaseReleasedAt: not null
            },
            BackupNotificationTypes.Failure => run?.Status == BackupRunStatuses.Failed,
            _ => false
        };
        if (!validOutcome)
        {
            logger.LogWarning(
                "Backup notification {NotificationType} for run {BackupRunId} was suppressed because the persisted backup outcome is not eligible.",
                notificationType,
                backupRunId);
            await AddAuditAsync(
                "BackupNotificationSuppressed",
                backupRunId.ToString(CultureInfo.InvariantCulture),
                new { notificationType, reason = "Persisted backup outcome is not eligible." },
                cancellationToken);
            return;
        }

        var recipient = await GetSettingAsync("Backups:NotificationRecipient", options.NotificationRecipient, cancellationToken);
        var parsed = QcEmailRecipientParser.Parse(recipient);
        if (parsed.Recipients.Count != 1 || parsed.InvalidRecipients.Count > 0)
        {
            logger.LogError("Backup notification for run {BackupRunId} could not be queued because the configured recipient is invalid.", backupRunId);
            await AddAuditAsync("BackupNotificationConfigurationInvalid", backupRunId.ToString(CultureInfo.InvariantCulture), new { notificationType }, cancellationToken);
            return;
        }

        var alreadyExists = await dbContext.BackupNotificationRecords
            .AnyAsync(x => x.BackupRunId == backupRunId && x.NotificationType == notificationType, cancellationToken);
        if (alreadyExists)
        {
            return;
        }

        dbContext.BackupNotificationRecords.Add(new BackupNotificationRecord
        {
            BackupRunId = backupRunId,
            NotificationType = notificationType,
            Recipient = parsed.Recipients[0],
            Status = BackupNotificationStatuses.Pending,
            CreatedAt = businessTime.UtcNow,
            NextAttemptAt = businessTime.UtcNow
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddAuditAsync("BackupNotificationQueued", backupRunId.ToString(CultureInfo.InvariantCulture), new { notificationType, recipient = parsed.Recipients[0] }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<BackupNotificationRecord>().Where(x => x.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }

            if (!await dbContext.BackupNotificationRecords.AsNoTracking()
                .AnyAsync(x => x.BackupRunId == backupRunId && x.NotificationType == notificationType, cancellationToken))
            {
                throw;
            }
        }
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        var candidates = await dbContext.BackupNotificationRecords.AsNoTracking()
            .Where(x => x.Status == BackupNotificationStatuses.Pending || x.Status == BackupNotificationStatuses.Failed)
            .Select(x => new { x.Id, x.CreatedAt, x.NextAttemptAt })
            .ToListAsync(cancellationToken);
        var ids = candidates
            .Where(x => x.NextAttemptAt is null || x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(10)
            .ToList();
        var sent = 0;
        foreach (var id in ids)
        {
            if (await TryDispatchAsync(id, cancellationToken))
            {
                sent++;
            }
        }

        return sent;
    }

    public async Task<string?> RetryAsync(long notificationId, string requestedBy, CancellationToken cancellationToken)
    {
        var notification = await dbContext.BackupNotificationRecords.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return "Backup notification was not found.";
        }

        if (notification.Status == BackupNotificationStatuses.Sent)
        {
            return "The backup notification was already sent.";
        }

        notification.Status = BackupNotificationStatuses.Pending;
        notification.NextAttemptAt = businessTime.UtcNow;
        notification.ErrorSummary = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync("BackupNotificationRetryRequested", notification.BackupRunId.ToString(CultureInfo.InvariantCulture), new { notification.Id, requestedBy }, cancellationToken);
        return null;
    }

    private async Task<bool> TryDispatchAsync(long notificationId, CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        var claimed = await dbContext.BackupNotificationRecords
            .Where(x => x.Id == notificationId
                && (x.Status == BackupNotificationStatuses.Pending || x.Status == BackupNotificationStatuses.Failed))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, BackupNotificationStatuses.Sending)
                .SetProperty(x => x.LastAttemptedAt, now)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken);
        if (claimed != 1)
        {
            return false;
        }

        dbContext.ChangeTracker.Clear();
        var notification = await dbContext.BackupNotificationRecords
            .Include(x => x.BackupRun)
            .SingleAsync(x => x.Id == notificationId, cancellationToken);
        try
        {
            var senderEmail = await GetSettingAsync("Backups:NotificationSender", options.NotificationSender, cancellationToken);
            var sender = await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Email == senderEmail && x.IsActive, cancellationToken);
            if (sender is null)
            {
                throw new InvalidOperationException("The configured backup notification sender is not an active dashboard user.");
            }

            var message = await BuildMessageAsync(notification, sender.Email, cancellationToken);
            var result = await emailSender.SendAsync(sender, message, cancellationToken);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error ?? "Gmail did not accept the backup notification.");
            }

            notification.Status = BackupNotificationStatuses.Sent;
            notification.SentAt = businessTime.UtcNow;
            notification.MessageId = result.MessageId;
            notification.NextAttemptAt = null;
            notification.ErrorSummary = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddAuditAsync("BackupNotificationSent", notification.BackupRunId.ToString(CultureInfo.InvariantCulture), new { notification.Id, notification.NotificationType, notification.Recipient, result.MessageId }, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Backup notification {BackupNotificationId} for run {BackupRunId} failed.", notification.Id, notification.BackupRunId);
            notification.Status = BackupNotificationStatuses.Failed;
            notification.ErrorSummary = SafeError(exception);
            notification.NextAttemptAt = businessTime.UtcNow.AddMinutes(Math.Min(60, Math.Max(5, notification.AttemptCount * 5)));
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddAuditAsync("BackupNotificationFailed", notification.BackupRunId.ToString(CultureInfo.InvariantCulture), new { notification.Id, notification.NotificationType, error = notification.ErrorSummary, retryAt = notification.NextAttemptAt }, cancellationToken);
            return false;
        }
    }

    private async Task<QcEmailMessage> BuildMessageAsync(BackupNotificationRecord notification, string senderEmail, CancellationToken cancellationToken)
    {
        var run = notification.BackupRun;
        var retained = await dbContext.BackupRunRecords.AsNoTracking()
            .Where(x => x.Status == BackupRunStatuses.Succeeded && x.VerifiedAt != null && x.PrunedAt == null)
            .GroupBy(x => x.RetentionCategory)
            .Select(x => new { Category = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var dailyCount = retained.Where(x => x.Category != BackupRunTypes.Weekly).Sum(x => x.Count);
        var weeklyCount = retained.Where(x => x.Category == BackupRunTypes.Weekly).Sum(x => x.Count);
        var nextUtc = businessTime.NextNightlyBackupUtc();
        var nextPacific = businessTime.FormatPacific(nextUtc, "dddd, MMMM d, yyyy 'at' h:mm tt");
        var databaseIdentifier = dbContext.Database.GetDbConnection().Database;
        var isSuccess = notification.NotificationType == BackupNotificationTypes.Success;
        var title = isSuccess ? "Crop QC backup completed and verified" : "Crop QC backup failed";
        var subject = $"{(isSuccess ? "SUCCESS" : "FAILURE")}: Crop QC {run.BackupType} backup #{run.Id}";
        var fields = new List<(string Label, string Value)>
        {
            ("Environment", run.EnvironmentName),
            ("Backup type", run.BackupType),
            ("Start time", businessTime.FormatPacific(run.StartedAt, "yyyy-MM-dd h:mm:ss tt")),
            (isSuccess ? "Completion time" : "Failure time", businessTime.FormatPacific(run.CompletedAt, "yyyy-MM-dd h:mm:ss tt")),
            ("Duration", run.DurationMilliseconds is null ? "Unknown" : TimeSpan.FromMilliseconds(run.DurationMilliseconds.Value).ToString("g", CultureInfo.InvariantCulture)),
            ("Deployed commit", run.DeployedCommit ?? "Unknown"),
            ("Database", $"{run.DatabaseProvider} / {databaseIdentifier}"),
            ("Backup run ID", run.Id.ToString(CultureInfo.InvariantCulture))
        };

        if (isSuccess)
        {
            fields.AddRange(
            [
                ("Package", run.PackageFileName ?? "Unknown"),
                ("Size", $"{run.FileSizeBytes ?? 0:N0} bytes"),
                ("SHA-256", run.Sha256 ?? "Unknown"),
                ("Google Drive", run.PackageWebUrl ?? run.PackageStorageKey ?? "Restricted backup folder"),
                ("Retention classification", run.RetentionCategory),
                ("Verification", run.VerifiedAt is null ? "Not verified" : $"Verified {businessTime.FormatPacific(run.VerifiedAt, "yyyy-MM-dd h:mm:ss tt")}"),
                ("Retained recovery points", $"{dailyCount} daily/manual/pre-deployment; {weeklyCount} weekly"),
                ("Next nightly backup", $"{nextPacific} (1:00 AM Pacific)")
            ]);
        }
        else
        {
            var priorExists = await dbContext.BackupRunRecords.AsNoTracking()
                .AnyAsync(x => x.Id != run.Id && x.Status == BackupRunStatuses.Succeeded && x.VerifiedAt != null && x.PrunedAt == null, cancellationToken);
            fields.AddRange(
            [
                ("Failure stage", run.FailureStage ?? "Unknown"),
                ("Safe error summary", run.ErrorSummary ?? "Backup failed. Review restricted server logs."),
                ("Incomplete object created", run.IncompleteObjectCreated ? "Yes" : "No"),
                ("Prior verified backup available", priorExists ? "Yes" : "No"),
                ("Production-changing operations blocked", "Yes")
            ]);
        }

        var text = new StringBuilder(title).AppendLine().AppendLine();
        var html = new StringBuilder("<h1>").Append(HtmlEncoder.Default.Encode(title)).Append("</h1><dl>");
        foreach (var field in fields)
        {
            text.Append(field.Label).Append(": ").AppendLine(field.Value);
            html.Append("<dt><strong>").Append(HtmlEncoder.Default.Encode(field.Label)).Append("</strong></dt><dd>")
                .Append(HtmlEncoder.Default.Encode(field.Value)).Append("</dd>");
        }
        html.Append("</dl>");

        return new QcEmailMessage(senderEmail, notification.Recipient, senderEmail, subject, text.ToString(), html.ToString(), []);
    }

    private async Task<string> GetSettingAsync(string key, string fallback, CancellationToken cancellationToken) =>
        await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key == key && x.Value != "")
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken) ?? fallback;

    private async Task AddAuditAsync(string action, string key, object after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = "BackupNotification",
            EntityKey = key,
            AfterValuesJson = JsonSerializer.Serialize(after, JsonOptions),
            SourceApplication = "CropQc.Web",
            CreatedAt = businessTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string SafeError(Exception exception) =>
        exception.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            ? "Gmail permission or credential is unavailable; reconnect the configured sender."
            : exception.Message.Contains("sender", StringComparison.OrdinalIgnoreCase)
                ? exception.Message
                : "Backup notification could not be sent. Review restricted server logs.";
}

public sealed class BackupNotificationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<BackupNotificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBackupNotificationService>();
                await service.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Backup notification dispatcher failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
