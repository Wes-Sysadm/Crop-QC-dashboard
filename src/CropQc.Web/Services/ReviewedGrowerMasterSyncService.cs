using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IReviewedGrowerMasterSyncService
{
    Task<ReviewedGrowerMasterSyncPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<ReviewedGrowerMasterSyncResult> RunAsync(ReviewedGrowerMasterSyncRequest request, CancellationToken cancellationToken);
}

public sealed record ReviewedGrowerMasterSyncRequest(
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

public sealed record ReviewedGrowerMasterChange(
    string GrowerNumber,
    string AuthoritativeName,
    IReadOnlyList<string> ProductionNames,
    int ReceiptCount,
    int CurrentPackableBins,
    int AdjustmentCount,
    int TransferCount,
    int LossCount,
    int BinsRunCount,
    int ActualRunReferenceCount,
    int QcSampleCount);

public sealed record ReviewedInactiveGrower(
    string GrowerNumber,
    string? RedirectToGrowerNumber,
    bool HasProductionEvidence,
    string Disposition);

public sealed record ReviewedGrowerMasterSyncPreflight(
    string State,
    DateTimeOffset GeneratedAtUtc,
    string WorkbookFileName,
    long WorkbookSizeBytes,
    string WorkbookSha256,
    string AssetSha256,
    int ReviewedRowCount,
    int ActiveRowCount,
    int InactiveRowCount,
    int ExactNameMatchCount,
    int ChangedNameCount,
    int NeverReceivedCount,
    int ProductionNumberNotInWorkbookCount,
    IReadOnlyList<string> ProductionNumbersNotInWorkbook,
    IReadOnlyList<ReviewedGrowerMasterChange> ChangedNames,
    IReadOnlyList<ReviewedInactiveGrower> InactiveRows,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues);

public sealed record ReviewedGrowerMasterSyncResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    int CanonicalGrowersCreated,
    int CanonicalGrowersUpdated,
    int NumberMappingsCreated,
    int AliasesCreated,
    ReviewedGrowerMasterSyncPreflight Preflight);

public static class ReviewedGrowerMasterSyncConstants
{
    public const string CommandName = "--sync-reviewed-grower-master";
    public const string ApplyAuthorizationToken = "APPLY_REVIEWED_GROWER_MASTER_2026_08_13";
    public const string AuditEntityName = "ReviewedGrowerMasterSync";
    public const string AuditEntityKey = ReviewedGrowerMasterConstants.AssetSha256;
    public const long VerifiedRestoreBackupRunId = 69;
    public const string VerifiedRestorePackageSha256 = "581c9029873df923a5df6d6915762e057ff2d901f39f49a314abe30621364dc2";
}

