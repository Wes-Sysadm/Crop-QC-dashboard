using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class Tr108859DroppedBinsCorrectionConstants
{
    public const string CommandName = "--correct-tr108859-dropped-bins";
    public const string ApplyAuthorizationToken = "APPLY_REVIEWED_TR108859_DROPPED_BINS";
    public const string OperationKey = "reviewed-tr108859-dropped-bins-2";
    public const long VerifiedRestoreBackupRunId = 64;
    public const string VerifiedRestorePackageSha256 = "98d50051978f375d42ea11293288bd75229a2d43b24aa86d71c3428f3d2c8820";
}

public sealed record Tr108859DroppedBinsCorrectionRequest(
    bool Apply,
    bool ConfirmProduction,
    bool ConfirmDisposableRestore,
    long? VerifiedBackupRunId,
    string? VerifiedBackupPackageSha256,
    string RequestedByEmail,
    string Reason,
    string? ExpectedTargetFingerprint,
    string? ExpectedProtectedFingerprint,
    string? AuthorizationToken);

public sealed record Tr108859DroppedBinsEvidence(
    long ReceiptId,
    string CompuTechReceiptId,
    int BinCount,
    DateTimeOffset ReceivedAt,
    int WarehouseId,
    string Warehouse,
    int RoomId,
    string Room,
    int CropYear,
    string? GrowerNumber,
    string GrowerName,
    string LotCode,
    int? GrowerLotId,
    int FruitProfileId,
    string Variety,
    string ProductionType,
    bool IsOrganic,
    int AdjustmentCount,
    int CurrentLedgerBalance,
    int BinsRunCount,
    int TransferAdjustmentCount,
    int ReceiptOverrideCount,
    int ManualTrueUpCount,
    int ExistingLossCount,
    long LatestAdjustmentId);

public sealed record Tr108859DroppedBinsCorrectionPreflight(
    string State,
    DateTimeOffset GeneratedAtUtc,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues,
    Tr108859DroppedBinsEvidence? Evidence,
    long? LossId);

public sealed record Tr108859DroppedBinsCorrectionResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    Tr108859DroppedBinsCorrectionPreflight Preflight);

public interface ITr108859DroppedBinsCorrectionService
{
    Task<Tr108859DroppedBinsCorrectionPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<Tr108859DroppedBinsCorrectionResult> RunAsync(Tr108859DroppedBinsCorrectionRequest request, CancellationToken cancellationToken);
}

