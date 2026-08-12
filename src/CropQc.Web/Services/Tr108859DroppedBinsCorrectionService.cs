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
    public const string MalformedTrueUpCase = "MalformedTrueUp280";
    public const string CaseA = "CaseA";
    public const string NormalizedLossReason = "Two bins were dropped after receiving.";
    public const string HistoricalNotes = "Bin Wall on bottom bin fell out causing top 2 bins to fall forward to the ground.";
    public const string CorrectionAuditSource = "CropQc.Web reviewed TR108859 dropped-bin correction command";
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
    long LatestAdjustmentId,
    string CorrectionCase,
    int CanonicalReceiptCount,
    int CanonicalReceiptAddBins,
    int CanonicalBalanceBeforeTarget,
    long? TargetAdjustmentId,
    int LaterIdentityAdjustmentCount,
    int OriginalAuditCount);

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
    private const long TargetAdjustmentId = 280;
    private const long OriginalAuditId = 23057;
    private static readonly DateTimeOffset TargetAdjustmentAt = DateTimeOffset.Parse("2026-08-11T22:23:00Z");
    private static readonly DateTimeOffset TargetCreatedAt = DateTimeOffset.Parse("2026-08-11T22:25:24.118888Z");
    private static readonly DateTimeOffset OriginalAuditCreatedAt = DateTimeOffset.Parse("2026-08-11T22:25:24.120349Z");
    private static readonly IReadOnlyDictionary<long, (string Reference, int Bins, DateTimeOffset ReceivedAt, long AdjustmentId)> ExpectedReceipts =
        new Dictionary<long, (string, int, DateTimeOffset, long)>
        {
            [208] = ("TR108859", 28, DateTimeOffset.Parse("2026-08-10T18:33:00Z"), 251),
            [209] = ("TR108860", 29, DateTimeOffset.Parse("2026-08-10T21:14:00Z"), 252),
            [225] = ("TR108861", 44, DateTimeOffset.Parse("2026-08-11T15:49:00Z"), 270),
            [226] = ("TR108862", 44, DateTimeOffset.Parse("2026-08-11T17:23:00Z"), 271),
            [227] = ("TR108863", 24, DateTimeOffset.Parse("2026-08-11T18:25:00Z"), 272),
            [228] = ("TR108864", 44, DateTimeOffset.Parse("2026-08-11T19:23:00Z"), 273),
            [230] = ("TR108865", 35, DateTimeOffset.Parse("2026-08-11T21:14:00Z"), 275)
        };

    public async Task<Tr108859DroppedBinsCorrectionPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile)
            .Where(x => x.CompuTechReceiptId == ReceiptReference)
            .ToListAsync(cancellationToken);
        var receipt = receipts.SingleOrDefault();
        if (receipts.Count != 1 || receipt is null)
            issues.Add($"Expected exactly one receipt {ReceiptReference}; found {receipts.Count}.");

        var existingLosses = await dbContext.RoomInventoryLosses.AsNoTracking()
            .Include(x => x.InventoryAdjustments)
            .Where(x => x.OperationKey == Tr108859DroppedBinsCorrectionConstants.OperationKey)
            .ToListAsync(cancellationToken);
        if (existingLosses.Count > 1) issues.Add("The reviewed operation key is not unique.");

        Tr108859DroppedBinsEvidence? evidence = null;
        if (receipt is not null)
        {
            var receiptAdjustments = await OrderedAsync(
                dbContext.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptId == receipt.Id),
                cancellationToken);
            var snapshots = await ledgerQuery.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], receipt.FruitProfileId, cancellationToken);
            var receiptLot = string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.LotCode : receipt.GrowerNumber;
            var matchingSnapshots = snapshots.Where(x =>
                x.CropYear == receipt.CropYear
                && x.GrowerLotId == receipt.GrowerLotId
                && x.FruitProfileId == receipt.FruitProfileId
                && string.Equals(x.Lot, receiptLot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Variety, receipt.FruitProfile.VarietyCode, StringComparison.OrdinalIgnoreCase)
                && x.IsOrganic == receipt.FruitProfile.IsOrganic).ToList();
            var snapshot = matchingSnapshots.SingleOrDefault();
            var binsRunCount = await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
            var transferCount = receiptAdjustments.Count(x => x.RoomTransferId is not null);
            var overrideCount = await dbContext.ReceiptInventoryOverrides.AsNoTracking().CountAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
            var target = await dbContext.RoomInventoryAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == TargetAdjustmentId, cancellationToken);
            var existingLoss = existingLosses.SingleOrDefault();
            var caseAReady = existingLoss is null
                && receipt.BinCount == 28
                && matchingSnapshots.Count == 1
                && snapshot?.CurrentBins == 28
                && receiptAdjustments.Count == 1
                && receiptAdjustments[0].ChangeAmount == 28
                && receiptAdjustments[0].AdjustmentType == "ReceiptAdd"
                && binsRunCount == 0 && transferCount == 0 && overrideCount == 0;

            var correctionCase = caseAReady ? Tr108859DroppedBinsCorrectionConstants.CaseA : Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase;
            var malformed = correctionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase
                ? await ValidateMalformedTrueUpAsync(receipt, target, existingLoss, cancellationToken)
                : new MalformedEvidence(0, 0, 0, 0, 0, 0, 0, []);
            if (caseAReady)
            {
                // Existing reviewed first-install shape remains supported.
            }
            else if (existingLoss is not null && target?.Id != TargetAdjustmentId)
            {
                ValidateCaseAApplied(receipt, snapshot, existingLoss, issues);
                correctionCase = Tr108859DroppedBinsCorrectionConstants.CaseA;
            }
            else
            {
                issues.AddRange(malformed.Issues);
            }

            evidence = new(
                receipt.Id, receipt.CompuTechReceiptId, receipt.BinCount, receipt.ReceivedAt,
                receipt.WarehouseId, receipt.Warehouse.Code, receipt.RoomId,
                receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
                receipt.CropYear, receipt.GrowerNumber, receipt.GrowerName, receipt.LotCode,
                receipt.GrowerLotId, receipt.FruitProfileId, receipt.FruitProfile.VarietyCode,
                receipt.FruitProfile.ProductionType, receipt.FruitProfile.IsOrganic,
                correctionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase ? malformed.IdentityAdjustmentCount : receiptAdjustments.Count,
                snapshot?.CurrentBins ?? int.MinValue, binsRunCount,
                transferCount, overrideCount,
                correctionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase ? malformed.ManualTrueUpCount : receiptAdjustments.Count(x => x.AdjustmentType == "ManualTrueUp"),
                existingLosses.Count, snapshot?.LatestAdjustmentId ?? 0,
                correctionCase, malformed.CanonicalReceiptCount, malformed.ReceiptAddBins,
                malformed.BalanceBeforeTarget, target?.Id, malformed.LaterAdjustmentCount,
                malformed.OriginalAuditCount);

            if (!caseAReady && correctionCase == Tr108859DroppedBinsCorrectionConstants.CaseA && existingLoss is null)
                issues.Add("Case A is not proven and the exact reviewed malformed adjustment 280 is absent.");
        }

        var targetState = await CaptureTargetStateAsync(cancellationToken);
        var targetFingerprint = Sha256(JsonSerializer.Serialize(new { evidence, TargetState = targetState }));
        var protectedFingerprint = await CaptureProtectedFingerprintAsync(cancellationToken);
        var state = issues.Count > 0 ? "Refused" : existingLosses.Count == 1 ? "AlreadyApplied" : "Ready";
        return new(state, businessTime.UtcNow, targetFingerprint, protectedFingerprint, issues, evidence, existingLosses.SingleOrDefault()?.Id);
    }

    public async Task<Tr108859DroppedBinsCorrectionResult> RunAsync(Tr108859DroppedBinsCorrectionRequest request, CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused") return Failed("Preflight refused the TR108859 correction. No data was changed.", preflight);
        if (preflight.State == "AlreadyApplied") return new(true, false, true, "The exact reviewed TR108859 correction is already applied; zero writes were made.", preflight);
        if (!request.Apply)
        {
            var detail = preflight.Evidence?.CorrectionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase
                ? "the exact malformed adjustment 280 is proven (248 canonical bins before it, +218 erroneous delta, no later activity)"
                : "Case A is proven (28 received, no later activity, two dropped bins pending)";
            return new(true, false, false, $"Dry-run passed: {detail}.", preflight);
        }
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
            RoomInventoryLossWriteResult write;
            if (evidence.CorrectionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase)
            {
                write = await lossService.NormalizeReviewedMalformedAdjustmentAsync(new(
                    Tr108859DroppedBinsCorrectionConstants.OperationKey,
                    TargetAdjustmentId,
                    466,
                    248,
                    2,
                    evidence.ReceiptId,
                    Tr108859DroppedBinsCorrectionConstants.NormalizedLossReason,
                    Tr108859DroppedBinsCorrectionConstants.HistoricalNotes,
                    Tr108859DroppedBinsCorrectionConstants.CorrectionAuditSource), admin.Id, cancellationToken);
            }
            else
            {
                write = await lossService.CreateReviewedCorrectionAsync(new(
                    Tr108859DroppedBinsCorrectionConstants.OperationKey,
                    evidence.RoomId,
                    evidence.LatestAdjustmentId,
                    28,
                    2,
                    null,
                    Tr108859DroppedBinsCorrectionConstants.NormalizedLossReason,
                    "Historical correction: 28 bins were received; 2 later became unavailable because they were dropped. Exact occurrence time is unavailable. Receipt quantity remains the received quantity.",
                    evidence.ReceiptId,
                    Tr108859DroppedBinsCorrectionConstants.CorrectionAuditSource), admin.Id, cancellationToken);
            }
            if (!write.Success || write.AlreadyApplied) throw new InvalidOperationException(write.Error ?? "Reviewed loss write did not create exactly one correction.");
            dbContext.ChangeTracker.Clear();
            var postflight = await PreflightAsync(cancellationToken);
            var expectedBalance = evidence.CorrectionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase ? 246 : 26;
            if (postflight.State != "AlreadyApplied" || postflight.Evidence?.BinCount != 28
                || postflight.Evidence.CurrentLedgerBalance != expectedBalance || postflight.LossId != write.LossId
                || postflight.ProtectedFingerprint != preflight.ProtectedFingerprint)
                throw new InvalidOperationException(
                    $"Focused post-apply integrity verification failed. State={postflight.State}; "
                    + $"Issues={string.Join(" | ", postflight.Issues)}; Balance={postflight.Evidence?.CurrentLedgerBalance}; "
                    + $"ExpectedBalance={expectedBalance}; LossId={postflight.LossId}; ExpectedLossId={write.LossId}; "
                    + $"ProtectedFingerprint={postflight.ProtectedFingerprint}; ExpectedProtectedFingerprint={preflight.ProtectedFingerprint}.");
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Reviewed TR108859 dropped-bin correction applied. Case={Case} ReceiptId={ReceiptId} LossId={LossId} Admin={Admin} BackupRunId={BackupRunId}", evidence.CorrectionCase, evidence.ReceiptId, write.LossId, admin.Email, backup!.Id);
            var message = evidence.CorrectionCase == Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase
                ? "TR108859 adjustment 280 now records a two-bin dropped loss against the 248-bin canonical identity, leaving 246 current packable bins."
                : "TR108859 now preserves 28 received, records 2 dropped, and has 26 current packable bins.";
            return new(true, true, false, message, postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "TR108859 dropped-bin correction failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("Correction failed and was rolled back. Review restricted logs.", await PreflightAsync(cancellationToken));
        }
    }

    private async Task<MalformedEvidence> ValidateMalformedTrueUpAsync(
        Receipt receipt,
        RoomInventoryAdjustment? target,
        RoomInventoryLoss? existingLoss,
        CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var expectedIds = ExpectedReceipts.Keys.ToHashSet();
        var identityReceipts = await dbContext.Receipts.AsNoTracking()
            .Where(x => expectedIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (identityReceipts.Count != ExpectedReceipts.Count) issues.Add("Reviewed malformed case is not proven: the exact seven receipts are not present.");
        foreach (var expected in ExpectedReceipts)
        {
            var actual = identityReceipts.SingleOrDefault(x => x.Id == expected.Key);
            if (actual is null
                || actual.CompuTechReceiptId != expected.Value.Reference
                || actual.BinCount != expected.Value.Bins
                || actual.ReceivedAt != expected.Value.ReceivedAt
                || actual.IsDeleted
                || actual.ReceiptType != "Truck receipt"
                || actual.WarehouseId != 1 || actual.RoomId != 17 || actual.CropYear != 2026
                || actual.GrowerLotId != 94 || actual.FruitProfileId != 2
                || actual.GrowerNumber != "9040" || actual.LotCode != "9040")
                issues.Add($"Reviewed malformed case is not proven: receipt {expected.Key}/{expected.Value.Reference} differs from the reviewed snapshot.");
        }

        var roomRows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
            .Include(x => x.FruitProfile)
            .Where(x => x.WarehouseId == 1 && x.RoomId == 17)
            .ToListAsync(cancellationToken);
        var identityRows = roomRows.Where(IsReviewedIdentity).OrderBy(x => x.AdjustmentAt).ThenBy(x => x.Id).ToList();
        var expectedAdds = ExpectedReceipts.ToDictionary(x => x.Value.AdjustmentId, x => (ReceiptId: x.Key, x.Value.Bins));
        foreach (var expected in expectedAdds)
        {
            var row = identityRows.SingleOrDefault(x => x.Id == expected.Key);
            if (row is null || row.ReceiptId != expected.Value.ReceiptId || row.AdjustmentType != "ReceiptAdd"
                || row.ChangeAmount != expected.Value.Bins || row.NewBinCount != expected.Value.Bins || !HasNoOperationalParent(row))
                issues.Add($"Reviewed malformed case is not proven: receipt-add adjustment {expected.Key} differs from the reviewed snapshot.");
        }
        if (identityRows.Count != 8 || identityRows.Count(x => expectedAdds.ContainsKey(x.Id)) != 7 || identityRows.Count(x => x.Id == TargetAdjustmentId) != 1)
            issues.Add("Reviewed malformed case is not proven: canonical ledger rows are not exactly the seven receipt adds plus adjustment 280.");

        var applied = existingLoss is not null;
        if (target is null) issues.Add("Reviewed malformed case is not proven: adjustment 280 is absent.");
        else if (target.ReceiptId != 208 || target.WarehouseId != 1 || target.RoomId != 17 || target.CropYear != 2026
            || target.GrowerLotId != 94 || target.FruitProfileId != 2 || target.GrowerName != "DL & JJ FARMS - CLARENCE"
            || target.LotNumber != "9040" || target.VarietyCode != "GALA" || target.InventoryStatus is not null
            || target.AdjustmentAt != TargetAdjustmentAt || target.CreatedAt != TargetCreatedAt || target.CreatedByUserId != 2
            || !HasNoOperationalParentExceptLoss(target))
            issues.Add("Reviewed malformed case is not proven: adjustment 280 identity, timestamps, actor, or parent shape differs.");
        else if (!applied && (target.AdjustmentType != "ManualTrueUp" || target.OldBinCount != 28 || target.ChangeAmount != 218
            || target.NewBinCount != 246 || target.Source != "Two Dropped Bins" || target.Reason != "Two Dropped Bins"
            || target.Notes != Tr108859DroppedBinsCorrectionConstants.HistoricalNotes || target.InventoryInvariantVersion != 0
            || !string.IsNullOrWhiteSpace(target.InventoryOperationKey) || target.RoomInventoryLossId is not null))
            issues.Add("Reviewed malformed case is not proven: adjustment 280 is not the exact +218 Manual True Up reviewed from run 64.");
        else if (applied && (target.AdjustmentType != RoomInventoryLossAdjustmentTypes.DroppedBins || target.OldBinCount != 248
            || target.ChangeAmount != -2 || target.NewBinCount != 246 || target.Source != "Room Inventory Loss"
            || target.Reason != Tr108859DroppedBinsCorrectionConstants.NormalizedLossReason
            || target.Notes != Tr108859DroppedBinsCorrectionConstants.HistoricalNotes
            || target.InventoryInvariantVersion != InventoryDeductionInvariantService.CurrentVersion
            || target.InventoryOperationKey != $"room-inventory-loss:{Tr108859DroppedBinsCorrectionConstants.OperationKey}:dropped"
            || target.RoomInventoryLossId != existingLoss!.Id))
            issues.Add("The reviewed operation exists but adjustment 280 does not match the exact normalized state.");

        var targetIndex = identityRows.FindIndex(x => x.Id == TargetAdjustmentId);
        var balanceBefore = targetIndex < 0 ? int.MinValue : identityRows.Take(targetIndex).Sum(x => x.ChangeAmount);
        var later = targetIndex < 0 ? 0 : identityRows.Skip(targetIndex + 1).Count();
        var receiptAddBins = identityRows.Where(x => x.AdjustmentType == "ReceiptAdd").Sum(x => x.ChangeAmount);
        if (receiptAddBins != 248 || balanceBefore != 248 || later != 0)
            issues.Add("Reviewed malformed case is not proven: canonical arithmetic is not exactly 248 before adjustment 280 with no later rows.");

        var binsRuns = await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x => x.ReceiptId != null && expectedIds.Contains(x.ReceiptId.Value), cancellationToken);
        var depletions = await dbContext.RoomDepletions.AsNoTracking().CountAsync(x => expectedIds.Contains(x.ReceiptId), cancellationToken);
        var overrides = await dbContext.ReceiptInventoryOverrides.AsNoTracking().CountAsync(x => expectedIds.Contains(x.ReceiptId), cancellationToken);
        var transfers = identityRows.Count(x => x.RoomTransferId is not null);
        if (binsRuns != 0 || depletions != 0 || overrides != 0 || transfers != 0)
            issues.Add("Reviewed malformed case is not proven: Bins Run, depletion, transfer, or receipt override activity exists for the exact receipts.");

        var originalAudits = await dbContext.AuditLogs.AsNoTracking().Where(x => x.Id == OriginalAuditId).ToListAsync(cancellationToken);
        var originalAudit = originalAudits.SingleOrDefault();
        if (originalAudits.Count != 1 || originalAudit is null || originalAudit.Action != "BinCountChange"
            || originalAudit.EntityName != nameof(RoomInventoryAdjustment) || originalAudit.EntityKey != "208"
            || originalAudit.UserId != 2 || originalAudit.SourceApplication != "Web" || originalAudit.CreatedAt != OriginalAuditCreatedAt
            || originalAudit.BeforeValuesJson is not null
            || originalAudit.AfterValuesJson != "ManualTrueUp changed bins from 28 to 246. Reason: Two Dropped Bins")
            issues.Add("Reviewed malformed case is not proven: original audit 23057 differs from the reviewed immutable record.");

        if (applied)
        {
            var lossActorIsActiveAdmin = await dbContext.Users.AsNoTracking().AnyAsync(x =>
                x.Id == existingLoss!.CreatedByUserId
                && x.IsActive
                && x.UserRoles.Any(ur => ur.Role.IsActive && ur.Role.Name == BuiltInRoleNames.Admin),
                cancellationToken);
            if (existingLoss!.ReceiptId != 208 || existingLoss.WarehouseId != 1 || existingLoss.RoomId != 17
                || existingLoss.CropYear != 2026 || existingLoss.GrowerLotId != 94 || existingLoss.FruitProfileId != 2
                || existingLoss.GrowerName != "DL & JJ FARMS - CLARENCE" || existingLoss.GrowerNumber != "9040"
                || existingLoss.LotNumber != "9040" || existingLoss.VarietyCode != "GALA" || existingLoss.InventoryStatus is not null
                || existingLoss.LossType != RoomInventoryLossTypes.Dropped || existingLoss.BinCount != 2
                || existingLoss.Reason != Tr108859DroppedBinsCorrectionConstants.NormalizedLossReason
                || existingLoss.Notes != Tr108859DroppedBinsCorrectionConstants.HistoricalNotes || existingLoss.OccurredAt is not null
                || !lossActorIsActiveAdmin || existingLoss.IsReversed || existingLoss.InventoryAdjustments.Count != 1
                || existingLoss.InventoryAdjustments.Single().Id != TargetAdjustmentId)
                issues.Add("The reviewed operation exists but the loss parent does not match the exact normalized state.");
            var correctionAudits = await dbContext.AuditLogs.AsNoTracking()
                .Where(x => x.Action == "NormalizeMalformedManualTrueUp" && x.EntityName == nameof(RoomInventoryAdjustment) && x.EntityKey == "280")
                .ToListAsync(cancellationToken);
            if (correctionAudits.Count != 1 || correctionAudits[0].SourceApplication != Tr108859DroppedBinsCorrectionConstants.CorrectionAuditSource)
                issues.Add("The reviewed operation exists but its immutable correction audit is missing or duplicated.");
        }

        return new(identityRows.Count, identityReceipts.Count, receiptAddBins, balanceBefore, later,
            identityRows.Count(x => x.AdjustmentType == "ManualTrueUp"), originalAudits.Count, issues);

        bool IsReviewedIdentity(RoomInventoryAdjustment row)
        {
            var normalizedCropYear = row.CropYear ?? row.Receipt?.CropYear;
            var normalizedGrowerLot = row.GrowerLotId ?? row.Receipt?.GrowerLotId;
            var normalizedFruitProfile = row.FruitProfileId ?? row.Receipt?.FruitProfileId;
            var normalizedLot = string.IsNullOrWhiteSpace(row.LotNumber) ? row.Receipt?.GrowerNumber ?? row.Receipt?.LotCode : row.LotNumber;
            var normalizedVariety = row.FruitProfile?.VarietyCode ?? row.Receipt?.FruitProfile.VarietyCode ?? row.VarietyCode;
            return normalizedCropYear == receipt.CropYear && normalizedGrowerLot == receipt.GrowerLotId
                && normalizedFruitProfile == receipt.FruitProfileId
                && string.Equals(normalizedLot?.Trim(), "9040", StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedVariety?.Trim(), "GALA", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ValidateCaseAApplied(Receipt receipt, RoomInventoryLedgerSnapshot? snapshot, RoomInventoryLoss loss, List<string> issues)
    {
        if (receipt.BinCount != 28 || loss.ReceiptId != receipt.Id || loss.BinCount != 2
            || loss.LossType != RoomInventoryLossTypes.Dropped || loss.IsReversed
            || loss.InventoryAdjustments.Count(x => x.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBins && x.ChangeAmount == -2) != 1
            || snapshot?.CurrentBins != 26)
            issues.Add("An operation with the reviewed key exists but does not match the exact applied Case A correction shape.");
    }

    private async Task<string> CaptureTargetStateAsync(CancellationToken cancellationToken)
    {
        var receiptIds = ExpectedReceipts.Keys.ToList();
        var state = new
        {
            Receipts = await dbContext.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.CompuTechReceiptId, x.BinCount, x.ReceivedAt, x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.GrowerNumber, x.GrowerName, x.LotCode, x.ReceiptType, x.IsDeleted }).ToListAsync(cancellationToken),
            Adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking().Where(x => receiptIds.Contains(x.ReceiptId ?? 0) || x.Id == TargetAdjustmentId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ReceiptId, x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.GrowerName, x.LotNumber, x.VarietyCode, x.InventoryStatus, x.OldBinCount, x.ChangeAmount, x.NewBinCount, x.AdjustmentType, x.Source, x.Reason, x.Notes, x.AdjustmentAt, x.CreatedAt, x.CreatedByUserId, x.InventoryInvariantVersion, x.InventoryOperationKey, x.RoomDepletionId, x.RoomTransferId, x.ReceiptInventoryOverrideId, x.ActualRunId, x.ActualRunRevisionId, x.RoomInventoryLossId }).ToListAsync(cancellationToken),
            Losses = await dbContext.RoomInventoryLosses.AsNoTracking().Where(x => x.OperationKey == Tr108859DroppedBinsCorrectionConstants.OperationKey)
                .Select(x => new { x.Id, x.OperationKey, x.ReceiptId, x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.GrowerName, x.GrowerNumber, x.LotNumber, x.VarietyCode, x.InventoryStatus, x.LossType, x.BinCount, x.Reason, x.Notes, x.OccurredAt, x.CreatedByUserId, x.CreatedAt, x.IsReversed }).ToListAsync(cancellationToken),
            Audits = await dbContext.AuditLogs.AsNoTracking().Where(x => x.Id == OriginalAuditId || (x.Action == "NormalizeMalformedManualTrueUp" && x.EntityKey == "280")).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Action, x.EntityName, x.EntityKey, x.UserId, x.BeforeValuesJson, x.AfterValuesJson, x.SourceApplication, x.CreatedAt }).ToListAsync(cancellationToken)
        };
        return JsonSerializer.Serialize(state);
    }

    private async Task<string> CaptureProtectedFingerprintAsync(CancellationToken cancellationToken)
    {
        var protectedState = new
        {
            Receipts = await dbContext.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.IsDeleted, x.ConcurrencyVersion }).ToListAsync(cancellationToken),
            Adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.Id != TargetAdjustmentId && x.InventoryOperationKey != $"room-inventory-loss:{Tr108859DroppedBinsCorrectionConstants.OperationKey}:dropped")
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.ChangeAmount, x.NewBinCount, x.AdjustmentType, x.RoomInventoryLossId }).ToListAsync(cancellationToken),
            Losses = await dbContext.RoomInventoryLosses.AsNoTracking().Where(x => x.OperationKey != Tr108859DroppedBinsCorrectionConstants.OperationKey).OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationKey, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            BinsRunEntries = await dbContext.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinsRun, x.ActualRunId, x.IsReversed }).ToListAsync(cancellationToken),
            ActualRuns = await dbContext.ActualRuns.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CurrentRevisionNumber }).ToListAsync(cancellationToken),
            ActualRunRevisions = await dbContext.ActualRunRevisions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ActualRunId, x.RevisionNumber }).ToListAsync(cancellationToken),
            Transfers = await dbContext.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            GrowerLots = await dbContext.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive, x.LotNumber }).ToListAsync(cancellationToken),
            QcSamples = await dbContext.QcSamples.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.IsDeleted }).ToListAsync(cancellationToken),
            Users = await dbContext.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Email, x.IsActive }).ToListAsync(cancellationToken),
            Roles = await dbContext.Roles.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync(cancellationToken),
            UserRoles = await dbContext.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).Select(x => new { x.UserId, x.RoleId }).ToListAsync(cancellationToken),
            RolePageAccesses = await dbContext.RolePageAccesses.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.AreaKey, x.AccessLevel }).ToListAsync(cancellationToken),
            Audits = await dbContext.AuditLogs.AsNoTracking()
                .Where(x => x.SourceApplication != Tr108859DroppedBinsCorrectionConstants.CorrectionAuditSource)
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.Action, x.EntityName, x.EntityKey, x.UserId, x.BeforeValuesJson, x.AfterValuesJson, x.SourceApplication, x.CreatedAt }).ToListAsync(cancellationToken),
            GoogleCredentials = await dbContext.UserGoogleCredentials.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.Provider, x.AccessTokenEncrypted, x.RefreshTokenEncrypted, x.Scope, x.ExpiresAt, x.UpdatedAt }).ToListAsync(cancellationToken),
            EndOfDayGroups = await dbContext.EndOfDayFillReportGroups.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Facility, x.IsActive, x.UpdatedAt }).ToListAsync(cancellationToken),
            EndOfDayRecipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.EmailAddress, x.IsActive, x.SortOrder, x.UpdatedAt }).ToListAsync(cancellationToken),
            EndOfDayAssignments = await dbContext.EndOfDayFillUserGroupAssignments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.ReportGroupId }).ToListAsync(cancellationToken),
            EndOfDaySends = await dbContext.EndOfDayFillReportSends.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReportGroupId, x.PacificReportDate, x.RevisionNumber, x.Status, x.SnapshotHash }).ToListAsync(cancellationToken),
            Migrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)
        };
        return Sha256(JsonSerializer.Serialize(protectedState));
    }

    private async Task<List<RoomInventoryAdjustment>> OrderedAsync(IQueryable<RoomInventoryAdjustment> query, CancellationToken cancellationToken) =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? (await query.ToListAsync(cancellationToken)).OrderBy(x => x.AdjustmentAt).ThenBy(x => x.Id).ToList()
            : await query.OrderBy(x => x.AdjustmentAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    private static bool HasNoOperationalParent(RoomInventoryAdjustment row) =>
        row.RoomDepletionId is null && row.RoomTransferId is null && row.ReceiptInventoryOverrideId is null
        && row.ActualRunId is null && row.ActualRunRevisionId is null && row.RoomInventoryLossId is null;

    private static bool HasNoOperationalParentExceptLoss(RoomInventoryAdjustment row) =>
        row.RoomDepletionId is null && row.RoomTransferId is null && row.ReceiptInventoryOverrideId is null
        && row.ActualRunId is null && row.ActualRunRevisionId is null;

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Tr108859DroppedBinsCorrectionResult Failed(string message, Tr108859DroppedBinsCorrectionPreflight preflight) => new(false, false, false, message, preflight);

    private sealed record MalformedEvidence(
        int IdentityAdjustmentCount,
        int CanonicalReceiptCount,
        int ReceiptAddBins,
        int BalanceBeforeTarget,
        int LaterAdjustmentCount,
        int ManualTrueUpCount,
        int OriginalAuditCount,
        IReadOnlyList<string> Issues);
}