public sealed class ReviewedGrowerMasterSyncService(
    CropQcDbContext dbContext,
    IReviewedGrowerMasterSource source,
    AppEnvironmentOptions appEnvironment,
    ILogger<ReviewedGrowerMasterSyncService> logger) : IReviewedGrowerMasterSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ReviewedGrowerMasterSyncPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var master = await source.LoadAsync(cancellationToken);
        var activeRows = master.Rows.Where(x => x.IsActive).ToList();
        var activeNumbers = activeRows.Select(x => x.GrowerNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allNumbers = master.Rows.Select(x => x.GrowerNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();

        var canonicalGrowers = await dbContext.CanonicalGrowers.AsNoTracking()
            .Include(x => x.GrowerNumbers)
            .Include(x => x.Aliases)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var numberOwners = canonicalGrowers
            .SelectMany(grower => grower.GrowerNumbers.Select(number => new { Grower = grower, Number = number }))
            .GroupBy(x => CanonicalGrowerService.NormalizeGrowerNumber(x.Number.GrowerNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in activeRows)
        {
            var owners = numberOwners.GetValueOrDefault(row.GrowerNumber) ?? [];
            if (owners.Count > 1)
            {
                issues.Add($"Grower number {row.GrowerNumber} has multiple canonical owners.");
            }
            if (owners.Count == 1)
            {
                var reviewedOwnedNumbers = owners[0].Grower.GrowerNumbers
                    .Where(x => x.IsActive && activeNumbers.Contains(CanonicalGrowerService.NormalizeGrowerNumber(x.GrowerNumber)))
                    .Select(x => CanonicalGrowerService.NormalizeGrowerNumber(x.GrowerNumber))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (reviewedOwnedNumbers.Count > 1)
                {
                    issues.Add($"Canonical grower {owners[0].Grower.Id} merges reviewed grower number {row.GrowerNumber} with another reviewed number.");
                }
            }
        }

        var receipts = await dbContext.Receipts.AsNoTracking()
            .Select(x => new { x.Id, Number = x.GrowerNumber ?? x.LotCode, x.GrowerName, x.BinCount, x.IsDeleted })
            .ToListAsync(cancellationToken);
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Select(x => new { x.Id, Number = x.LotNumber, x.GrowerName, x.ReceiptId, x.ChangeAmount, x.NewBinCount })
            .ToListAsync(cancellationToken);
        var transfers = await dbContext.RoomTransfers.AsNoTracking()
            .Select(x => new { x.Id, Number = x.LotNumber, x.GrowerName })
            .ToListAsync(cancellationToken);
        var losses = await dbContext.RoomInventoryLosses.AsNoTracking()
            .Select(x => new { x.Id, Number = x.GrowerNumber ?? x.LotNumber, x.GrowerName })
            .ToListAsync(cancellationToken);
        var binsRuns = await dbContext.BinsRunEntries.AsNoTracking()
            .Select(x => new { x.Id, Number = x.GrowerNumberSnapshot ?? x.LotNumber, x.GrowerName, x.ActualRunId })
            .ToListAsync(cancellationToken);
        var fieldSamples = await dbContext.QcSamples.AsNoTracking()
            .Select(x => new { x.Id, x.ReceiptId, Number = x.FieldSampleGrowerNumber, Name = x.FieldSampleGrowerName })
            .ToListAsync(cancellationToken);

        static string Number(string? value) => CanonicalGrowerService.NormalizeGrowerNumber(value);
        var evidenceNumbers = receipts.Select(x => Number(x.Number))
            .Concat(adjustments.Select(x => Number(x.Number)))
            .Concat(transfers.Select(x => Number(x.Number)))
            .Concat(losses.Select(x => Number(x.Number)))
            .Concat(binsRuns.Select(x => Number(x.Number)))
            .Concat(fieldSamples.Select(x => Number(x.Number)))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inactive = master.Rows.Where(x => !x.IsActive).Select(row =>
        {
            var hasEvidence = evidenceNumbers.Contains(row.GrowerNumber);
            if (hasEvidence)
            {
                issues.Add($"Inactive grower number {row.GrowerNumber} has production evidence and requires separate manual review.");
            }
            var disposition = row.RedirectToGrowerNumber is null
                ? "Skipped; historical identity is preserved and no literal INACTIVE name is created."
                : "Redirect skipped; production evidence does not prove redirect semantics."
                    + " Historical identity is preserved.";
            return new ReviewedInactiveGrower(row.GrowerNumber, row.RedirectToGrowerNumber, hasEvidence, disposition);
        }).ToList();

        var changedNames = new List<ReviewedGrowerMasterChange>();
        var exactMatchCount = 0;
        var neverReceivedCount = 0;
        foreach (var row in activeRows)
        {
            var rowReceipts = receipts.Where(x => Number(x.Number) == row.GrowerNumber).ToList();
            var rowAdjustments = adjustments.Where(x => Number(x.Number) == row.GrowerNumber).ToList();
            var rowTransfers = transfers.Where(x => Number(x.Number) == row.GrowerNumber).ToList();
            var rowLosses = losses.Where(x => Number(x.Number) == row.GrowerNumber).ToList();
            var rowBinsRuns = binsRuns.Where(x => Number(x.Number) == row.GrowerNumber).ToList();
            var rowFieldSamples = fieldSamples.Where(x => Number(x.Number) == row.GrowerNumber).ToList();
            var names = rowReceipts.Select(x => x.GrowerName)
                .Concat(rowAdjustments.Select(x => x.GrowerName))
                .Concat(rowTransfers.Select(x => x.GrowerName))
                .Concat(rowLosses.Select(x => x.GrowerName))
                .Concat(rowBinsRuns.Select(x => x.GrowerName))
                .Concat(rowFieldSamples.Select(x => x.Name ?? ""))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rowReceipts.Count == 0) neverReceivedCount++;
            if (rowReceipts.Count > 0 && rowReceipts.All(x => x.GrowerName.Trim().Equals(row.GrowerName, StringComparison.OrdinalIgnoreCase))) exactMatchCount++;
            if (names.Any(x => !x.Equals(row.GrowerName, StringComparison.Ordinal)))
            {
                var currentPackableBins = Math.Max(0, rowAdjustments.Sum(x => x.ChangeAmount));
                changedNames.Add(new(
                    row.GrowerNumber,
                    row.GrowerName,
                    names,
                    rowReceipts.Count,
                    currentPackableBins,
                    rowAdjustments.Count,
                    rowTransfers.Count,
                    rowLosses.Count,
                    rowBinsRuns.Count,
                    rowBinsRuns.Count(x => x.ActualRunId != null),
                    rowFieldSamples.Count + fieldSamples.Count(x => x.ReceiptId != null && rowReceipts.Any(r => r.Id == x.ReceiptId))));
            }
        }

        var productionNumbersNotInWorkbook = evidenceNumbers.Where(x => !allNumbers.Contains(x)).OrderBy(x => x).ToList();
        var auditExists = await dbContext.AuditLogs.AsNoTracking().AnyAsync(
            x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName
                && x.EntityKey == ReviewedGrowerMasterSyncConstants.AuditEntityKey,
            cancellationToken);
        var complete = activeRows.All(row =>
        {
            var owners = numberOwners.GetValueOrDefault(row.GrowerNumber) ?? [];
            return owners.Count == 1
                && owners[0].Number.IsActive
                && owners[0].Grower.IsActive
                && owners[0].Grower.MergedIntoCanonicalGrowerId is null
                && owners[0].Grower.DisplayName.Equals(row.GrowerName, StringComparison.Ordinal)
                && owners[0].Grower.NormalizedKey.Equals(CanonicalKey(row.GrowerNumber), StringComparison.Ordinal);
        }) && auditExists;

        var targetFingerprint = Sha256(JsonSerializer.Serialize(new
        {
            master.WorkbookSha256,
            master.AssetSha256,
            Rows = master.Rows,
            Canonical = canonicalGrowers.OrderBy(x => x.Id).Select(x => new
            {
                x.Id,
                x.DisplayName,
                x.NormalizedKey,
                x.IsActive,
                x.MergedIntoCanonicalGrowerId,
                Numbers = x.GrowerNumbers.OrderBy(y => y.Id).Select(y => new { y.Id, y.GrowerNumber, y.NormalizedGrowerNumber, y.IsActive, y.SourceSystem }),
                Aliases = x.Aliases.OrderBy(y => y.Id).Select(y => new { y.Id, y.AliasName, y.NormalizedAliasKey, y.IsActive, y.SourceSystem })
            }),
            Evidence = changedNames
        }));
        var protectedFingerprint = await CaptureProtectedFingerprintAsync(cancellationToken);
        return new(
            issues.Count > 0 ? "Refused" : complete ? "AlreadyApplied" : "Ready",
            DateTimeOffset.UtcNow,
            master.WorkbookFileName,
            master.WorkbookSizeBytes,
            master.WorkbookSha256,
            master.AssetSha256,
            master.Rows.Count,
            activeRows.Count,
            inactive.Count,
            exactMatchCount,
            changedNames.Count,
            neverReceivedCount,
            productionNumbersNotInWorkbook.Count,
            productionNumbersNotInWorkbook,
            changedNames,
            inactive,
            targetFingerprint,
            protectedFingerprint,
            issues.Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task<ReviewedGrowerMasterSyncResult> RunAsync(ReviewedGrowerMasterSyncRequest request, CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused") return Failed("Preflight refused the reviewed grower sync; no data was changed.", preflight);
        if (preflight.State == "AlreadyApplied") return new(true, false, true, "The exact reviewed grower master is already applied; zero writes were made.", 0, 0, 0, 0, preflight);
        if (!request.Apply) return new(true, false, false, "Dry-run passed. No data was changed.", 0, 0, 0, 0, preflight);
        if (!string.Equals(request.AuthorizationToken, ReviewedGrowerMasterSyncConstants.ApplyAuthorizationToken, StringComparison.Ordinal)) return Failed("Apply requires the exact reviewed authorization token.", preflight);
        if (appEnvironment.IsProduction && !request.ConfirmProduction) return Failed("Production apply requires --confirm-production.", preflight);
        if (!appEnvironment.IsProduction && !request.ConfirmDisposableRestore) return Failed("Non-production rehearsal requires --confirm-disposable-restore.", preflight);
        if (request.VerifiedBackupRunId is null) return Failed("Apply requires an explicit verified backup run ID.", preflight);
        if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("Apply requires a reason.", preflight);
        if (!string.Equals(request.ExpectedTargetFingerprint, preflight.TargetFingerprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.OrdinalIgnoreCase))
            return Failed("The reviewed target or protected-data fingerprint does not match current database state.", preflight);

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
            && request.VerifiedBackupRunId == ReviewedGrowerMasterSyncConstants.VerifiedRestoreBackupRunId
            && string.Equals(request.VerifiedBackupPackageSha256, ReviewedGrowerMasterSyncConstants.VerifiedRestorePackageSha256, StringComparison.OrdinalIgnoreCase)
            && backup is not null;
        if (!databaseVerified && !restoredCopyAttested) return Failed("The backup is not a fresh, fully verified successful production backup, or the exact reviewed disposable-restore attestation is missing.", preflight);

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

            var master = await source.LoadAsync(cancellationToken);
            var allGrowers = await dbContext.CanonicalGrowers
                .Include(x => x.GrowerNumbers)
                .Include(x => x.Aliases)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var observedNames = await LoadObservedNamesAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var growersCreated = 0;
            var growersUpdated = 0;
            var numbersCreated = 0;
            var aliasesCreated = 0;
            foreach (var row in master.Rows.Where(x => x.IsActive))
            {
                var number = row.GrowerNumber;
                var owner = allGrowers.SingleOrDefault(x => x.GrowerNumbers.Any(y => CanonicalGrowerService.NormalizeGrowerNumber(y.GrowerNumber) == number));
                if (owner is null)
                {
                    owner = new CanonicalGrower
                    {
                        DisplayName = row.GrowerName,
                        NormalizedKey = CanonicalKey(number),
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    dbContext.CanonicalGrowers.Add(owner);
                    allGrowers.Add(owner);
                    growersCreated++;
                }
                else
                {
                    if (!owner.DisplayName.Equals(row.GrowerName, StringComparison.Ordinal)
                        || !owner.NormalizedKey.Equals(CanonicalKey(number), StringComparison.Ordinal)
                        || !owner.IsActive
                        || owner.MergedIntoCanonicalGrowerId is not null)
                    {
                        owner.DisplayName = row.GrowerName;
                        owner.NormalizedKey = CanonicalKey(number);
                        owner.IsActive = true;
                        owner.MergedIntoCanonicalGrowerId = null;
                        owner.UpdatedAt = now;
                        growersUpdated++;
                    }
                }

                var numberMapping = owner.GrowerNumbers.SingleOrDefault(x => CanonicalGrowerService.NormalizeGrowerNumber(x.GrowerNumber) == number);
                if (numberMapping is null)
                {
                    owner.GrowerNumbers.Add(new CanonicalGrowerNumber
                    {
                        GrowerNumber = number,
                        NormalizedGrowerNumber = number,
                        SourceSystem = ReviewedGrowerMasterConstants.SourceSystem,
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    numbersCreated++;
                }
                else
                {
                    numberMapping.GrowerNumber = number;
                    numberMapping.NormalizedGrowerNumber = number;
                    numberMapping.SourceSystem = ReviewedGrowerMasterConstants.SourceSystem;
                    numberMapping.IsActive = true;
                    numberMapping.UpdatedAt = now;
                }

                var aliases = new[] { row.GrowerName }.Concat(observedNames.GetValueOrDefault(number) ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var aliasName in aliases)
                {
                    var key = CanonicalGrowerService.NormalizeGrowerKey(aliasName);
                    var alias = owner.Aliases.SingleOrDefault(x => x.NormalizedAliasKey == key);
                    if (alias is null)
                    {
                        owner.Aliases.Add(new CanonicalGrowerAlias
                        {
                            AliasName = aliasName,
                            NormalizedAliasKey = key,
                            SourceSystem = ReviewedGrowerMasterConstants.SourceSystem,
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                        aliasesCreated++;
                    }
                    else
                    {
                        alias.IsActive = true;
                        alias.UpdatedAt = now;
                    }
                }
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = admin.Id,
                Action = "ReviewedMasterSync",
                EntityName = ReviewedGrowerMasterSyncConstants.AuditEntityName,
                EntityKey = ReviewedGrowerMasterSyncConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    preflight.TargetFingerprint,
                    preflight.ProtectedFingerprint,
                    BackupRunId = backup!.Id,
                    BackupVerification = databaseVerified ? "DatabaseRecord" : "VerifiedDisposableRestorePackageAttestation",
                    request.VerifiedBackupPackageSha256,
                    preflight.ChangedNames,
                    preflight.InactiveRows,
                    preflight.ProductionNumbersNotInWorkbook
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    Workbook = ReviewedGrowerMasterConstants.WorkbookFileName,
                    ReviewedGrowerMasterConstants.WorkbookSha256,
                    ReviewedGrowerMasterConstants.AssetSha256,
                    ActiveMappings = ReviewedGrowerMasterConstants.ExpectedActiveCount,
                    growersCreated,
                    growersUpdated,
                    numbersCreated,
                    aliasesCreated,
                    RequestedBy = admin.Email,
                    Reason = request.Reason.Trim(),
                    HistoricalOperationalRowsChanged = 0,
                    InactiveRowsChanged = 0
                }, JsonOptions),
                SourceApplication = "CropQc.Web reviewed grower master command",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var protectedAfter = await CaptureProtectedFingerprintAsync(cancellationToken);
            if (protectedAfter != preflight.ProtectedFingerprint) throw new InvalidOperationException("Protected operational data changed during the sync.");
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied") throw new InvalidOperationException("Post-apply verification did not recognize the exact reviewed state.");
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Reviewed grower master applied by {Admin}; {Growers} growers and {Numbers} number mappings created.", admin.Email, growersCreated, numbersCreated);
            return new(true, true, false, "The reviewed grower master sync completed successfully.", growersCreated, growersUpdated, numbersCreated, aliasesCreated, postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Reviewed grower master sync failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("The sync failed and was rolled back. Review restricted logs.", await PreflightAsync(cancellationToken));
        }
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadObservedNamesAsync(CancellationToken cancellationToken)
    {
        var evidence = new List<(string Number, string Name)>();
        evidence.AddRange((await dbContext.Receipts.AsNoTracking().Select(x => new { Number = x.GrowerNumber ?? x.LotCode, x.GrowerName }).ToListAsync(cancellationToken)).Select(x => (x.Number, x.GrowerName)));
        evidence.AddRange((await dbContext.RoomInventoryAdjustments.AsNoTracking().Select(x => new { Number = x.LotNumber, x.GrowerName }).ToListAsync(cancellationToken)).Select(x => (x.Number, x.GrowerName)));
        evidence.AddRange((await dbContext.RoomTransfers.AsNoTracking().Select(x => new { Number = x.LotNumber, x.GrowerName }).ToListAsync(cancellationToken)).Select(x => (x.Number, x.GrowerName)));
        evidence.AddRange((await dbContext.RoomInventoryLosses.AsNoTracking().Select(x => new { Number = x.GrowerNumber ?? x.LotNumber, x.GrowerName }).ToListAsync(cancellationToken)).Select(x => (x.Number, x.GrowerName)));
        evidence.AddRange((await dbContext.BinsRunEntries.AsNoTracking().Select(x => new { Number = x.GrowerNumberSnapshot ?? x.LotNumber, x.GrowerName }).ToListAsync(cancellationToken)).Select(x => (x.Number, x.GrowerName)));
        return evidence.Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => CanonicalGrowerService.NormalizeGrowerNumber(x.Number), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(y => y.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> CaptureProtectedFingerprintAsync(CancellationToken cancellationToken)
    {
        var snapshot = new
        {
            Migrations = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).OrderBy(x => x).ToList(),
            Receipts = await dbContext.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CropYear, x.ReceivedAt, x.CompuTechReceiptId, x.WarehouseId, x.RoomId, x.FruitProfileId, x.GrowerLotId, x.GrowerNumber, x.GrowerName, x.LotCode, x.BinCount, x.IsDeleted }).ToListAsync(cancellationToken),
            Adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.RoomId, x.GrowerLotId, x.FruitProfileId, x.GrowerName, x.LotNumber, x.ChangeAmount, x.NewBinCount, x.AdjustmentType, x.RoomTransferId, x.RoomInventoryLossId, x.ActualRunId }).ToListAsync(cancellationToken),
            Losses = await dbContext.RoomInventoryLosses.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationKey, x.ReceiptId, x.RoomId, x.GrowerName, x.GrowerNumber, x.LotNumber, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            Transfers = await dbContext.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationKey, x.SourceRoomId, x.DestinationRoomId, x.GrowerName, x.LotNumber, x.BinCount, x.IsReversed }).ToListAsync(cancellationToken),
            BinsRuns = await dbContext.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.InventoryAdjustmentId, x.GrowerName, x.GrowerNumberSnapshot, x.LotNumber, x.BinsRun, x.PreviousAvailableBins, x.NewAvailableBins, x.ActualRunId, x.IsReversed }).ToListAsync(cancellationToken),
            ActualRuns = await dbContext.ActualRuns.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CurrentRevisionNumber, x.RunAt }).ToListAsync(cancellationToken),
            ActualRunRevisions = await dbContext.ActualRunRevisions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ActualRunId, x.RevisionNumber, x.OperationType, x.OperationKey, x.IsCurrent }).ToListAsync(cancellationToken),
            RunExpectations = await dbContext.RunExpectations.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ActualRunId, x.ActualRunRevisionId, x.RevisionNumber, x.TotalBins, x.GrossPounds, x.ExpectedPackoutPercent, x.ExpectedPackedPounds, x.ExpectedWholeBoxes, x.ExpectedCullPounds, x.ExpectedJuicePounds, x.ExpectedPeelerPounds, x.ExpectedWastePounds, x.ConfidencePercent, x.SizeDistributionSnapshotJson, x.GradeDistributionSnapshotJson, x.ConfigurationSnapshotJson, x.CalculationVersion, x.CalculatedAt }).ToListAsync(cancellationToken),
            RunExpectationSources = await dbContext.RunExpectationSources.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RunExpectationId, x.BinsRunEntryId, x.WarehouseId, x.RoomId, x.FacilitySnapshot, x.RoomSnapshot, x.CropYearSnapshot, x.GrowerLotId, x.FruitProfileId, x.GrowerSnapshot, x.LotSnapshot, x.VarietySnapshot, x.ProductionTypeSnapshot, x.IsOrganicSnapshot, x.BinsContributed, x.ContributionPercent, x.QcSampleId, x.QcSampleTakenAtSnapshot, x.QcFruitCountSnapshot, x.QcMeasurementSnapshotJson, x.SizeDistributionSnapshotJson, x.GradeDistributionSnapshotJson, x.GrossPounds, x.ExpectedPackedPounds, x.ExpectedWholeBoxes, x.ExpectedCullPounds, x.ConfidencePercent, x.WarningSnapshot }).ToListAsync(cancellationToken),
            GrowerLots = await dbContext.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.PoolStart, x.IsActive }).ToListAsync(cancellationToken),
            QcSamples = await dbContext.QcSamples.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.SampleTypeId, x.SampleSequenceNumber, x.Status, x.FieldSampleGrowerName, x.FieldSampleGrowerNumber, x.SampleTakenAt, x.CreatedAt, x.UpdatedAt, x.IsDeleted }).ToListAsync(cancellationToken),
            QcFruitReadings = await dbContext.QcFruitReadings.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QcSampleId, x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs, x.WeightGrams, x.GradeId, x.StarchScaleValueId, x.SizeCategory, x.SizeStatus, x.DefectsInspected, x.FieldVersion, x.IsCompleted, x.CreatedAt, x.UpdatedAt }).ToListAsync(cancellationToken),
            QcPhotos = await dbContext.QcPhotos.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReceiptId, x.QcSampleId, x.PhotoType, x.PhotoSource, x.FileName, x.ContentType, x.FileSizeBytes, x.StorageProvider, x.DriveId, x.FileId, x.FolderId, x.SharePointDriveId, x.SharePointItemId, x.CapturedAt, x.UploadedAt, x.IsDeleted }).ToListAsync(cancellationToken),
            Users = await dbContext.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Email, x.DisplayName, x.IsActive, x.EmploymentFacility }).ToListAsync(cancellationToken),
            Roles = await dbContext.Roles.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.NormalizedName, x.IsActive }).ToListAsync(cancellationToken),
            UserRoles = await dbContext.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).Select(x => new { x.UserId, x.RoleId }).ToListAsync(cancellationToken),
            RoleAccess = await dbContext.RolePageAccesses.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.AreaKey, x.AccessLevel }).ToListAsync(cancellationToken),
            Credentials = await dbContext.UserGoogleCredentials.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.Provider, x.AccessTokenEncrypted, x.RefreshTokenEncrypted, x.Scope, x.ExpiresAt }).ToListAsync(cancellationToken),
            EodGroups = await dbContext.EndOfDayFillReportGroups.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Facility, x.IsActive }).ToListAsync(cancellationToken),
            EodRecipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.EmailAddress, x.IsActive, x.SortOrder }).ToListAsync(cancellationToken),
            EodSends = await dbContext.EndOfDayFillReportSends.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReportGroupId, x.PacificReportDate, x.RevisionNumber, x.SnapshotHash, x.Status }).ToListAsync(cancellationToken)
        };
        return Sha256(JsonSerializer.Serialize(snapshot));
    }

    private static string CanonicalKey(string growerNumber) => $"REVIEWED_GROWER_NUMBER_{growerNumber}";
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static ReviewedGrowerMasterSyncResult Failed(string message, ReviewedGrowerMasterSyncPreflight preflight) => new(false, false, false, message, 0, 0, 0, 0, preflight);
}