public sealed class Tr108859DroppedBinsCorrectionService(
    CropQcDbContext dbContext,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    IRoomInventoryLedgerQueryService ledgerQuery,
    IRoomInventoryLossService lossService,
    ILogger<Tr108859DroppedBinsCorrectionService> logger) : ITr108859DroppedBinsCorrectionService
{
    private const string ReceiptReference = "TR108859";

    public async Task<Tr108859DroppedBinsCorrectionPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile)
            .Where(x => x.CompuTechReceiptId == ReceiptReference)
            .ToListAsync(cancellationToken);
        var receipt = receipts.SingleOrDefault();
        if (receipts.Count != 1 || receipt is null)
        {
            issues.Add($"Expected exactly one active receipt {ReceiptReference}; found {receipts.Count}.");
        }

        var existingLosses = await dbContext.RoomInventoryLosses.AsNoTracking()
            .Include(x => x.InventoryAdjustments)
            .Where(x => x.OperationKey == Tr108859DroppedBinsCorrectionConstants.OperationKey)
            .ToListAsync(cancellationToken);
        if (existingLosses.Count > 1) issues.Add("The reviewed operation key is not unique.");

        Tr108859DroppedBinsEvidence? evidence = null;
        if (receipt is not null)
        {
            var adjustmentQuery = dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.ReceiptId == receipt.Id);
            var adjustments = dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
                ? (await adjustmentQuery.ToListAsync(cancellationToken)).OrderBy(x => x.AdjustmentAt).ThenBy(x => x.Id).ToList()
                : await adjustmentQuery.OrderBy(x => x.AdjustmentAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
            var snapshots = await ledgerQuery.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], receipt.FruitProfileId, cancellationToken);
            var receiptLot = string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.LotCode : receipt.GrowerNumber;
            var matchingSnapshots = snapshots.Where(x =>
                x.CropYear == receipt.CropYear
                && x.GrowerLotId == receipt.GrowerLotId
                && string.Equals(x.Lot, receiptLot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Variety, receipt.FruitProfile.VarietyCode, StringComparison.OrdinalIgnoreCase)
                && x.IsOrganic == receipt.FruitProfile.IsOrganic).ToList();
            var snapshot = matchingSnapshots.SingleOrDefault();
            var binsRunCount = await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
            var transferCount = adjustments.Count(x => x.RoomTransferId is not null);
            var overrideCount = await dbContext.ReceiptInventoryOverrides.AsNoTracking().CountAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
            var trueUpCount = adjustments.Count(x => x.AdjustmentType == "ManualTrueUp");
            evidence = new(
                receipt.Id, receipt.CompuTechReceiptId, receipt.BinCount, receipt.ReceivedAt,
                receipt.WarehouseId, receipt.Warehouse.Code, receipt.RoomId,
                receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
                receipt.CropYear, receipt.GrowerNumber, receipt.GrowerName, receipt.LotCode,
                receipt.GrowerLotId, receipt.FruitProfileId, receipt.FruitProfile.VarietyCode,
                receipt.FruitProfile.ProductionType, receipt.FruitProfile.IsOrganic,
                adjustments.Count, snapshot?.CurrentBins ?? int.MinValue, binsRunCount,
                transferCount, overrideCount, trueUpCount, existingLosses.Count,
                snapshot?.LatestAdjustmentId ?? 0);

            var applied = existingLosses.SingleOrDefault();
            if (applied is not null)
            {
                if (receipt.BinCount != 28 || applied.ReceiptId != receipt.Id || applied.BinCount != 2
                    || applied.LossType != RoomInventoryLossTypes.Dropped || applied.IsReversed
                    || applied.InventoryAdjustments.Count(x => x.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBins && x.ChangeAmount == -2) != 1
                    || snapshot?.CurrentBins != 26)
                {
                    issues.Add("An operation with the reviewed key exists but does not match the exact applied correction shape.");
                }
            }
            else
            {
                if (receipt.BinCount != 28) issues.Add("Case A is not proven: the receipt quantity is not exactly 28.");
                if (matchingSnapshots.Count != 1 || snapshot?.CurrentBins != 28) issues.Add("Case A is not proven: exact current packable inventory is not uniquely 28.");
                if (adjustments.Count != 1 || adjustments[0].ChangeAmount != 28 || adjustments[0].AdjustmentType != "ReceiptAdd")
                    issues.Add("Case A is not proven: receipt-linked ledger history is not exactly the original +28 receipt adjustment.");
                if (binsRunCount != 0 || transferCount != 0 || overrideCount != 0 || trueUpCount != 0)
                    issues.Add("Subsequent run, transfer, receipt override, or true-up activity exists; correction is refused.");
            }
        }

        var targetFingerprint = Sha256(JsonSerializer.Serialize(new { evidence, ExistingLosses = existingLosses.Select(x => new { x.Id, x.OperationKey, x.BinCount, x.IsReversed }) }));
        var protectedFingerprint = await CaptureProtectedFingerprintAsync(cancellationToken);
        var state = issues.Count > 0 ? "Refused" : existingLosses.Count == 1 ? "AlreadyApplied" : "Ready";
        return new(state, businessTime.UtcNow, targetFingerprint, protectedFingerprint, issues, evidence, existingLosses.SingleOrDefault()?.Id);
    }

    public async Task<Tr108859DroppedBinsCorrectionResult> RunAsync(Tr108859DroppedBinsCorrectionRequest request, CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused") return Failed("Preflight refused the TR108859 correction. No data was changed.", preflight);
        if (preflight.State == "AlreadyApplied") return new(true, false, true, "The exact reviewed TR108859 correction is already applied; zero writes were made.", preflight);
        if (!request.Apply) return new(true, false, false, "Dry-run passed: Case A is proven (28 received, no later activity, two dropped bins pending).", preflight);
        if (request.AuthorizationToken != Tr108859DroppedBinsCorrectionConstants.ApplyAuthorizationToken)
            return Failed("Apply requires the exact reviewed authorization token.", preflight);
        if (appEnvironment.IsProduction && !request.ConfirmProduction)
            return Failed("Production apply requires --confirm-production.", preflight);
        if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("Apply requires a correction reason.", preflight);
        if (!string.Equals(request.ExpectedTargetFingerprint, preflight.TargetFingerprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.OrdinalIgnoreCase))
            return Failed("The reviewed target or protected fingerprint does not match current database state.", preflight);

        var backup = request.VerifiedBackupRunId is null ? null : await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId, cancellationToken);
        var databaseVerified = backup is not null && backup.Status == BackupRunStatuses.Succeeded
            && backup.VerifiedAt is not null && backup.RetentionProcessedAt is not null
            && backup.LeaseReleasedAt is not null && backup.PrunedAt is null
            && !string.IsNullOrWhiteSpace(backup.Sha256)
            && string.Equals(request.VerifiedBackupPackageSha256, backup.Sha256, StringComparison.OrdinalIgnoreCase);
        var restoreAttested = !appEnvironment.IsProduction && request.ConfirmDisposableRestore
            && request.VerifiedBackupRunId == Tr108859DroppedBinsCorrectionConstants.VerifiedRestoreBackupRunId
            && string.Equals(request.VerifiedBackupPackageSha256, Tr108859DroppedBinsCorrectionConstants.VerifiedRestorePackageSha256, StringComparison.OrdinalIgnoreCase)
            && backup is not null;
        if (!databaseVerified && !restoreAttested)
            return Failed("Apply requires a fully verified retained backup, or the exact reviewed run-64 package attestation on a disposable restore.", preflight);

        var admin = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == request.RequestedByEmail && x.IsActive
            && x.UserRoles.Any(ur => ur.Role.IsActive && ur.Role.Name == BuiltInRoleNames.Admin), cancellationToken);
        if (admin is null) return Failed("The correction actor is not an active built-in Admin.", preflight);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var locked = await PreflightAsync(cancellationToken);
            if (locked.State != "Ready" || locked.TargetFingerprint != preflight.TargetFingerprint || locked.ProtectedFingerprint != preflight.ProtectedFingerprint)
                throw new InvalidOperationException("Database state changed after review preflight.");
            var evidence = locked.Evidence!;
            var write = await lossService.CreateReviewedCorrectionAsync(new(
                Tr108859DroppedBinsCorrectionConstants.OperationKey,
                evidence.RoomId,
                evidence.LatestAdjustmentId,
                28,
                2,
                null,
                request.Reason.Trim(),
                "Historical correction: 28 bins were received; 2 later became unavailable because they were dropped. Exact occurrence time is unavailable. Receipt quantity remains the received quantity.",
                evidence.ReceiptId,
                "CropQc.Web reviewed TR108859 dropped-bin correction command"), admin.Id, cancellationToken);
            if (!write.Success || write.AlreadyApplied) throw new InvalidOperationException(write.Error ?? "Reviewed loss write did not create exactly one correction.");
            dbContext.ChangeTracker.Clear();
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied" || postflight.Evidence?.BinCount != 28
                || postflight.Evidence.CurrentLedgerBalance != 26 || postflight.LossId != write.LossId
                || postflight.ProtectedFingerprint != preflight.ProtectedFingerprint)
                throw new InvalidOperationException("Focused post-apply integrity verification failed.");
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Reviewed TR108859 dropped-bin correction applied. ReceiptId={ReceiptId} LossId={LossId} Admin={Admin} BackupRunId={BackupRunId}", evidence.ReceiptId, write.LossId, admin.Email, backup!.Id);
            return new(true, true, false, "TR108859 now preserves 28 received, records 2 dropped, and has 26 current packable bins.", postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "TR108859 dropped-bin correction failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("Correction failed and was rolled back. Review restricted logs.", await PreflightAsync(cancellationToken));
        }
    }

    private async Task<string> CaptureProtectedFingerprintAsync(CancellationToken cancellationToken)
    {
        var protectedState = new
        {
            Receipts = await dbContext.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.IsDeleted, x.ConcurrencyVersion }).ToListAsync(cancellationToken),
            BinsRunEntries = await dbContext.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinsRun, x.ActualRunId, x.IsReversed }).ToListAsync(cancellationToken),
            ActualRuns = await dbContext.ActualRuns.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CurrentRevisionNumber }).ToListAsync(cancellationToken),
            Transfers = await dbContext.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            GrowerLots = await dbContext.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive, x.LotNumber }).ToListAsync(cancellationToken),
            Migrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)
        };
        return Sha256(JsonSerializer.Serialize(protectedState));
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Tr108859DroppedBinsCorrectionResult Failed(string message, Tr108859DroppedBinsCorrectionPreflight preflight) => new(false, false, false, message, preflight);
}
