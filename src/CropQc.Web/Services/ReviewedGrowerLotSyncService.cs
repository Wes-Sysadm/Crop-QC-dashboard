using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IReviewedGrowerLotPolicy
{
    Task<IReadOnlyDictionary<string, ReviewedGrowerMasterRow>> GetActiveReviewedGrowersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GrowerLot>> GetAlignedActiveGrowerLotsAsync(CancellationToken cancellationToken);
}

public interface IReviewedGrowerLotSyncService
{
    Task<ReviewedGrowerLotSyncPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<ReviewedGrowerLotSyncResult> RunAsync(ReviewedGrowerLotSyncRequest request, CancellationToken cancellationToken);
}

public sealed record ReviewedGrowerLotSyncRequest(
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

public sealed record GrowerLotOperationalEvidence(
    int ReceiptCount,
    int CurrentSeasonReceiptCount,
    int CurrentPackableBins,
    int AdjustmentCount,
    int QcReferenceCount,
    int TransferCount,
    int LossCount,
    int BinsRunCount,
    int ActualRunReferenceCount,
    int RunReportingReferenceCount)
{
    public bool HasCurrentEvidence => CurrentSeasonReceiptCount > 0 || CurrentPackableBins > 0;
    public int TotalReferences => ReceiptCount + AdjustmentCount + QcReferenceCount + TransferCount + LossCount + BinsRunCount + ActualRunReferenceCount + RunReportingReferenceCount;
}

public sealed record ReviewedGrowerLotChange(
    string Action,
    int? GrowerLotId,
    string GrowerNumber,
    string? CurrentGrowerName,
    string? CurrentLotNumber,
    string AuthoritativeGrowerName,
    bool CurrentIsActive,
    string? PoolStart,
    GrowerLotOperationalEvidence Evidence,
    string Disposition);

public sealed record ReviewedGrowerLotSyncPreflight(
    string State,
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string AssetSha256,
    int ReviewedGrowerCount,
    int ReviewedActiveGrowerCount,
    int ReviewedInactiveGrowerCount,
    int GrowerLotCount,
    int ActiveGrowerLotCount,
    int InactiveGrowerLotCount,
    int AlreadyAlignedCount,
    int MissingGrowerLotsToCreate,
    int NamesToUpdate,
    int RowsToActivate,
    int RowsToDeactivate,
    int DuplicateOrConflictCount,
    int LegacyRowsPreserved,
    int ExistingPoolStartChanges,
    int HistoricalForeignKeyChanges,
    IReadOnlyList<string> AffectedGrowerNumbers,
    IReadOnlyList<ReviewedGrowerLotChange> Changes,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues);

public sealed record ReviewedGrowerLotSyncResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    int Created,
    int Updated,
    int Activated,
    int Deactivated,
    ReviewedGrowerLotSyncPreflight Preflight);

public static class ReviewedGrowerLotSyncConstants
{
    public const string CommandName = "--sync-grower-lots-with-reviewed-growers";
    public const string ApplyAuthorizationToken = "APPLY_REVIEWED_GROWER_LOT_ALIGNMENT_V1_2026_08_17";
    public const string AuditEntityName = "ReviewedGrowerLotSync";
    public const string AuditEntityKey = ReviewedGrowerMasterConstants.AssetSha256;
    public const long VerifiedRestoreBackupRunId = 81;
    public const string VerifiedRestorePackageSha256 = "360f939acf52834eed80576b020f13959bef030ca28bc0ac3fd511cd6fb29c03";
}

