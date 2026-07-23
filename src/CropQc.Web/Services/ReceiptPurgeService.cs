using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IReceiptPurgeService
{
    Task<ReceiptDeletionConfirmationViewModel?> GetDeletionConfirmationAsync(long receiptId, CancellationToken cancellationToken);
    Task<string?> DeleteEligibleReceiptAsync(DeleteReceiptForm form, string requestedByEmail, CancellationToken cancellationToken);
    Task<ReceiptPurgePreflight> PreflightAsync(int targetCropYear, CancellationToken cancellationToken);
    Task<ReceiptPurgeResult> PurgeAsync(ReceiptPurgeRequest request, CancellationToken cancellationToken);
}

public sealed record ReceiptPurgeRequest(
    int TargetCropYear,
    bool Apply,
    bool ConfirmProduction,
    long? VerifiedBackupRunId,
    string RequestedByEmail,
    string Reason);

public sealed record ReceiptPurgeResult(
    bool Success,
    bool Applied,
    string Message,
    Guid? OperationId,
    ReceiptPurgePreflight Preflight,
    ReceiptPurgeDeletedCounts? DeletedCounts);

public sealed record ReceiptPurgePreflight(
    int TargetCropYear,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ReceiptPurgeReceipt> Receipts,
    ReceiptPurgeDependencyTotals Totals,
    ReceiptPreservationBaseline PreservationBaseline);

public sealed record ReceiptPurgeReceipt(
    long ReceiptId,
    string ReceiptNumber,
    DateTimeOffset ReceivedAt,
    string Grower,
    string GrowerNumber,
    string Orchard,
    string Block,
    string Variety,
    string Warehouse,
    string Room,
    int GrossBins,
    int RemainingBins,
    ReceiptPurgeDependencyTotals Dependencies);

public sealed record ReceiptPurgeDependencyTotals(
    int Receipts,
    int QcSamples,
    int FruitReadings,
    int Defects,
    int Photos,
    int EmailLogs,
    int InventoryAdjustments,
    int Transfers,
    int DepletionsAndTrueUps,
    int BinsRun,
    int AuditRecords,
    int OfflineSyncItems);

public sealed record ReceiptPreservationBaseline(
    int CropYear,
    int ReceiptCount,
    string ReceiptIdentitySha256,
    int QcSamples,
    int FruitReadings,
    int InventoryAdjustments,
    int BinsRun,
    int FieldSamples,
    int BackupRuns);

public sealed record ReceiptPurgeDeletedCounts(
    int Receipts,
    int QcSamples,
    int FruitReadings,
    int Defects,
    int Photos,
    int EmailLogs,
    int InventoryAdjustments,
    int Depletions,
    int BinsRun,
    int OfflineSyncItems,
    int ExistingAuditRecordsPreserved,
    int DeletionAuditsCreated);

