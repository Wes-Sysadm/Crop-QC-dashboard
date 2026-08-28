using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class TreatmentLineage144CorrectionConstants
{
    public const string CommandName = "--correct-treatment-lineage-segment-144";
    public const string ReadinessCommandName = "--verify-treatment-lineage-readiness";
    public const string ReleaseReadinessCommandName = "--verify-release-readiness";
    public const string ApplyAuthorizationToken = "REVIEWED-TREATMENT-LINEAGE-SEGMENT-144";
    public const string IdentityKey = "2026|98|2|9100|9100|GALA|CONVENTIONAL|False|";
    public const string AuditEntityName = nameof(TreatmentLineageSegment);
    public const string AuditEntityKey = "144";
    public const string AuditSource = "CropQc.Web reviewed treatment-lineage segment 144 correction";
}

public sealed record TreatmentLineage144CorrectionRequest(
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

public sealed record TreatmentLineage144CorrectionEvidence(
    int SegmentBins,
    long SegmentConcurrencyVersion,
    int RemainingLineageBins,
    int ExplicitLineageBins,
    int AuthoritativeInventoryBins,
    int AuditCount,
    long RoomInventoryAdjustmentCount,
    long RoomInventoryAdjustmentQuantity,
    long BinsRunEntryCount,
    long BinsRunQuantity,
    long ReceiptCount,
    long ReceiptQuantity,
    long MovementCount,
    long MovementQuantity);

public sealed record TreatmentLineage144CorrectionPreflight(
    string State,
    DateTimeOffset CheckedAt,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues,
    TreatmentLineage144CorrectionEvidence? Evidence);

public sealed record TreatmentLineage144CorrectionResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    TreatmentLineage144CorrectionPreflight Preflight);

public sealed record TreatmentLineageReadinessResult(
    bool Success,
    int CurrentIdentityCount,
    int BlockingIssueCount,
    IReadOnlyList<TreatmentLineageReadinessIssue> BlockingIssues,
    string Message);

public sealed record TreatmentLineageReadinessIssue(
    string Code,
    string Facility,
    int RoomId,
    string Room,
    int? CropYear,
    string? GrowerNumber,
    string Lot,
    string Variety,
    int AuthoritativeBins,
    int ExplicitLineageBins,
    int Difference,
    string IdentityKey);

public interface ITreatmentLineage144CorrectionService
{
    Task<TreatmentLineage144CorrectionPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<TreatmentLineage144CorrectionResult> RunAsync(TreatmentLineage144CorrectionRequest request, CancellationToken cancellationToken);
}

public interface ITreatmentLineageReadinessService
{
    Task<TreatmentLineageReadinessResult> VerifyAsync(CancellationToken cancellationToken);
}