public sealed class ReviewedGrowerLotSyncService(
    CropQcDbContext dbContext,
    IReviewedGrowerMasterSource source,
    IRoomInventoryLedgerQueryService roomInventoryLedger,
    ICropYearService cropYearService,
    AppEnvironmentOptions appEnvironment,
    ILogger<ReviewedGrowerLotSyncService> logger) : IReviewedGrowerLotSyncService, IReviewedGrowerLotPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyDictionary<string, ReviewedGrowerMasterRow>> GetActiveReviewedGrowersAsync(CancellationToken cancellationToken)
    {
        var master = await source.LoadAsync(cancellationToken);
        return master.Rows.Where(x => x.IsActive).ToDictionary(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<GrowerLot>> GetAlignedActiveGrowerLotsAsync(CancellationToken cancellationToken)
    {
        var reviewed = await GetActiveReviewedGrowersAsync(cancellationToken);
        var lots = await dbContext.GrowerLots.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var groups = lots.GroupBy(x => CanonicalGrowerService.NormalizeGrowerNumber(x.LotNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var aligned = reviewed.Count == lots.Count
            && reviewed.All(pair => groups.TryGetValue(pair.Key, out var matches)
                && matches.Count == 1
                && matches[0].LotNumber.Equals(pair.Key, StringComparison.Ordinal)
                && matches[0].Grower.Equals(pair.Value.GrowerName, StringComparison.Ordinal));
        if (!aligned)
        {
            throw new InvalidOperationException("Active Grower Lots are not aligned with the reviewed Grower master. Run the reviewed Grower Lot sync before accepting new Receiving selections.");
        }
        return lots.OrderBy(x => x.LotNumber, StringComparer.Ordinal).ToList();
    }

    public async Task<ReviewedGrowerLotSyncPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var master = await source.LoadAsync(cancellationToken);
        var activeRows = master.Rows.Where(x => x.IsActive).ToDictionary(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase);
        var allRows = master.Rows.ToDictionary(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase);
        var lots = await dbContext.GrowerLots.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var evidence = await LoadEvidenceAsync(lots, cancellationToken);
        var groups = lots.GroupBy(x => CanonicalGrowerService.NormalizeGrowerNumber(x.LotNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var changes = new List<ReviewedGrowerLotChange>();
        var issues = new List<string>();
        var alreadyAligned = 0;

        foreach (var row in activeRows.Values.OrderBy(x => x.GrowerNumber, StringComparer.Ordinal))
        {
            var matches = groups.GetValueOrDefault(row.GrowerNumber) ?? [];
            var activeMatches = matches.Where(x => x.IsActive).ToList();
            if (activeMatches.Count > 1)
            {
                issues.Add($"Grower number {row.GrowerNumber} has {activeMatches.Count} active Grower Lots; no row was chosen.");
                changes.AddRange(activeMatches.Select(x => Change("Conflict", x, row, evidence[x.Id], "Duplicate active rows are ambiguous and must be reviewed without repointing historical references.")));
                continue;
            }
            if (activeMatches.Count == 0)
            {
                if (matches.Count == 0)
                {
                    changes.Add(new("Create", null, row.GrowerNumber, null, null, row.GrowerName, false, null, EmptyEvidence(), "Create one current Grower Lot with PoolStart unset; no historical row or FK is changed."));
                }
                else if (matches.Count == 1)
                {
                    var existing = matches[0];
                    changes.Add(Change("Activate", existing, row, evidence[existing.Id], "Reactivate the exact unambiguous row, retain its ID and PoolStart, and align its current name/number."));
                }
                else
                {
                    issues.Add($"Grower number {row.GrowerNumber} has {matches.Count} inactive historical Grower Lots; activation is ambiguous.");
                    changes.AddRange(matches.Select(x => Change("Conflict", x, row, evidence[x.Id], "Multiple historical rows prevent safe activation without choosing or repointing identity.")));
                }
                continue;
            }

            var active = activeMatches[0];
            if (!active.LotNumber.Equals(row.GrowerNumber, StringComparison.Ordinal)
                || !active.Grower.Equals(row.GrowerName, StringComparison.Ordinal))
            {
                changes.Add(Change("Update", active, row, evidence[active.Id], "Keep the existing ID and PoolStart; align only the current Grower Number and authoritative display name."));
            }
            else alreadyAligned++;
        }

        foreach (var lot in lots.Where(x => x.IsActive).OrderBy(x => x.Id))
        {
            var number = CanonicalGrowerService.NormalizeGrowerNumber(lot.LotNumber);
            if (activeRows.ContainsKey(number)) continue;
            allRows.TryGetValue(number, out var reviewed);
            var rowEvidence = evidence[lot.Id];
            var reason = reviewed is null
                ? "The active Grower Lot number is absent from the reviewed master."
                : "The reviewed master marks this Grower Number inactive.";
            if (rowEvidence.HasCurrentEvidence)
            {
                issues.Add($"Grower Lot {lot.Id} / {number} cannot be deactivated because current operational evidence exists.");
                changes.Add(new("Conflict", lot.Id, number, lot.Grower, lot.LotNumber, reviewed?.GrowerName ?? "", true, lot.PoolStart, rowEvidence, $"{reason} Current inventory or current-season Receiving evidence requires manual review."));
            }
            else
            {
                changes.Add(new("Deactivate", lot.Id, number, lot.Grower, lot.LotNumber, reviewed?.GrowerName ?? "", true, lot.PoolStart, rowEvidence, $"{reason} Preserve the row and every historical reference, but remove it from new selections."));
            }
        }

        var conflictCount = changes.Count(x => x.Action == "Conflict");
        var currentAuditExists = await dbContext.AuditLogs.AsNoTracking().AnyAsync(
            x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName
                && x.EntityKey == ReviewedGrowerLotSyncConstants.AuditEntityKey,
            cancellationToken);
        var actionable = changes.Where(x => x.Action != "Conflict").ToList();
        var complete = actionable.Count == 0 && conflictCount == 0 && currentAuditExists;
        var targetFingerprint = Sha256(JsonSerializer.Serialize(new
        {
            master.AssetSha256,
            ReviewedGrowerMasterConstants.SourceVersion,
            Reviewed = master.Rows,
            GrowerLots = lots.Select(x => new { x.Id, x.Grower, x.LotNumber, x.PoolStart, x.Notes, x.IsActive, x.CreatedAt, x.UpdatedAt }),
            Changes = changes
        }));
        var protectedFingerprint = await CaptureProtectedFingerprintAsync(cancellationToken);
        return new(
            conflictCount > 0 || issues.Count > 0 ? "Refused" : complete ? "AlreadyApplied" : "Ready",
            DateTimeOffset.UtcNow,
            ReviewedGrowerMasterConstants.SourceVersion,
            master.AssetSha256,
            master.Rows.Count,
            activeRows.Count,
            master.Rows.Count - activeRows.Count,
            lots.Count,
            lots.Count(x => x.IsActive),
            lots.Count(x => !x.IsActive),
            alreadyAligned,
            changes.Count(x => x.Action == "Create"),
            changes.Count(x => x.Action == "Update"),
            changes.Count(x => x.Action == "Activate"),
            changes.Count(x => x.Action == "Deactivate"),
            conflictCount,
            lots.Count(x => !x.IsActive && !changes.Any(y => y.GrowerLotId == x.Id && y.Action == "Activate")),
            0,
            0,
            actionable.Select(x => x.GrowerNumber).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            changes,
            targetFingerprint,
            protectedFingerprint,
            issues.Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task<ReviewedGrowerLotSyncResult> RunAsync(ReviewedGrowerLotSyncRequest request, CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused") return Failed("Preflight refused Grower Lot alignment; zero writes were made.", preflight);
        if (preflight.State == "AlreadyApplied") return new(true, false, true, "The reviewed Grower Lot alignment is already applied; zero writes were made.", 0, 0, 0, 0, preflight);
        if (!request.Apply) return new(true, false, false, "Dry-run passed. No data was changed.", 0, 0, 0, 0, preflight);
        if (!string.Equals(request.AuthorizationToken, ReviewedGrowerLotSyncConstants.ApplyAuthorizationToken, StringComparison.Ordinal)) return Failed("Apply requires the exact reviewed authorization token.", preflight);
        if (appEnvironment.IsProduction && !request.ConfirmProduction) return Failed("Production apply requires --confirm-production.", preflight);
        if (!appEnvironment.IsProduction && !request.ConfirmDisposableRestore) return Failed("Non-production rehearsal requires --confirm-disposable-restore.", preflight);
        if (request.VerifiedBackupRunId is null) return Failed("Apply requires an explicit verified backup run ID.", preflight);
        if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("Apply requires a reason.", preflight);
        if (!string.Equals(request.ExpectedTargetFingerprint, preflight.TargetFingerprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.OrdinalIgnoreCase))
            return Failed("The target or protected-data fingerprint does not match current database state.", preflight);

        var backup = await dbContext.BackupRunRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId, cancellationToken);
        var databaseVerified = backup is not null
            && backup.BackupType == BackupRunTypes.PreDeployment
            && backup.Status == BackupRunStatuses.Succeeded
            && backup.VerifiedAt is not null
            && backup.RetentionProcessedAt is not null
            && backup.LeaseReleasedAt is not null
            && backup.PrunedAt is null
            && !string.IsNullOrWhiteSpace(request.VerifiedBackupPackageSha256)
            && string.Equals(request.VerifiedBackupPackageSha256, backup.Sha256, StringComparison.OrdinalIgnoreCase)
            && backup.CompletedAt >= DateTimeOffset.UtcNow.AddHours(-24);
        var restoredCopyAttested = !appEnvironment.IsProduction
            && request.ConfirmDisposableRestore
            && request.VerifiedBackupRunId == ReviewedGrowerLotSyncConstants.VerifiedRestoreBackupRunId
            && string.Equals(request.VerifiedBackupPackageSha256, ReviewedGrowerLotSyncConstants.VerifiedRestorePackageSha256, StringComparison.OrdinalIgnoreCase)
            && backup is not null;
        if (!databaseVerified && !restoredCopyAttested) return Failed("The backup is not a fresh verified production backup, or the reviewed disposable-restore attestation is missing.", preflight);

        var admin = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == request.RequestedByEmail
            && x.IsActive
            && x.UserRoles.Any(y => y.Role.IsActive && y.Role.Name == BuiltInRoleNames.Admin), cancellationToken);
        if (admin is null) return Failed("The requested-by user is not an active built-in Admin.", preflight);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var locked = await PreflightAsync(cancellationToken);
            if (locked.State != "Ready"
                || locked.TargetFingerprint != preflight.TargetFingerprint
                || locked.ProtectedFingerprint != preflight.ProtectedFingerprint)
                throw new InvalidOperationException("Database state changed after preflight.");

            var now = DateTimeOffset.UtcNow;
            var created = 0;
            var updated = 0;
            var activated = 0;
            var deactivated = 0;
            foreach (var change in preflight.Changes.Where(x => x.Action != "Conflict"))
            {
                if (change.Action == "Create")
                {
                    dbContext.GrowerLots.Add(new GrowerLot
                    {
                        Grower = change.AuthoritativeGrowerName,
                        LotNumber = change.GrowerNumber,
                        PoolStart = null,
                        Notes = null,
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    created++;
                    continue;
                }

                var lot = await dbContext.GrowerLots.SingleAsync(x => x.Id == change.GrowerLotId, cancellationToken);
                if (change.Action is "Update" or "Activate")
                {
                    lot.Grower = change.AuthoritativeGrowerName;
                    lot.LotNumber = change.GrowerNumber;
                    lot.IsActive = true;
                    lot.UpdatedAt = now;
                    if (change.Action == "Activate") activated++;
                    else updated++;
                }
                else if (change.Action == "Deactivate")
                {
                    lot.IsActive = false;
                    lot.UpdatedAt = now;
                    deactivated++;
                }
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = admin.Id,
                Action = "ReviewedMasterSync",
                EntityName = ReviewedGrowerLotSyncConstants.AuditEntityName,
                EntityKey = ReviewedGrowerLotSyncConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    preflight.TargetFingerprint,
                    preflight.ProtectedFingerprint,
                    BackupRunId = backup!.Id,
                    BackupVerification = databaseVerified ? "DatabaseRecord" : "VerifiedDisposableRestorePackageAttestation",
                    request.VerifiedBackupPackageSha256,
                    preflight.Changes,
                    ExistingPoolStartChanges = 0,
                    HistoricalForeignKeyChanges = 0
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    ReviewedGrowerMasterConstants.SourceVersion,
                    ReviewedGrowerMasterConstants.AssetSha256,
                    ReviewedActiveGrowers = preflight.ReviewedActiveGrowerCount,
                    ActiveGrowerLots = preflight.ReviewedActiveGrowerCount,
                    created,
                    updated,
                    activated,
                    deactivated,
                    RequestedBy = admin.Email,
                    Reason = request.Reason.Trim(),
                    ExistingPoolStartChanges = 0,
                    HistoricalForeignKeyChanges = 0
                }, JsonOptions),
                SourceApplication = "CropQc.Web reviewed Grower Lot sync command",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            if (await CaptureProtectedFingerprintAsync(cancellationToken) != preflight.ProtectedFingerprint)
                throw new InvalidOperationException("Protected operational data changed during Grower Lot alignment.");
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied"
                || postflight.ActiveGrowerLotCount != postflight.ReviewedActiveGrowerCount)
                throw new InvalidOperationException("Post-apply verification did not prove exact active-set equality.");
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Reviewed Grower Lot alignment applied by {Admin}; created {Created}, updated {Updated}, activated {Activated}, deactivated {Deactivated}.", admin.Email, created, updated, activated, deactivated);
            return new(true, true, false, "Grower Lots were aligned with the reviewed Grower master.", created, updated, activated, deactivated, postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Reviewed Grower Lot alignment failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("The sync failed and was rolled back. Review restricted logs.", await PreflightAsync(cancellationToken));
        }
    }

    private async Task<Dictionary<int, GrowerLotOperationalEvidence>> LoadEvidenceAsync(IReadOnlyList<GrowerLot> lots, CancellationToken cancellationToken)
    {
        var currentCropYear = cropYearService.GetCurrentCropYear(DateTimeOffset.Now);
        var receipts = await dbContext.Receipts.AsNoTracking().Where(x => x.GrowerLotId != null)
            .GroupBy(x => x.GrowerLotId!.Value)
            .Select(x => new { Id = x.Key, Total = x.Count(), Current = x.Count(y => y.CropYear == currentCropYear && !y.IsDeleted) })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking().Where(x => x.GrowerLotId != null)
            .GroupBy(x => x.GrowerLotId!.Value).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var qc = await dbContext.QcSamples.AsNoTracking().Where(x => x.Receipt != null && x.Receipt.GrowerLotId != null)
            .GroupBy(x => x.Receipt!.GrowerLotId!.Value).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var transfers = await dbContext.RoomTransfers.AsNoTracking().Where(x => x.GrowerLotId != null)
            .GroupBy(x => x.GrowerLotId!.Value).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var losses = await dbContext.RoomInventoryLosses.AsNoTracking().Where(x => x.GrowerLotId != null)
            .GroupBy(x => x.GrowerLotId!.Value).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var binsRuns = await dbContext.BinsRunEntries.AsNoTracking().Where(x => x.GrowerLotId != null)
            .GroupBy(x => x.GrowerLotId!.Value).Select(x => new { Id = x.Key, Count = x.Count(), Actual = x.Count(y => y.ActualRunId != null) }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var reporting = await dbContext.RunExpectationSources.AsNoTracking().Where(x => x.GrowerLotId != null)
            .GroupBy(x => x.GrowerLotId!.Value).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var currentBins = (await roomInventoryLedger.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.GrowerLotId is not null && x.CurrentBins > 0)
            .GroupBy(x => x.GrowerLotId!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.CurrentBins));
        return lots.ToDictionary(x => x.Id, x => new GrowerLotOperationalEvidence(
            receipts.GetValueOrDefault(x.Id)?.Total ?? 0,
            receipts.GetValueOrDefault(x.Id)?.Current ?? 0,
            currentBins.GetValueOrDefault(x.Id),
            adjustments.GetValueOrDefault(x.Id)?.Count ?? 0,
            qc.GetValueOrDefault(x.Id)?.Count ?? 0,
            transfers.GetValueOrDefault(x.Id)?.Count ?? 0,
            losses.GetValueOrDefault(x.Id)?.Count ?? 0,
            binsRuns.GetValueOrDefault(x.Id)?.Count ?? 0,
            binsRuns.GetValueOrDefault(x.Id)?.Actual ?? 0,
            reporting.GetValueOrDefault(x.Id)?.Count ?? 0));
    }

    private async Task<string> CaptureProtectedFingerprintAsync(CancellationToken cancellationToken)
    {
        var snapshot = new
        {
            Migrations = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).OrderBy(x => x).ToList(),
            Receipts = await dbContext.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CropYear, x.ReceivedAt, x.CompuTechReceiptId, x.WarehouseId, x.RoomId, x.FruitProfileId, x.GrowerLotId, x.GrowerNumber, x.GrowerName, x.LotCode, x.BinCount, x.IsDeleted }).ToListAsync(cancellationToken),
            Adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.RoomId, x.GrowerLotId, x.FruitProfileId, x.GrowerName, x.LotNumber, x.ChangeAmount, x.NewBinCount, x.AdjustmentType, x.RoomTransferId, x.RoomInventoryLossId, x.ActualRunId }).ToListAsync(cancellationToken),
            Losses = await dbContext.RoomInventoryLosses.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationKey, x.ReceiptId, x.RoomId, x.GrowerLotId, x.GrowerName, x.GrowerNumber, x.LotNumber, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            Transfers = await dbContext.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationKey, x.SourceRoomId, x.DestinationRoomId, x.GrowerLotId, x.GrowerName, x.LotNumber, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            BinsRuns = await dbContext.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.InventoryAdjustmentId, x.GrowerLotId, x.GrowerName, x.GrowerNumberSnapshot, x.LotNumber, x.BinsRun, x.PreviousAvailableBins, x.NewAvailableBins, x.ActualRunId, x.IsReversed }).ToListAsync(cancellationToken),
            ActualRuns = await dbContext.ActualRuns.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CurrentRevisionNumber, x.RunAt }).ToListAsync(cancellationToken),
            ActualRunRevisions = await dbContext.ActualRunRevisions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ActualRunId, x.RevisionNumber, x.OperationType, x.OperationKey, x.IsCurrent }).ToListAsync(cancellationToken),
            RunExpectations = await dbContext.RunExpectations.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ActualRunId, x.ActualRunRevisionId, x.RevisionNumber, x.TotalBins, x.GrossPounds, x.ExpectedPackoutPercent, x.ExpectedPackedPounds, x.ExpectedWholeBoxes, x.ExpectedCullPounds, x.ExpectedJuicePounds, x.ExpectedPeelerPounds, x.ExpectedWastePounds, x.ConfidencePercent, x.CalculationVersion, x.CalculatedAt }).ToListAsync(cancellationToken),
            RunExpectationSources = await dbContext.RunExpectationSources.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RunExpectationId, x.BinsRunEntryId, x.WarehouseId, x.RoomId, x.GrowerLotId, x.FruitProfileId, x.GrowerSnapshot, x.LotSnapshot, x.BinsContributed, x.QcSampleId, x.GrossPounds, x.ExpectedPackedPounds }).ToListAsync(cancellationToken),
            QcSamples = await dbContext.QcSamples.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.SampleTypeId, x.Status, x.FieldSampleGrowerName, x.FieldSampleGrowerNumber, x.SampleTakenAt, x.IsDeleted }).ToListAsync(cancellationToken),
            QcFruitReadings = await dbContext.QcFruitReadings.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QcSampleId, x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs, x.WeightGrams, x.GradeId, x.StarchScaleValueId, x.SizeCategory, x.IsCompleted }).ToListAsync(cancellationToken),
            QcPhotos = await dbContext.QcPhotos.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.QcSampleId, x.PhotoType, x.PhotoSource, x.FileName, x.StorageProvider, x.FileId, x.IsDeleted }).ToListAsync(cancellationToken),
            Users = await dbContext.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Email, x.DisplayName, x.IsActive, x.EmploymentFacility }).ToListAsync(cancellationToken),
            Roles = await dbContext.Roles.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.NormalizedName, x.IsActive }).ToListAsync(cancellationToken),
            UserRoles = await dbContext.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).Select(x => new { x.UserId, x.RoleId }).ToListAsync(cancellationToken),
            RoleAccess = await dbContext.RolePageAccesses.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.AreaKey, x.AccessLevel }).ToListAsync(cancellationToken),
            Credentials = await dbContext.UserGoogleCredentials.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.Provider, x.AccessTokenEncrypted, x.RefreshTokenEncrypted, x.Scope, x.ExpiresAt }).ToListAsync(cancellationToken),
            Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, x.IsActive }).ToListAsync(cancellationToken),
            Rooms = await dbContext.Rooms.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.WarehouseId, x.Code, x.Name, x.SubLocation, x.CropQcRoomName, x.CompuTechRoomCode, x.DisplayName, x.SortOrder, x.CapacityBins, x.IsActive, x.EndOfDayFillReportGroupId }).ToListAsync(cancellationToken),
            EodGroups = await dbContext.EndOfDayFillReportGroups.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.WarehouseId, x.Name, x.Facility, x.IsActive }).ToListAsync(cancellationToken),
            EodRecipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.EmailAddress, x.NormalizedEmailAddress, x.IsActive, x.SortOrder }).ToListAsync(cancellationToken),
            EodAssignments = await dbContext.EndOfDayFillUserGroupAssignments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.ReportGroupId, x.CreatedAt, x.CreatedByUserId }).ToListAsync(cancellationToken),
            EodSends = await dbContext.EndOfDayFillReportSends.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReportGroupId, x.PacificReportDate, x.RevisionNumber, x.SnapshotHash, x.Status }).ToListAsync(cancellationToken),
            EodReservations = await dbContext.EndOfDayFillSendReservations.AsNoTracking().OrderBy(x => x.ReportGroupId).Select(x => new { x.ReportGroupId, x.PacificReportDate, x.RevisionNumber, x.SnapshotHash, x.SendAttemptId, x.CreatedAt }).ToListAsync(cancellationToken),
            // The JSON bodies can be very large on a production restore. IDs, immutable identity,
            // timestamps, and both body lengths give a practical whole-history fingerprint without
            // copying tens of thousands of audit payloads into the release process.
            HistoricalAudits = await dbContext.AuditLogs.AsNoTracking().Where(x => x.EntityName != ReviewedGrowerLotSyncConstants.AuditEntityName).OrderBy(x => x.Id).Select(x => new
            {
                x.Id,
                x.UserId,
                x.Action,
                x.EntityName,
                x.EntityKey,
                BeforeLength = x.BeforeValuesJson == null ? -1 : x.BeforeValuesJson.Length,
                AfterLength = x.AfterValuesJson == null ? -1 : x.AfterValuesJson.Length,
                x.SourceApplication,
                x.CreatedAt
            }).ToListAsync(cancellationToken)
        };
        return Sha256(JsonSerializer.Serialize(snapshot));
    }

    private static ReviewedGrowerLotChange Change(string action, GrowerLot lot, ReviewedGrowerMasterRow row, GrowerLotOperationalEvidence evidence, string disposition) =>
        new(action, lot.Id, row.GrowerNumber, lot.Grower, lot.LotNumber, row.GrowerName, lot.IsActive, lot.PoolStart, evidence, disposition);

    private static GrowerLotOperationalEvidence EmptyEvidence() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static ReviewedGrowerLotSyncResult Failed(string message, ReviewedGrowerLotSyncPreflight preflight) =>
        new(false, false, false, message, 0, 0, 0, 0, preflight);
}