public sealed class ReceiptPurgeService(
    CropQcDbContext dbContext,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    ILogger<ReceiptPurgeService> logger) : IReceiptPurgeService
{
    public const int AuthorizedProductionPurgeCropYear = 2026;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ReceiptDeletionConfirmationViewModel?> GetDeletionConfirmationAsync(long receiptId, CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        var dependencies = await GetDependencyTotalsAsync([receipt.Id], cancellationToken);
        var blocking = BlockingReasons(dependencies);
        return new ReceiptDeletionConfirmationViewModel
        {
            Id = receipt.Id,
            ReceiptNumber = receipt.CompuTechReceiptId,
            CropYear = receipt.CropYear,
            ReceivedAt = receipt.ReceivedAt,
            Grower = receipt.GrowerName,
            GrowerNumber = receipt.GrowerNumber ?? "",
            Variety = receipt.FruitProfile.VarietyCode,
            Lot = receipt.LotCode,
            Warehouse = receipt.Warehouse.Code,
            Room = receipt.Room.Code,
            GrossBins = receipt.BinCount,
            Dependencies = ToViewModel(dependencies),
            HasBlockingOperationalHistory = blocking.Count > 0,
            BlockingReasons = blocking,
            Form = new DeleteReceiptForm
            {
                Id = receipt.Id,
                OperationToken = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture)
            }
        };
    }

    public async Task<string?> DeleteEligibleReceiptAsync(DeleteReceiptForm form, string requestedByEmail, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(form.OperationToken, out var operationId))
        {
            return "The deletion confirmation expired. Reopen the receipt deletion page.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "A deletion reason is required.";
        }

        if (!form.ConfirmDeletion)
        {
            return "Select the second confirmation before deleting the receipt.";
        }

        var receipt = await dbContext.Receipts
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return "Receipt not found or already deleted.";
        }

        if (!string.Equals(form.ConfirmationValue.Trim(), receipt.CompuTechReceiptId, StringComparison.Ordinal))
        {
            return "Type the exact receipt number to confirm deletion.";
        }

        var dependencies = await GetDependencyTotalsAsync([receipt.Id], cancellationToken);
        var blockers = BlockingReasons(dependencies);
        if (blockers.Count > 0)
        {
            return $"Receipt deletion refused because it has operational history: {string.Join("; ", blockers)}. Use only the separately authorized, backup-gated purge command when removal is intentional.";
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = businessTime.UtcNow;
        dbContext.ReceiptDeletionAudits.Add(new ReceiptDeletionAudit
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            DeletedReceiptId = receipt.Id,
            ReceiptNumber = receipt.CompuTechReceiptId,
            CropYear = receipt.CropYear,
            IdentifyingFieldsJson = IdentifyingJson(receipt),
            DependencyCountsJson = JsonSerializer.Serialize(dependencies, JsonOptions),
            DeletedByEmail = requestedByEmail,
            DeletedAt = now,
            Reason = form.Reason.Trim(),
            Result = "SoftDeleted"
        });
        receipt.IsDeleted = true;
        receipt.DeletedAt = now;
        receipt.DeleteReason = form.Reason.Trim();
        receipt.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "Delete",
            EntityName = nameof(Receipt),
            EntityKey = receipt.Id.ToString(CultureInfo.InvariantCulture),
            AfterValuesJson = JsonSerializer.Serialize(new { receipt.Id, receipt.CompuTechReceiptId, receipt.CropYear, receipt.DeletedAt, receipt.DeleteReason, operationId }, JsonOptions),
            SourceApplication = "CropQc.Web",
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<ReceiptPurgePreflight> PreflightAsync(int targetCropYear, CancellationToken cancellationToken)
    {
        ValidateTargetCropYear(targetCropYear);
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Where(x => x.CropYear == targetCropYear)
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
                .ThenInclude(x => x!.CanonicalOrchard)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var rows = new List<ReceiptPurgeReceipt>(receipts.Count);
        foreach (var receipt in receipts)
        {
            var dependencies = await GetDependencyTotalsAsync([receipt.Id], cancellationToken);
            var depleted = await dbContext.RoomDepletions.AsNoTracking()
                .Where(x => x.ReceiptId == receipt.Id && !x.IsVoided)
                .SumAsync(x => (int?)x.BinCountDepleted, cancellationToken) ?? 0;
            rows.Add(new ReceiptPurgeReceipt(
                receipt.Id,
                receipt.CompuTechReceiptId,
                receipt.ReceivedAt,
                receipt.GrowerName,
                receipt.GrowerNumber ?? "",
                receipt.CanonicalOrchardBlock?.CanonicalOrchard?.OrchardName ?? "",
                receipt.CanonicalOrchardBlock?.CanonicalBlockName ?? "",
                receipt.FruitProfile.VarietyCode,
                receipt.Warehouse.Code,
                receipt.Room.Code,
                receipt.BinCount,
                Math.Max(0, receipt.BinCount - depleted),
                dependencies));
        }

        var totals = Sum(rows.Select(x => x.Dependencies), receipts.Count);
        var baseline = await GetPreservationBaselineAsync(2025, cancellationToken);
        return new ReceiptPurgePreflight(targetCropYear, businessTime.UtcNow, rows, totals, baseline);
    }

    public async Task<ReceiptPurgeResult> PurgeAsync(ReceiptPurgeRequest request, CancellationToken cancellationToken)
    {
        ValidateTargetCropYear(request.TargetCropYear);
        var preflight = await PreflightAsync(request.TargetCropYear, cancellationToken);
        if (!request.Apply)
        {
            return new ReceiptPurgeResult(true, false, $"Dry run found {preflight.Receipts.Count} receipt(s) with persisted CropYear {request.TargetCropYear}.", null, preflight, null);
        }

        if (appEnvironment.IsProduction && !request.ConfirmProduction)
        {
            return new ReceiptPurgeResult(false, false, "Production apply requires --confirm-production.", null, preflight, null);
        }

        if (request.VerifiedBackupRunId is null)
        {
            return new ReceiptPurgeResult(false, false, "A verified backup run ID is required.", null, preflight, null);
        }

        var backup = await dbContext.BackupRunRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId.Value, cancellationToken);
        if (backup is null
            || backup.Status != BackupRunStatuses.Succeeded
            || backup.VerifiedAt is null
            || backup.LeaseReleasedAt is null
            || backup.RetentionProcessedAt is null
            || backup.PrunedAt is not null)
        {
            return new ReceiptPurgeResult(false, false, "The supplied backup run is not a current, fully verified backup with retention completed and its lease released.", null, preflight, null);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return new ReceiptPurgeResult(false, false, "A purge reason is required.", null, preflight, null);
        }

        if (preflight.Receipts.Count == 0)
        {
            return new ReceiptPurgeResult(true, false, "No persisted CropYear 2026 receipts matched; nothing was deleted.", null, preflight, new ReceiptPurgeDeletedCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        var operationId = Guid.NewGuid();
        var operation = new ReceiptPurgeOperation
        {
            Id = operationId,
            TargetCropYear = request.TargetCropYear,
            BackupRunId = backup.Id,
            RequestedByEmail = request.RequestedByEmail,
            Reason = request.Reason.Trim(),
            Status = ReceiptPurgeStatuses.Running,
            StartedAt = businessTime.UtcNow,
            PreflightJson = JsonSerializer.Serialize(preflight, JsonOptions),
            PreservationBaselineJson = JsonSerializer.Serialize(preflight.PreservationBaseline, JsonOptions)
        };
        dbContext.ReceiptPurgeOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var receiptIds = preflight.Receipts.Select(x => x.ReceiptId).ToArray();
            var sampleIds = await dbContext.QcSamples.Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value)).Select(x => x.Id).ToArrayAsync(cancellationToken);
            var readingIds = await dbContext.QcFruitReadings.Where(x => sampleIds.Contains(x.QcSampleId)).Select(x => x.Id).ToArrayAsync(cancellationToken);
            var depletionIds = await dbContext.RoomDepletions.Where(x => receiptIds.Contains(x.ReceiptId)).Select(x => x.Id).ToArrayAsync(cancellationToken);
            var adjustmentIds = await dbContext.RoomInventoryAdjustments
                .Where(x => (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                    || (x.RoomDepletionId != null && depletionIds.Contains(x.RoomDepletionId.Value)))
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            foreach (var receipt in preflight.Receipts)
            {
                dbContext.ReceiptDeletionAudits.Add(new ReceiptDeletionAudit
                {
                    Id = Guid.NewGuid(),
                    OperationId = operationId,
                    DeletedReceiptId = receipt.ReceiptId,
                    ReceiptNumber = receipt.ReceiptNumber,
                    CropYear = request.TargetCropYear,
                    IdentifyingFieldsJson = JsonSerializer.Serialize(receipt, JsonOptions),
                    DependencyCountsJson = JsonSerializer.Serialize(receipt.Dependencies, JsonOptions),
                    DeletedByEmail = request.RequestedByEmail,
                    DeletedAt = businessTime.UtcNow,
                    Reason = request.Reason.Trim(),
                    BackupRunId = backup.Id,
                    Result = "Purged"
                });
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            var deletedDefects = await dbContext.QcFruitDefects.Where(x => readingIds.Contains(x.QcFruitReadingId)).ExecuteDeleteAsync(cancellationToken);
            var deletedPhotos = await dbContext.QcPhotos.Where(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || (x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value))).ExecuteDeleteAsync(cancellationToken);
            var deletedEmailLogs = await dbContext.QcSummaryEmailLogs.Where(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || (x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value))).ExecuteDeleteAsync(cancellationToken);
            var deletedSync = await dbContext.OfflineSyncItems.Where(x =>
                x.ServerEntityId != null
                && sampleIds.Contains(x.ServerEntityId.Value)
                && (x.EntityName == nameof(QcSample) || x.EntityName == "Sample" || x.EntityName == "FieldSample")).ExecuteDeleteAsync(cancellationToken);
            var deletedReadings = await dbContext.QcFruitReadings.Where(x => sampleIds.Contains(x.QcSampleId)).ExecuteDeleteAsync(cancellationToken);
            var deletedSamples = await dbContext.QcSamples.Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value)).ExecuteDeleteAsync(cancellationToken);
            var deletedBinsRun = await dbContext.BinsRunEntries.Where(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || adjustmentIds.Contains(x.InventoryAdjustmentId)
                || (x.SourceInventoryAdjustmentId != null && adjustmentIds.Contains(x.SourceInventoryAdjustmentId.Value))).ExecuteDeleteAsync(cancellationToken);
            var deletedAdjustments = await dbContext.RoomInventoryAdjustments.Where(x => adjustmentIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
            var deletedDepletions = await dbContext.RoomDepletions.Where(x => receiptIds.Contains(x.ReceiptId)).ExecuteDeleteAsync(cancellationToken);
            var deletedReceipts = await dbContext.Receipts.Where(x => x.CropYear == request.TargetCropYear && receiptIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);

            var expected = preflight.Totals;
            RequireCount("receipts", expected.Receipts, deletedReceipts);
            RequireCount("samples", expected.QcSamples, deletedSamples);
            RequireCount("fruit readings", expected.FruitReadings, deletedReadings);
            RequireCount("defects", expected.Defects, deletedDefects);
            RequireCount("photos", expected.Photos, deletedPhotos);
            RequireCount("email logs", expected.EmailLogs, deletedEmailLogs);
            RequireCount("inventory adjustments", expected.InventoryAdjustments, deletedAdjustments);
            RequireCount("depletions", expected.DepletionsAndTrueUps, deletedDepletions);
            RequireCount("Bins Run", expected.BinsRun, deletedBinsRun);
            RequireCount("offline sync items", expected.OfflineSyncItems, deletedSync);

            if (await dbContext.Receipts.AnyAsync(x => x.CropYear == request.TargetCropYear, cancellationToken))
            {
                throw new InvalidOperationException("Post-delete verification still found a target crop-year receipt.");
            }

            var preservationAfter = await GetPreservationBaselineAsync(2025, cancellationToken);
            if (preflight.PreservationBaseline != preservationAfter)
            {
                throw new InvalidOperationException("The 2025/Field Sample/backup preservation baseline changed; the purge was rolled back.");
            }

            var deletedCounts = new ReceiptPurgeDeletedCounts(
                deletedReceipts,
                deletedSamples,
                deletedReadings,
                deletedDefects,
                deletedPhotos,
                deletedEmailLogs,
                deletedAdjustments,
                deletedDepletions,
                deletedBinsRun,
                deletedSync,
                expected.AuditRecords,
                preflight.Receipts.Count);
            operation.Status = ReceiptPurgeStatuses.Succeeded;
            operation.CompletedAt = businessTime.UtcNow;
            operation.DeletedCountsJson = JsonSerializer.Serialize(deletedCounts, JsonOptions);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Authorized receipt purge {OperationId} removed {ReceiptCount} persisted CropYear {CropYear} receipts after verified backup run {BackupRunId}.", operationId, deletedReceipts, request.TargetCropYear, backup.Id);
            return new ReceiptPurgeResult(true, true, $"Purged {deletedReceipts} persisted CropYear {request.TargetCropYear} receipt(s).", operationId, preflight, deletedCounts);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Receipt purge {OperationId} failed and was rolled back.", operationId);
            dbContext.ChangeTracker.Clear();
            var failedOperation = await dbContext.ReceiptPurgeOperations.SingleAsync(x => x.Id == operationId, cancellationToken);
            failedOperation.Status = ReceiptPurgeStatuses.Failed;
            failedOperation.CompletedAt = businessTime.UtcNow;
            failedOperation.ErrorSummary = "Receipt purge failed and rolled back. Review restricted server logs.";
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ReceiptPurgeResult(false, false, failedOperation.ErrorSummary, operationId, preflight, null);
        }
    }

    private async Task<ReceiptPurgeDependencyTotals> GetDependencyTotalsAsync(long[] receiptIds, CancellationToken cancellationToken)
    {
        var sampleIds = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var readingIds = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => sampleIds.Contains(x.QcSampleId))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var depletionIds = await dbContext.RoomDepletions.AsNoTracking()
            .Where(x => receiptIds.Contains(x.ReceiptId))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || (x.RoomDepletionId != null && depletionIds.Contains(x.RoomDepletionId.Value)))
            .Select(x => new { x.Id, x.AdjustmentType })
            .ToListAsync(cancellationToken);
        var adjustmentIds = adjustments.Select(x => x.Id).ToArray();
        var auditKeys = receiptIds.Select(x => x.ToString(CultureInfo.InvariantCulture))
            .Concat(sampleIds.Select(x => x.ToString(CultureInfo.InvariantCulture)))
            .Concat(readingIds.Select(x => x.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        return new ReceiptPurgeDependencyTotals(
            receiptIds.Length,
            sampleIds.Length,
            readingIds.Length,
            await dbContext.QcFruitDefects.AsNoTracking().CountAsync(x => readingIds.Contains(x.QcFruitReadingId), cancellationToken),
            await dbContext.QcPhotos.AsNoTracking().CountAsync(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || (x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value)), cancellationToken),
            await dbContext.QcSummaryEmailLogs.AsNoTracking().CountAsync(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || (x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value)), cancellationToken),
            adjustments.Count,
            adjustments.Count(x => x.AdjustmentType.Contains("Transfer", StringComparison.OrdinalIgnoreCase)),
            depletionIds.Length,
            await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || adjustmentIds.Contains(x.InventoryAdjustmentId)
                || (x.SourceInventoryAdjustmentId != null && adjustmentIds.Contains(x.SourceInventoryAdjustmentId.Value)), cancellationToken),
            await dbContext.AuditLogs.AsNoTracking().CountAsync(x => auditKeys.Contains(x.EntityKey), cancellationToken),
            await dbContext.OfflineSyncItems.AsNoTracking().CountAsync(x =>
                x.ServerEntityId != null
                && sampleIds.Contains(x.ServerEntityId.Value)
                && (x.EntityName == nameof(QcSample) || x.EntityName == "Sample" || x.EntityName == "FieldSample"), cancellationToken));
    }

    private async Task<ReceiptPreservationBaseline> GetPreservationBaselineAsync(int cropYear, CancellationToken cancellationToken)
    {
        var receiptIds = await dbContext.Receipts.AsNoTracking().Where(x => x.CropYear == cropYear).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var sampleIds = await dbContext.QcSamples.AsNoTracking().Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var adjustmentIds = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.CropYear == cropYear || (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value)))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', receiptIds)))).ToLowerInvariant();
        return new ReceiptPreservationBaseline(
            cropYear,
            receiptIds.Length,
            hash,
            sampleIds.Length,
            await dbContext.QcFruitReadings.AsNoTracking().CountAsync(x => sampleIds.Contains(x.QcSampleId), cancellationToken),
            adjustmentIds.Length,
            await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x =>
                (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                || adjustmentIds.Contains(x.InventoryAdjustmentId)
                || (x.SourceInventoryAdjustmentId != null && adjustmentIds.Contains(x.SourceInventoryAdjustmentId.Value)), cancellationToken),
            await dbContext.QcSamples.AsNoTracking().CountAsync(x => x.ReceiptId == null, cancellationToken),
            await dbContext.BackupRunRecords.AsNoTracking().CountAsync(cancellationToken));
    }

    private static ReceiptPurgeDependencyTotals Sum(IEnumerable<ReceiptPurgeDependencyTotals> values, int receiptCount)
    {
        var list = values.ToList();
        return new ReceiptPurgeDependencyTotals(
            receiptCount,
            list.Sum(x => x.QcSamples),
            list.Sum(x => x.FruitReadings),
            list.Sum(x => x.Defects),
            list.Sum(x => x.Photos),
            list.Sum(x => x.EmailLogs),
            list.Sum(x => x.InventoryAdjustments),
            list.Sum(x => x.Transfers),
            list.Sum(x => x.DepletionsAndTrueUps),
            list.Sum(x => x.BinsRun),
            list.Sum(x => x.AuditRecords),
            list.Sum(x => x.OfflineSyncItems));
    }

    private static IReadOnlyList<string> BlockingReasons(ReceiptPurgeDependencyTotals counts)
    {
        var reasons = new List<string>();
        if (counts.QcSamples > 0) reasons.Add($"{counts.QcSamples} QC sample(s)");
        if (counts.Photos > 0) reasons.Add($"{counts.Photos} photo(s)");
        if (counts.EmailLogs > 0) reasons.Add($"{counts.EmailLogs} email log(s)");
        if (counts.InventoryAdjustments > 0) reasons.Add($"{counts.InventoryAdjustments} inventory adjustment(s)");
        if (counts.DepletionsAndTrueUps > 0) reasons.Add($"{counts.DepletionsAndTrueUps} depletion/true-up record(s)");
        if (counts.BinsRun > 0) reasons.Add($"{counts.BinsRun} Bins Run record(s)");
        if (counts.OfflineSyncItems > 0) reasons.Add($"{counts.OfflineSyncItems} sync record(s)");
        return reasons;
    }

    private static ReceiptDependencyCountsViewModel ToViewModel(ReceiptPurgeDependencyTotals counts) => new()
    {
        QcSamples = counts.QcSamples,
        FruitReadings = counts.FruitReadings,
        Defects = counts.Defects,
        Photos = counts.Photos,
        EmailLogs = counts.EmailLogs,
        InventoryAdjustments = counts.InventoryAdjustments,
        Transfers = counts.Transfers,
        DepletionsAndTrueUps = counts.DepletionsAndTrueUps,
        BinsRun = counts.BinsRun,
        AuditRecords = counts.AuditRecords,
        OfflineSyncItems = counts.OfflineSyncItems
    };

    private static string IdentifyingJson(Receipt receipt) =>
        JsonSerializer.Serialize(new
        {
            receipt.Id,
            receipt.CompuTechReceiptId,
            receipt.CropYear,
            receipt.ReceivedAt,
            receipt.GrowerName,
            receipt.GrowerNumber,
            receipt.LotCode,
            warehouse = receipt.Warehouse.Code,
            room = receipt.Room.Code,
            variety = receipt.FruitProfile.VarietyCode,
            receipt.BinCount
        }, JsonOptions);

    private static void RequireCount(string category, int expected, int actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Unexpected {category} count: expected {expected}, deleted {actual}.");
        }
    }

    private static void ValidateTargetCropYear(int targetCropYear)
    {
        if (targetCropYear != AuthorizedProductionPurgeCropYear)
        {
            throw new InvalidOperationException("This purpose-built operation accepts only the explicitly authorized persisted crop year 2026. Wildcards, missing years, 2025, and earlier years are refused.");
        }
    }
}