public sealed class TreatmentLineageReadinessService(
    IRoomInventoryLedgerQueryService ledger,
    CropQcDbContext dbContext) : ITreatmentLineageReadinessService
{
    private const int MaximumReportedIssues = 50;

    public async Task<TreatmentLineageReadinessResult> VerifyAsync(CancellationToken cancellationToken)
    {
        var snapshots = (await ledger.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .ToList();
        var roomIds = snapshots.Select(x => x.RoomId).Distinct().ToList();
        var explicitByIdentity = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => roomIds.Contains(x.RoomId) && x.CurrentBins > 0)
            .GroupBy(x => new { x.RoomId, x.IdentityKey })
            .Select(x => new { x.Key.RoomId, x.Key.IdentityKey, Bins = x.Sum(y => y.CurrentBins) })
            .ToListAsync(cancellationToken);
        var authoritative = snapshots
            .GroupBy(RoomTreatmentService.SelectionLookupKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                Snapshot = x.OrderByDescending(y => y.LastTransactionAt).ThenByDescending(y => y.LatestAdjustmentId).First(),
                CurrentBins = x.Sum(y => y.CurrentBins)
            })
            .ToList();
        var issues = authoritative
            .Select(item =>
            {
                var snapshot = item.Snapshot;
                var identityKey = RoomTreatmentService.IdentityKey(snapshot);
                var explicitBins = explicitByIdentity
                    .Where(x => x.RoomId == snapshot.RoomId && x.IdentityKey == identityKey)
                    .Sum(x => x.Bins);
                return explicitBins <= item.CurrentBins
                    ? null
                    : new TreatmentLineageReadinessIssue(
                        "TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY",
                        snapshot.Facility,
                        snapshot.RoomId,
                        snapshot.Room,
                        snapshot.CropYear,
                        snapshot.GrowerNumber,
                        snapshot.Lot,
                        snapshot.Variety,
                        item.CurrentBins,
                        explicitBins,
                        explicitBins - item.CurrentBins,
                        identityKey);
            })
            .Where(x => x is not null)
            .Cast<TreatmentLineageReadinessIssue>()
            .OrderBy(x => x.Facility)
            .ThenBy(x => x.RoomId)
            .ThenBy(x => x.IdentityKey)
            .ToList();
        var reported = issues.Take(MaximumReportedIssues).ToList();
        return issues.Count == 0
            ? new(true, authoritative.Count, 0, [],
                $"Treatment-lineage readiness passed for {authoritative.Count} current inventory identities; no explicit lineage exceeds authoritative inventory.")
            : new(false, authoritative.Count, issues.Count, reported,
                $"Treatment-lineage readiness failed: {issues.Count} current inventory identity/identities exceed authoritative inventory. "
                + $"Reporting the first {reported.Count} bounded issue(s).");
    }
}

