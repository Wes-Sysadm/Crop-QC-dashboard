using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record TreatmentReportUploadResult(int Uploaded, IReadOnlyList<string> Failures);

public interface ITreatmentReportAttachmentService
{
    Task<TreatmentReportUploadResult> UploadAsync(long applicationId, TreatmentReportUploadForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<(Stream? Content, string? ContentType, string? FileName)> OpenReadAsync(long applicationId, long attachmentId, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> RemoveAsync(long applicationId, long attachmentId, string reason, ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class TreatmentReportAttachmentService(
    CropQcDbContext dbContext,
    IFileStorageService fileStorage,
    IUserAccessService access,
    IBusinessTimeService businessTime,
    ILogger<TreatmentReportAttachmentService> logger) : ITreatmentReportAttachmentService
{
    public const long MaxFileSizeBytes = 15 * 1024 * 1024;
    private const string SourceApplication = "CropQc.Web treatment report workflow";

    public async Task<TreatmentReportUploadResult> UploadAsync(
        long applicationId,
        TreatmentReportUploadForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var application = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == applicationId, cancellationToken);
        if (application is null) return new(0, ["Treatment application was not found."]);
        var permissionArea = application.ApplicationLevel == TreatmentApplicationLevels.Receiving
            ? ApplicationAreas.Receipts
            : ApplicationAreas.RoomTransactions;
        var permissionLabel = application.ApplicationLevel == TreatmentApplicationLevels.Receiving ? "Receipts" : "Room Transactions";
        if (!await access.HasAccessAsync(principal, permissionArea, PageAccessLevel.Edit, cancellationToken))
            return new(0, [$"{permissionLabel} Edit access is required to add a treatment report."]);
        if (form.Files.Count == 0) return new(0, []);
        if (string.IsNullOrWhiteSpace(form.OperationKey) || form.OperationKey.Trim().Length > 80)
        {
            return new(0, ["A valid report upload operation key is required."]);
        }

        var actor = await CurrentUserAsync(principal, cancellationToken);
        if (actor is null) return new(0, ["The active user record could not be resolved."]);

        var uploaded = 0;
        var failures = new List<string>();
        for (var index = 0; index < form.Files.Count; index++)
        {
            var file = form.Files[index];
            var operationKey = $"{form.OperationKey.Trim()}:{index}";
            var existing = await dbContext.RoomTreatmentApplicationAttachments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RoomTreatmentApplicationId == applicationId && x.OperationKey == operationKey, cancellationToken);
            if (existing is not null)
            {
                uploaded++;
                continue;
            }

            var validationError = await ValidateAsync(file, cancellationToken);
            if (validationError is not null)
            {
                failures.Add($"{SafeDisplayName(file.FileName)}: {validationError}");
                continue;
            }

            FileStorageReference? reference = null;
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                var fileName = SafeFileName(file.FileName);
                var targetPath = string.Join('/',
                    "Treatment Reports",
                    application.AppliedAt.Year.ToString(),
                    SafePathSegment(application.Warehouse.Code),
                    $"Treatment-{application.Id}");
                await using var content = file.OpenReadStream();
                reference = await fileStorage.SaveAsync(new FileStorageSaveRequest(
                    content, targetPath, fileName, NormalizeContentType(file.ContentType), file.Length), cancellationToken);
                var now = businessTime.UtcNow;
                var attachment = new RoomTreatmentApplicationAttachment
                {
                    RoomTreatmentApplicationId = application.Id,
                    OperationKey = operationKey,
                    FileName = reference.FileName,
                    ContentType = NormalizeContentType(file.ContentType),
                    FileSizeBytes = reference.FileSizeBytes,
                    StorageProvider = reference.StorageProvider,
                    DriveId = reference.DriveId,
                    FileId = reference.FileId ?? reference.StorageKey,
                    FolderId = reference.FolderId,
                    StoragePath = reference.TargetPath,
                    CreatedAt = now,
                    CreatedByUserId = actor.Id
                };
                dbContext.RoomTreatmentApplicationAttachments.Add(attachment);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.AuditLogs.Add(new AuditLog
                {
                    UserId = actor.Id,
                    Action = "TreatmentReportAdded",
                    EntityName = nameof(RoomTreatmentApplicationAttachment),
                    EntityKey = attachment.Id.ToString(),
                    AfterValuesJson = JsonSerializer.Serialize(new
                    {
                        TreatmentApplicationId = application.Id,
                        AttachmentId = attachment.Id,
                        attachment.FileName,
                        attachment.ContentType,
                        FileSize = attachment.FileSizeBytes,
                        Actor = actor.Email
                    }),
                    SourceApplication = SourceApplication,
                    CreatedAt = now
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                uploaded++;
            }
            catch (DbUpdateException exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                if (reference is not null) await TryVoidDuplicateAsync(reference.StorageKey, cancellationToken);
                dbContext.ChangeTracker.Clear();
                if (await OperationExistsAsync(applicationId, operationKey, cancellationToken))
                {
                    logger.LogInformation(exception, "Duplicate treatment report upload was suppressed. ApplicationId={ApplicationId}; OperationKey={OperationKey}", applicationId, operationKey);
                    uploaded++;
                }
                else
                {
                    logger.LogError(exception, "Treatment report metadata could not be saved. ApplicationId={ApplicationId}; OperationKey={OperationKey}", applicationId, operationKey);
                    failures.Add($"{SafeDisplayName(file.FileName)}: upload metadata failed; retry from Treatment Application History.");
                }
            }
            catch (Exception exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                if (reference is not null) await TryVoidDuplicateAsync(reference.StorageKey, cancellationToken);
                dbContext.ChangeTracker.Clear();
                logger.LogError(exception, "Treatment report upload failed. ApplicationId={ApplicationId}; FileName={FileName}", applicationId, SafeDisplayName(file.FileName));
                failures.Add($"{SafeDisplayName(file.FileName)}: upload failed; retry from Treatment Application History.");
            }
        }

        return new(uploaded, failures);
    }

    public async Task<(Stream? Content, string? ContentType, string? FileName)> OpenReadAsync(
        long applicationId,
        long attachmentId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var attachment = await dbContext.RoomTreatmentApplicationAttachments.AsNoTracking()
            .Include(x => x.RoomTreatmentApplication)
            .SingleOrDefaultAsync(x => x.Id == attachmentId && x.RoomTreatmentApplicationId == applicationId && !x.IsDeleted, cancellationToken);
        if (attachment is null) return (null, null, null);
        var permissionArea = attachment.RoomTreatmentApplication.ApplicationLevel == TreatmentApplicationLevels.Receiving
            ? ApplicationAreas.Receipts
            : ApplicationAreas.Rooms;
        if (!await access.HasAccessAsync(principal, permissionArea, PageAccessLevel.View, cancellationToken))
            return (null, null, null);
        try
        {
            return (await fileStorage.OpenReadAsync(attachment.FileId, cancellationToken), attachment.ContentType, attachment.FileName);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Treatment report content could not be loaded. ApplicationId={ApplicationId}; AttachmentId={AttachmentId}; Provider={Provider}", applicationId, attachmentId, attachment.StorageProvider);
            return (null, null, null);
        }
    }

    public async Task<string?> RemoveAsync(long applicationId, long attachmentId, string reason, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "A removal reason is required.";
        if (reason.Trim().Length > 1000) return "Removal reason cannot exceed 1000 characters.";
        var attachment = await dbContext.RoomTreatmentApplicationAttachments
            .Include(x => x.RoomTreatmentApplication)
            .SingleOrDefaultAsync(x => x.Id == attachmentId && x.RoomTreatmentApplicationId == applicationId && !x.IsDeleted, cancellationToken);
        if (attachment is null) return "Treatment report attachment was not found.";
        var permissionArea = attachment.RoomTreatmentApplication.ApplicationLevel == TreatmentApplicationLevels.Receiving
            ? ApplicationAreas.Receipts
            : ApplicationAreas.RoomTransactions;
        var permissionLabel = attachment.RoomTreatmentApplication.ApplicationLevel == TreatmentApplicationLevels.Receiving ? "Receipts" : "Room Transactions";
        if (!await access.HasAccessAsync(principal, permissionArea, PageAccessLevel.Admin, cancellationToken))
            return $"{permissionLabel} Admin access is required to remove a treatment report.";
        var actor = await CurrentUserAsync(principal, cancellationToken);
        if (actor is null) return "The active administrator could not be resolved.";
        var now = businessTime.UtcNow;
        attachment.IsDeleted = true;
        attachment.DeletedAt = now;
        attachment.DeletedByUserId = actor.Id;
        attachment.DeleteReason = reason.Trim();
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = actor.Id,
            Action = "TreatmentReportRemoved",
            EntityName = nameof(RoomTreatmentApplicationAttachment),
            EntityKey = attachment.Id.ToString(),
            BeforeValuesJson = JsonSerializer.Serialize(new { TreatmentApplicationId = applicationId, AttachmentId = attachment.Id, attachment.FileName, attachment.ContentType, FileSize = attachment.FileSizeBytes }),
            AfterValuesJson = JsonSerializer.Serialize(new { attachment.IsDeleted, attachment.DeletedAt, RemovedBy = actor.Email, Reason = reason.Trim() }),
            SourceApplication = SourceApplication,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    internal static async Task<string?> ValidateAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0) return "The file is empty.";
        if (file.Length > MaxFileSizeBytes) return "Files must be 15 MB or smaller.";
        var contentType = NormalizeContentType(file.ContentType);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var expected = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (expected is null || !string.Equals(contentType, expected, StringComparison.OrdinalIgnoreCase))
            return "Only PDF, JPG, PNG, or WEBP treatment reports are allowed.";
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        var validSignature = contentType switch
        {
            "application/pdf" => read >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            "image/jpeg" => read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            "image/png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            "image/webp" => read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        return validSignature ? null : "The file contents do not match the selected file type.";
    }

    private async Task<User?> CurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToLower() == email, cancellationToken);
    }

    private Task<bool> OperationExistsAsync(long applicationId, string operationKey, CancellationToken cancellationToken) =>
        dbContext.RoomTreatmentApplicationAttachments.AsNoTracking().AnyAsync(x => x.RoomTreatmentApplicationId == applicationId && x.OperationKey == operationKey, cancellationToken);

    private async Task TryVoidDuplicateAsync(string storageKey, CancellationToken cancellationToken)
    {
        try { await fileStorage.DeleteOrVoidAsync(storageKey, cancellationToken); }
        catch (Exception exception) { logger.LogWarning(exception, "Duplicate/failed treatment report storage object could not be voided."); }
    }

    private static string NormalizeContentType(string contentType) =>
        string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : contentType.Trim().ToLowerInvariant();

    private static string SafeDisplayName(string value) => Path.GetFileName(value);

    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(x => invalid.Contains(x) || char.IsControl(x) ? '_' : x));
        return string.IsNullOrWhiteSpace(safe) ? $"Treatment-Report-{Guid.NewGuid():N}" : safe[..Math.Min(safe.Length, 255)];
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(x => invalid.Contains(x) || x is '/' or '\\' || char.IsControl(x) ? '_' : x));
    }
}
