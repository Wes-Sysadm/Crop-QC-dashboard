using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IFieldSampleDeletionService
{
    Task<FieldSampleDeletionConfirmationViewModel?> GetConfirmationAsync(long sampleId, CancellationToken cancellationToken);
    Task<string?> DeleteAsync(DeleteFieldSampleForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class FieldSampleDeletionService(
    CropQcDbContext dbContext,
    IUserAccessService userAccessService,
    IBusinessTimeService businessTime) : IFieldSampleDeletionService
{
    private const string FieldSampleTypeName = "Field Sample";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<FieldSampleDeletionConfirmationViewModel?> GetConfirmationAsync(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.Id == sampleId && !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName)
            .Select(x => new
            {
                x.Id,
                OrchardName = x.CanonicalOrchardBlock == null
                    ? x.FieldSampleGrowerName
                    : x.CanonicalOrchardBlock.CanonicalOrchard.OrchardName,
                x.FieldSampleGrowerName,
                x.FieldSampleGrowerNumber,
                BlockName = x.CanonicalOrchardBlock == null ? x.FieldSampleOriginalBlockName : x.CanonicalOrchardBlock.CanonicalBlockName,
                Variety = x.FieldSampleFruitProfile == null ? "" : x.FieldSampleFruitProfile.Name,
                x.SampleTakenAt,
                x.Status,
                x.EmailStatus
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (sample is null)
        {
            return null;
        }

        var dependencies = await CountDependenciesAsync(sampleId, cancellationToken);
        var backup = await LatestVerifiedBackupAsync(cancellationToken);
        var backupIsCurrent = backup?.VerifiedAt >= businessTime.UtcNow.AddHours(-24);
        return new FieldSampleDeletionConfirmationViewModel
        {
            Id = sample.Id,
            OrchardName = sample.OrchardName ?? "",
            GrowerName = sample.FieldSampleGrowerName ?? "",
            GrowerNumber = sample.FieldSampleGrowerNumber ?? "",
            BlockName = sample.BlockName ?? "",
            Variety = sample.Variety,
            SampleTakenAt = sample.SampleTakenAt,
            LifecycleStatus = sample.Status,
            EmailStatus = sample.EmailStatus,
            HasBeenSent = string.Equals(sample.EmailStatus, "Sent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sample.EmailStatus, "Needs Resend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sample.Status, "Sent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sample.Status, "Changed Since Last Send", StringComparison.OrdinalIgnoreCase),
            ChangedSinceLastSend = string.Equals(sample.EmailStatus, "Needs Resend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sample.Status, "Changed Since Last Send", StringComparison.OrdinalIgnoreCase),
            Dependencies = dependencies,
            VerifiedBackupRunId = backupIsCurrent ? backup!.Id : null,
            VerifiedBackupFileName = backupIsCurrent ? backup!.PackageFileName : null,
            VerifiedBackupAt = backupIsCurrent ? backup!.VerifiedAt : null,
            BackupWarning = backup is null
                ? "No fully verified production backup is recorded."
                : !backupIsCurrent
                    ? "The latest fully verified backup is more than 24 hours old. Run and verify a new backup before deleting."
                    : null,
            Form = new DeleteFieldSampleForm
            {
                Id = sample.Id,
                OperationToken = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                VerifiedBackupRunId = backupIsCurrent ? backup!.Id : 0
            }
        };
    }

    public async Task<string?> DeleteAsync(DeleteFieldSampleForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Admin, cancellationToken))
        {
            return "Field Samples Admin access is required.";
        }
        if (!Guid.TryParse(form.OperationToken, out var operationId))
        {
            return "The deletion confirmation expired. Reopen the Field Sample deletion page.";
        }
        if (string.IsNullOrWhiteSpace(form.Reason) || form.Reason.Trim().Length < 10)
        {
            return "A detailed deletion reason of at least 10 characters is required.";
        }
        if (!form.ConfirmDeletion)
        {
            return "Select the second confirmation before deleting the Field Sample.";
        }
        if (form.ConfirmationValue.Trim() != form.Id.ToString(CultureInfo.InvariantCulture))
        {
            return $"Type the exact Field Sample ID {form.Id} to confirm deletion.";
        }
        if (await dbContext.FieldSampleDeletionAudits.AsNoTracking().AnyAsync(x => x.OperationId == operationId, cancellationToken))
        {
            return "This deletion request was already processed.";
        }

        var backup = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == form.VerifiedBackupRunId, cancellationToken);
        if (!IsUsableBackup(backup))
        {
            return "The verified backup gate is no longer satisfied. Run and verify a current backup, then reopen this page.";
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
            .Include(x => x.FieldSampleFruitProfile)
            .SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName, cancellationToken);
        if (sample is null)
        {
            return "Field Sample not found or already deleted.";
        }

        var email = user.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return "The signed-in administrator email could not be resolved.";
        }
        var userId = await dbContext.Users.AsNoTracking()
            .Where(x => x.Email == email)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var dependencies = await CountDependenciesAsync(sample.Id, cancellationToken);
        var now = businessTime.UtcNow;
        var reason = form.Reason.Trim();

        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }
        await using (transaction)
        {
            dbContext.FieldSampleDeletionAudits.Add(new FieldSampleDeletionAudit
            {
                Id = Guid.NewGuid(),
                OperationId = operationId,
                DeletedFieldSampleId = sample.Id,
                IdentifyingFieldsJson = JsonSerializer.Serialize(new
                {
                    sample.Id,
                    Orchard = sample.CanonicalOrchardBlock?.CanonicalOrchard.OrchardName ?? sample.FieldSampleGrowerName,
                    sample.FieldSampleGrowerName,
                    sample.FieldSampleGrowerNumber,
                    Block = sample.CanonicalOrchardBlock?.CanonicalBlockName ?? sample.FieldSampleOriginalBlockName,
                    Variety = sample.FieldSampleFruitProfile?.Name,
                    sample.SampleTakenAt,
                    LifecycleStatus = sample.Status,
                    sample.EmailStatus,
                    sample.QcStationId,
                    sample.FieldSampleAutosaveVersion
                }, JsonOptions),
                DependencyCountsJson = JsonSerializer.Serialize(dependencies, JsonOptions),
                DeletedByEmail = email,
                DeletedAt = now,
                DeletedAtPacific = businessTime.FormatPacific(now, "O"),
                Reason = reason,
                BackupRunId = backup!.Id,
                Result = "SoftDeleted"
            });

            sample.IsDeleted = true;
            sample.DeletedAt = now;
            sample.DeletedByUserId = userId;
            sample.DeleteReason = reason;
            sample.UpdatedAt = now;

            var photos = await dbContext.QcPhotos
                .Where(x => x.QcSampleId == sample.Id && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            foreach (var photo in photos)
            {
                photo.IsDeleted = true;
                photo.DeletedAt = now;
                photo.DeletedByUserId = userId;
                photo.DeleteReason = $"Field Sample {sample.Id} deleted: {reason}";
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "Delete",
                EntityName = "FieldSample",
                EntityKey = sample.Id.ToString(CultureInfo.InvariantCulture),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    sample.Id,
                    sample.DeletedAt,
                    sample.DeleteReason,
                    operationId,
                    BackupRunId = backup.Id,
                    Dependencies = dependencies,
                    PhotoBinariesRetained = true
                }, JsonOptions),
                SourceApplication = "CropQc.Web",
                CreatedAt = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        return null;
    }

    private async Task<FieldSampleDeletionDependencyCounts> CountDependenciesAsync(long sampleId, CancellationToken cancellationToken)
    {
        var rowIds = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sampleId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var key = sampleId.ToString(CultureInfo.InvariantCulture);
        var rowKeys = rowIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        return new FieldSampleDeletionDependencyCounts
        {
            FruitRows = rowIds.Count,
            Defects = await dbContext.QcFruitDefects.AsNoTracking().CountAsync(x => rowIds.Contains(x.QcFruitReadingId), cancellationToken),
            Photos = await dbContext.QcPhotos.AsNoTracking().CountAsync(x => x.QcSampleId == sampleId, cancellationToken),
            EmailLogs = await dbContext.QcSummaryEmailLogs.AsNoTracking().CountAsync(x => x.QcSampleId == sampleId, cancellationToken),
            AuditRecords = await dbContext.AuditLogs.AsNoTracking().CountAsync(x =>
                (x.EntityName == nameof(QcSample) || x.EntityName == "FieldSample" || x.EntityName == "field-sample-report")
                    && x.EntityKey == key
                || x.EntityName == nameof(QcFruitReading) && rowKeys.Contains(x.EntityKey), cancellationToken),
            OfflineSyncItems = await dbContext.OfflineSyncItems.AsNoTracking().CountAsync(x =>
                x.ServerEntityId == sampleId
                && (x.EntityName == nameof(QcSample) || x.EntityName == "FieldSample"), cancellationToken),
            HasQcStationReference = await dbContext.QcSamples.AsNoTracking().AnyAsync(x => x.Id == sampleId && x.QcStationId != null, cancellationToken)
        };
    }

    private Task<BackupRunRecord?> LatestVerifiedBackupAsync(CancellationToken cancellationToken) =>
        dbContext.BackupRunRecords.AsNoTracking()
            .Where(x => x.Status == BackupRunStatuses.Succeeded
                && x.VerifiedAt != null
                && x.LeaseReleasedAt != null
                && x.RetentionProcessedAt != null
                && x.PrunedAt == null
                && x.PackageFileName != null
                && x.PackageStorageKey != null
                && x.FileSizeBytes > 0
                && x.Sha256 != null)
            .OrderByDescending(x => x.VerifiedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private bool IsUsableBackup(BackupRunRecord? backup) =>
        backup is not null
        && backup.Status == BackupRunStatuses.Succeeded
        && backup.VerifiedAt >= businessTime.UtcNow.AddHours(-24)
        && backup.LeaseReleasedAt is not null
        && backup.RetentionProcessedAt is not null
        && backup.PrunedAt is null
        && !string.IsNullOrWhiteSpace(backup.PackageFileName)
        && !string.IsNullOrWhiteSpace(backup.PackageStorageKey)
        && backup.FileSizeBytes > 0
        && !string.IsNullOrWhiteSpace(backup.Sha256);
}