public sealed class TreatmentLineage144CorrectionService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    ILogger<TreatmentLineage144CorrectionService> logger) : ITreatmentLineage144CorrectionService
{
    private const long SegmentId = 144;
    private const long MovementId = 203;
    private const long FirstEntryId = 188;
    private const long SecondEntryId = 189;
    private static readonly long[] RemainingSegmentIds = [175, 176, 180, 184];
    private static readonly long[] ReceiptIds = [927, 930, 938, 944];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TreatmentLineage144CorrectionPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var segment = await dbContext.TreatmentLineageSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == SegmentId, cancellationToken);
        var movement = await dbContext.TreatmentLineageMovements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == MovementId, cancellationToken);
        var movementReversalCount = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .CountAsync(x => x.ReversesTreatmentLineageMovementId == MovementId, cancellationToken);
        var firstEntry = await dbContext.BinsRunEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == FirstEntryId, cancellationToken);
        var secondEntry = await dbContext.BinsRunEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == SecondEntryId, cancellationToken);
        var remaining = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => RemainingSegmentIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Where(x => ReceiptIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var audits = await dbContext.AuditLogs.AsNoTracking()
            .Where(x => x.EntityName == TreatmentLineage144CorrectionConstants.AuditEntityName
                && x.EntityKey == TreatmentLineage144CorrectionConstants.AuditEntityKey
                && x.SourceApplication == TreatmentLineage144CorrectionConstants.AuditSource)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

        ValidateSegment(segment, issues);
        ValidateMovement(movement, movementReversalCount, issues);
        ValidateEntry(firstEntry, FirstEntryId, 225, 132, 93, TreatmentLineageStates.Untreated, "u", null, issues);
        ValidateEntry(secondEntry, SecondEntryId, 93, 1, 92, TreatmentLineageStates.Confirmed, "u|a:12", "MCP", issues);
        ValidateRemaining(remaining, issues);
        ValidateReceipts(receipts, issues);

        var authoritativeBins = await AuthoritativeBinsAsync(cancellationToken);
        if (authoritativeBins != 92) issues.Add($"Authoritative current inventory is {authoritativeBins}; expected exactly 92 bins.");
        var remainingBins = remaining.Sum(x => x.CurrentBins);
        var explicitBins = remainingBins + (segment?.CurrentBins ?? 0);
        var ready = segment?.CurrentBins == 132;
        var alreadyApplied = segment?.CurrentBins == 0;
        if (ready && audits.Count != 0) issues.Add("A correction audit exists while segment #144 still contains 132 bins.");
        if (alreadyApplied) ValidateAppliedAudit(audits, issues);
        if (!ready && !alreadyApplied && segment is not null)
            issues.Add($"Segment #144 has conflicting CurrentBins={segment.CurrentBins}; only 132 or exact applied state 0 is accepted.");
        if (ready && explicitBins != 224) issues.Add($"Pre-correction explicit lineage is {explicitBins}; expected exactly 224 bins.");
        if (alreadyApplied && explicitBins != 92) issues.Add($"Applied explicit lineage is {explicitBins}; expected exactly 92 bins.");

        TreatmentLineage144CorrectionEvidence? evidence = null;
        if (segment is not null)
        {
            evidence = new(
                segment.CurrentBins,
                segment.ConcurrencyVersion,
                remainingBins,
                explicitBins,
                authoritativeBins,
                audits.Count,
                await dbContext.RoomInventoryAdjustments.LongCountAsync(cancellationToken),
                await dbContext.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount, cancellationToken),
                await dbContext.BinsRunEntries.LongCountAsync(cancellationToken),
                await dbContext.BinsRunEntries.SumAsync(x => (long)x.BinsRun, cancellationToken),
                await dbContext.Receipts.LongCountAsync(cancellationToken),
                await dbContext.Receipts.SumAsync(x => (long)x.BinCount, cancellationToken),
                await dbContext.TreatmentLineageMovements.LongCountAsync(cancellationToken),
                await dbContext.TreatmentLineageMovements.SumAsync(x => (long)x.BinCount, cancellationToken));
        }

        var targetFingerprint = Sha256(JsonSerializer.Serialize(new
        {
            SegmentBins = segment?.CurrentBins,
            SegmentConcurrencyVersion = segment?.ConcurrencyVersion,
            SegmentUpdatedAt = segment?.UpdatedAt,
            AuditIds = audits.Select(x => x.Id).ToArray()
        }));
        var protectedFingerprint = Sha256(JsonSerializer.Serialize(new
        {
            Segment = segment is null ? null : new
            {
                segment.Id,
                segment.WarehouseId,
                segment.RoomId,
                segment.CropYear,
                segment.GrowerLotId,
                segment.FruitProfileId,
                segment.IdentityKey,
                segment.GrowerNumberSnapshot,
                segment.LotNumberSnapshot,
                segment.VarietyCodeSnapshot,
                segment.ProductionTypeSnapshot,
                segment.IsOrganicSnapshot,
                segment.ReceiptId,
                segment.TreatmentState,
                segment.TreatmentSignature,
                segment.CreatedAt
            },
            Movement = movement is null ? null : new
            {
                movement.Id,
                movement.MovementType,
                movement.BinsRunEntryId,
                movement.SourceSegmentId,
                movement.DestinationSegmentId,
                movement.SourceRoomId,
                movement.IdentityKey,
                movement.TreatmentStateSnapshot,
                movement.TreatmentSignatureSnapshot,
                movement.BinCount,
                movement.ReversesTreatmentLineageMovementId,
                ReversalCount = movementReversalCount
            },
            FirstEntry = EntryFingerprint(firstEntry),
            SecondEntry = EntryFingerprint(secondEntry),
            Remaining = remaining.Select(x => new { x.Id, x.ReceiptId, x.CurrentBins, x.TreatmentState, x.TreatmentSignature }).ToArray(),
            Receipts = receipts.Select(x => new { x.Id, x.CompuTechReceiptId, x.BinCount, x.GrowerNumber, x.FruitProfileId, x.RoomId, x.WarehouseId, x.IsDeleted }).ToArray(),
            AuthoritativeBins = authoritativeBins,
            Quantities = evidence is null ? null : new
            {
                evidence.RoomInventoryAdjustmentCount,
                evidence.RoomInventoryAdjustmentQuantity,
                evidence.BinsRunEntryCount,
                evidence.BinsRunQuantity,
                evidence.ReceiptCount,
                evidence.ReceiptQuantity,
                evidence.MovementCount,
                evidence.MovementQuantity
            }
        }));
        var state = issues.Count != 0 ? "Refused" : alreadyApplied ? "AlreadyApplied" : "Ready";
        return new(state, businessTime.UtcNow, targetFingerprint, protectedFingerprint, issues, evidence);
    }

    public async Task<TreatmentLineage144CorrectionResult> RunAsync(
        TreatmentLineage144CorrectionRequest request,
        CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused") return Failed("Preflight refused the segment #144 correction. No data was changed.", preflight);
        if (preflight.State == "AlreadyApplied")
            return new(true, false, true, "The exact reviewed segment #144 correction is already applied; zero writes were made.", preflight);
        if (!request.Apply) return new(true, false, false, "Dry-run passed for the exact reviewed segment #144 correction.", preflight);
        if (request.AuthorizationToken != TreatmentLineage144CorrectionConstants.ApplyAuthorizationToken)
            return Failed("Apply requires the exact reviewed authorization token.", preflight);
        if (appEnvironment.IsProduction && !request.ConfirmProduction)
            return Failed("Production apply requires --confirm-production.", preflight);
        if (!appEnvironment.IsProduction && !request.ConfirmDisposableRestore)
            return Failed("Non-production apply requires --confirm-disposable-restore.", preflight);
        if (request.VerifiedBackupRunId is null || string.IsNullOrWhiteSpace(request.VerifiedBackupPackageSha256))
            return Failed("Apply requires the exact fully verified backup run ID and package SHA-256.", preflight);
        if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("Apply requires a correction reason.", preflight);
        if (!Same(request.ExpectedTargetFingerprint, preflight.TargetFingerprint)
            || !Same(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint))
            return Failed("The reviewed target or protected fingerprint does not match current database state.", preflight);

        var backup = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId, cancellationToken);
        if (backup is null || backup.Status != BackupRunStatuses.Succeeded || backup.VerifiedAt is null
            || backup.RetentionProcessedAt is null || backup.LeaseReleasedAt is null || backup.PrunedAt is not null
            || !Same(backup.Sha256, request.VerifiedBackupPackageSha256))
            return Failed("Apply requires the exact fully verified retained backup.", preflight);

        var admin = await dbContext.Users.SingleOrDefaultAsync(
            x => x.Email == request.RequestedByEmail && x.IsActive
                && x.UserRoles.Any(link => link.Role.IsActive && link.Role.Name == BuiltInRoleNames.Admin),
            cancellationToken);
        if (admin is null) return Failed("The correction actor is not an active built-in Admin.", preflight);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var locked = await PreflightAsync(cancellationToken);
            if (locked.State != "Ready" || locked.TargetFingerprint != preflight.TargetFingerprint
                || locked.ProtectedFingerprint != preflight.ProtectedFingerprint)
                throw new InvalidOperationException("Database state changed after reviewed preflight.");
            var now = businessTime.UtcNow;
            int updated;
            if (dbContext.Database.IsRelational())
            {
                updated = await dbContext.TreatmentLineageSegments
                    .Where(x => x.Id == SegmentId && x.CurrentBins == 132)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.CurrentBins, 0)
                        .SetProperty(x => x.UpdatedAt, now)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), cancellationToken);
            }
            else
            {
                var target = await dbContext.TreatmentLineageSegments.SingleAsync(x => x.Id == SegmentId, cancellationToken);
                target.CurrentBins = 0;
                target.UpdatedAt = now;
                target.ConcurrencyVersion++;
                updated = 1;
            }
            if (updated != 1) throw new InvalidOperationException("The exact segment #144 target was not updated once.");
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = admin.Id,
                Action = "ReviewedProductionCorrection",
                EntityName = TreatmentLineage144CorrectionConstants.AuditEntityName,
                EntityKey = TreatmentLineage144CorrectionConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    TreatmentLineageSegmentId = SegmentId,
                    CurrentBins = 132,
                    preflight.TargetFingerprint,
                    preflight.ProtectedFingerprint,
                    BackupRunId = request.VerifiedBackupRunId,
                    BackupPackageSha256 = request.VerifiedBackupPackageSha256
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    TreatmentLineageSegmentId = SegmentId,
                    CurrentBins = 0,
                    AuthoritativeInventoryBins = 92,
                    RemainingExplicitLineageBins = 92,
                    BinsRunEntryIds = new[] { FirstEntryId, SecondEntryId },
                    TreatmentLineageMovementIds = new[] { MovementId, 204L },
                    InventoryQuantityDelta = 0,
                    ReceiptQuantityDelta = 0,
                    MovementHistoryChanged = false,
                    CorrectionAdministrator = admin.Email,
                    Reason = request.Reason.Trim(),
                    RootCause = "A second Actual Run line rematerialized 132 untreated bins because it passed the stale pre-run CurrentBins value instead of the running canonical balance.",
                    Semantics = "One active lineage-segment quantity correction backed by immutable Bins Run movement #203; Room inventory, Receipts, Actual Run #56, and movement history remain unchanged."
                }, JsonOptions),
                SourceApplication = TreatmentLineage144CorrectionConstants.AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied" || postflight.ProtectedFingerprint != preflight.ProtectedFingerprint
                || postflight.Evidence?.AuditCount != 1)
                throw new InvalidOperationException("Focused post-apply verification failed.");
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Reviewed treatment-lineage segment #144 correction applied. BackupRunId={BackupRunId} Admin={Admin}", request.VerifiedBackupRunId, admin.Email);
            return new(true, true, false, "The exact reviewed segment #144 correction completed successfully.", postflight);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "The segment #144 correction failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("The correction failed and was rolled back. No partial change was retained.", await PreflightAsync(cancellationToken));
        }
    }

    private async Task<int> AuthoritativeBinsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await ledger.GetSnapshotsAsync(1, [8], cancellationToken);
        return snapshots.Where(x => x.CurrentBins > 0
                && string.Equals(RoomTreatmentService.IdentityKey(x), TreatmentLineage144CorrectionConstants.IdentityKey, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Sum(x => x.CurrentBins);
    }

    private static void ValidateSegment(TreatmentLineageSegment? x, List<string> issues)
    {
        if (x is null) { issues.Add("TreatmentLineageSegment #144 is missing."); return; }
        if (x.WarehouseId != 1 || x.RoomId != 8 || x.CropYear != 2026 || x.GrowerLotId != 98 || x.FruitProfileId != 2
            || x.IdentityKey != TreatmentLineage144CorrectionConstants.IdentityKey || x.GrowerNumberSnapshot != "9100"
            || x.LotNumberSnapshot != "9100" || x.VarietyCodeSnapshot != "GALA" || x.ProductionTypeSnapshot != "Conventional"
            || x.IsOrganicSnapshot != false || x.ReceiptId is not null || x.TreatmentState != TreatmentLineageStates.Untreated
            || x.TreatmentSignature != "u")
            issues.Add("TreatmentLineageSegment #144 no longer matches the exact reviewed untreated identity evidence.");
    }

    private static void ValidateMovement(TreatmentLineageMovement? x, int reversalCount, List<string> issues)
    {
        if (x is null) { issues.Add("TreatmentLineageMovement #203 is missing."); return; }
        if (x.MovementType != TreatmentLineageMovementTypes.BinsRun || x.BinsRunEntryId != FirstEntryId
            || x.SourceSegmentId != SegmentId || x.DestinationSegmentId is not null || x.SourceRoomId != 8
            || x.IdentityKey != TreatmentLineage144CorrectionConstants.IdentityKey || x.TreatmentStateSnapshot != TreatmentLineageStates.Untreated
            || x.TreatmentSignatureSnapshot != "u" || x.BinCount != 132 || x.ReversesTreatmentLineageMovementId is not null
            || reversalCount != 0)
            issues.Add("TreatmentLineageMovement #203 or its reversal state no longer matches the exact reviewed evidence.");
    }

    private static void ValidateEntry(BinsRunEntry? x, long id, int previous, int bins, int next, string state, string signature, string? summary, List<string> issues)
    {
        if (x is null) { issues.Add($"BinsRunEntry #{id} is missing."); return; }
        if (x.ActualRunId != 56 || x.ActualRunRevisionId != 62 || x.RoomId != 8 || x.GrowerLotId != 98
            || x.FruitProfileId != 2 || x.LotNumber != "9100" || x.VarietyCode != "GALA" || x.PreviousAvailableBins != previous
            || x.BinsRun != bins || x.NewAvailableBins != next || x.TreatmentStateSnapshot != state
            || x.TreatmentSignatureSnapshot != signature || (summary is not null && x.TreatmentSummarySnapshot != summary))
            issues.Add($"BinsRunEntry #{id} no longer matches the exact reviewed Actual Run #56 evidence.");
    }

    private static void ValidateRemaining(IReadOnlyList<TreatmentLineageSegment> values, List<string> issues)
    {
        var expected = new Dictionary<long, (long ReceiptId, int Bins)> { [175] = (930, 24), [176] = (927, 24), [180] = (938, 24), [184] = (944, 20) };
        if (values.Count != expected.Count || values.Any(x => !expected.TryGetValue(x.Id, out var item)
            || x.ReceiptId != item.ReceiptId || x.CurrentBins != item.Bins || x.TreatmentState != TreatmentLineageStates.Confirmed))
            issues.Add("The four reviewed positive MCP segments no longer match 24 + 24 + 24 + 20 = 92 bins.");
    }

    private static void ValidateReceipts(IReadOnlyList<Receipt> values, List<string> issues)
    {
        var expected = new Dictionary<long, (string Number, int Bins)> { [927] = ("TR109201", 24), [930] = ("TR109204", 24), [938] = ("TR109211", 24), [944] = ("TR109214", 21) };
        if (values.Count != expected.Count || values.Any(x => !expected.TryGetValue(x.Id, out var item)
            || x.CompuTechReceiptId != item.Number || x.BinCount != item.Bins || x.GrowerNumber != "9100"
            || x.FruitProfileId != 2 || x.RoomId != 8 || x.WarehouseId != 1 || x.IsDeleted))
            issues.Add("The four reviewed Receipt guards no longer match production evidence.");
    }

    private static void ValidateAppliedAudit(IReadOnlyList<AuditLog> audits, List<string> issues)
    {
        if (audits.Count != 1 || audits[0].Action != "ReviewedProductionCorrection" || !AuditHasExpectedValues(audits[0].AfterValuesJson))
            issues.Add("The applied segment #144 state does not have exactly one matching reviewed correction audit.");
    }

    private static bool AuditHasExpectedValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.GetProperty("treatmentLineageSegmentId").GetInt64() == SegmentId
                && root.GetProperty("currentBins").GetInt32() == 0
                && root.GetProperty("authoritativeInventoryBins").GetInt32() == 92
                && root.GetProperty("remainingExplicitLineageBins").GetInt32() == 92
                && root.GetProperty("inventoryQuantityDelta").GetInt32() == 0
                && root.GetProperty("receiptQuantityDelta").GetInt32() == 0
                && !root.GetProperty("movementHistoryChanged").GetBoolean();
        }
        catch (JsonException) { return false; }
        catch (KeyNotFoundException) { return false; }
    }

    private static object? EntryFingerprint(BinsRunEntry? x) => x is null ? null : new
    {
        x.Id,
        x.ActualRunId,
        x.ActualRunRevisionId,
        x.RoomId,
        x.GrowerLotId,
        x.FruitProfileId,
        x.LotNumber,
        x.VarietyCode,
        x.PreviousAvailableBins,
        x.BinsRun,
        x.NewAvailableBins,
        x.TreatmentStateSnapshot,
        x.TreatmentSignatureSnapshot,
        x.TreatmentSummarySnapshot
    };

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static TreatmentLineage144CorrectionResult Failed(string message, TreatmentLineage144CorrectionPreflight preflight) =>
        new(false, false, false, message, preflight);
}
