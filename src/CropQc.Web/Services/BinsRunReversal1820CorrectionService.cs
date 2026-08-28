using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class BinsRunReversal1820CorrectionConstants
{
    public const string CommandName = "--correct-bins-run-reversal-1820-inventory-status";
    public const string ApplyAuthorizationToken = "REVIEWED-BINS-RUN-REVERSAL-1820-INVENTORY-STATUS";
    public const string AuditEntityName = nameof(RoomInventoryAdjustment);
    public const string AuditEntityKey = "1820";
    public const string AuditSource = "CropQc.Web reviewed Bins Run reversal 1820 inventory-status correction";
    public const long VerifiedRestoreBackupRunId = 110;
    public const string VerifiedRestorePackageSha256 = "7c9545aa841679a5970938ae93e338f74bb1eb719930e9162137155b9a9c7c1d";
}

public sealed record BinsRunReversal1820CorrectionRequest(
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

public sealed record BinsRunReversal1820CorrectionEvidence(
    long AdjustmentId,
    string AdjustmentType,
    int ChangeAmount,
    int? OldBinCount,
    int NewBinCount,
    long? ActualRunId,
    long? ActualRunRevisionId,
    int? FruitProfileId,
    string? LotNumber,
    string? VarietyCode,
    int? CropYear,
    int WarehouseId,
    int RoomId,
    string? Source,
    int InventoryInvariantVersion,
    string? InventoryStatus,
    long ParentEntryId,
    string ParentInventoryStatus,
    long OriginalEntryId,
    string OriginalInventoryStatus,
    string FruitProfileName,
    string FruitProfileProductionType,
    bool FruitProfileIsOrganic,
    long AdjustmentCount,
    long AdjustmentChangeAmountSum,
    long BinsRunEntryCount,
    long BinsRunQuantitySum,
    long ReceiptCount,
    long ReceiptQuantitySum,
    int AuditCount);

public sealed record BinsRunReversal1820CorrectionPreflight(
    string State,
    DateTimeOffset CheckedAt,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues,
    BinsRunReversal1820CorrectionEvidence? Evidence);

public sealed record BinsRunReversal1820CorrectionResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    BinsRunReversal1820CorrectionPreflight Preflight);

public interface IBinsRunReversal1820CorrectionService
{
    Task<BinsRunReversal1820CorrectionPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<BinsRunReversal1820CorrectionResult> RunAsync(BinsRunReversal1820CorrectionRequest request, CancellationToken cancellationToken);
}

public sealed class BinsRunReversal1820CorrectionService(
    CropQcDbContext dbContext,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    ILogger<BinsRunReversal1820CorrectionService> logger) : IBinsRunReversal1820CorrectionService
{
    private const long AdjustmentId = 1820;
    private const long ParentEntryId = 164;
    private const long OriginalEntryId = 42;
    private const int FruitProfileId = 17;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record AdjustmentSnapshot(
        long Id,
        string AdjustmentType,
        int ChangeAmount,
        int? OldBinCount,
        int NewBinCount,
        long? ActualRunId,
        long? ActualRunRevisionId,
        int? FruitProfileId,
        string LotNumber,
        string? VarietyCode,
        int? CropYear,
        int WarehouseId,
        int RoomId,
        string? Source,
        int InventoryInvariantVersion,
        string? InventoryStatus);

    private sealed record EntrySnapshot(
        long Id,
        long InventoryAdjustmentId,
        long? ActualRunId,
        long? ActualRunRevisionId,
        long? ReversesBinsRunEntryId,
        string TransactionType,
        int BinsRun,
        int? FruitProfileId,
        string LotNumber,
        string? VarietyCode,
        string? InventoryStatus,
        int WarehouseId,
        int RoomId,
        int? CropYear,
        int PreviousAvailableBins,
        int NewAvailableBins,
        bool IsReversed);

    private sealed record FruitSnapshot(
        int Id,
        string VarietyCode,
        string Name,
        string ProductionType,
        bool IsOrganic);

    public async Task<BinsRunReversal1820CorrectionPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var adjustment = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.Id == AdjustmentId)
            .Select(x => new AdjustmentSnapshot(
                x.Id,
                x.AdjustmentType,
                x.ChangeAmount,
                x.OldBinCount,
                x.NewBinCount,
                x.ActualRunId,
                x.ActualRunRevisionId,
                x.FruitProfileId,
                x.LotNumber,
                x.VarietyCode,
                x.CropYear,
                x.WarehouseId,
                x.RoomId,
                x.Source,
                x.InventoryInvariantVersion,
                x.InventoryStatus))
            .SingleOrDefaultAsync(cancellationToken);
        var parent = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.Id == ParentEntryId)
            .Select(x => new EntrySnapshot(
                x.Id,
                x.InventoryAdjustmentId,
                x.ActualRunId,
                x.ActualRunRevisionId,
                x.ReversesBinsRunEntryId,
                x.TransactionType,
                x.BinsRun,
                x.FruitProfileId,
                x.LotNumber,
                x.VarietyCode,
                x.InventoryStatus,
                x.WarehouseId,
                x.RoomId,
                x.CropYear,
                x.PreviousAvailableBins,
                x.NewAvailableBins,
                x.IsReversed))
            .SingleOrDefaultAsync(cancellationToken);
        var original = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.Id == OriginalEntryId)
            .Select(x => new EntrySnapshot(
                x.Id,
                x.InventoryAdjustmentId,
                x.ActualRunId,
                x.ActualRunRevisionId,
                x.ReversesBinsRunEntryId,
                x.TransactionType,
                x.BinsRun,
                x.FruitProfileId,
                x.LotNumber,
                x.VarietyCode,
                x.InventoryStatus,
                x.WarehouseId,
                x.RoomId,
                x.CropYear,
                x.PreviousAvailableBins,
                x.NewAvailableBins,
                x.IsReversed))
            .SingleOrDefaultAsync(cancellationToken);
        var fruit = await dbContext.FruitProfiles.AsNoTracking()
            .Where(x => x.Id == FruitProfileId)
            .Select(x => new FruitSnapshot(x.Id, x.VarietyCode, x.Name, x.ProductionType, x.IsOrganic))
            .SingleOrDefaultAsync(cancellationToken);
        var audits = await dbContext.AuditLogs.AsNoTracking()
            .Where(x => x.EntityName == BinsRunReversal1820CorrectionConstants.AuditEntityName
                && x.EntityKey == BinsRunReversal1820CorrectionConstants.AuditEntityKey
                && x.SourceApplication == BinsRunReversal1820CorrectionConstants.AuditSource)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        ValidateAdjustment(adjustment, issues);
        ValidateParent(parent, issues);
        ValidateOriginal(original, issues);
        ValidateFruit(fruit, issues);

        var ready = adjustment?.InventoryStatus is null
            || (!dbContext.Database.IsRelational() && string.IsNullOrWhiteSpace(adjustment?.InventoryStatus));
        var alreadyApplied = string.Equals(adjustment?.InventoryStatus, "Conventional", StringComparison.Ordinal);
        if (ready)
        {
            if (audits.Count != 0)
                issues.Add("A correction audit exists while adjustment #1820 InventoryStatus is still NULL.");
        }
        else if (alreadyApplied)
        {
            ValidateAppliedAudit(audits, issues);
        }
        else if (adjustment is not null)
        {
            issues.Add($"Adjustment #1820 has a conflicting InventoryStatus '{adjustment.InventoryStatus}' under provider '{dbContext.Database.ProviderName}'; only NULL or the exact applied Conventional state is accepted.");
        }

        BinsRunReversal1820CorrectionEvidence? evidence = null;
        if (adjustment is not null && parent is not null && original is not null && fruit is not null)
        {
            evidence = new(
                adjustment.Id,
                adjustment.AdjustmentType,
                adjustment.ChangeAmount,
                adjustment.OldBinCount,
                adjustment.NewBinCount,
                adjustment.ActualRunId,
                adjustment.ActualRunRevisionId,
                adjustment.FruitProfileId,
                adjustment.LotNumber,
                adjustment.VarietyCode,
                adjustment.CropYear,
                adjustment.WarehouseId,
                adjustment.RoomId,
                adjustment.Source,
                adjustment.InventoryInvariantVersion,
                adjustment.InventoryStatus,
                parent.Id,
                parent.InventoryStatus ?? "",
                original.Id,
                original.InventoryStatus ?? "",
                fruit.Name,
                fruit.ProductionType,
                fruit.IsOrganic,
                await dbContext.RoomInventoryAdjustments.LongCountAsync(cancellationToken),
                await dbContext.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount, cancellationToken),
                await dbContext.BinsRunEntries.LongCountAsync(cancellationToken),
                await dbContext.BinsRunEntries.SumAsync(x => (long)x.BinsRun, cancellationToken),
                await dbContext.Receipts.LongCountAsync(cancellationToken),
                await dbContext.Receipts.SumAsync(x => (long)x.BinCount, cancellationToken),
                audits.Count);
        }

        var targetFingerprint = Sha256(JsonSerializer.Serialize(new
        {
            adjustment?.InventoryStatus,
            AuditCount = audits.Count,
            AuditIds = audits.Select(x => x.Id).ToArray()
        }));
        var protectedFingerprint = Sha256(JsonSerializer.Serialize(new
        {
            Adjustment = adjustment is null ? null : new
            {
                adjustment.Id,
                adjustment.AdjustmentType,
                adjustment.ChangeAmount,
                adjustment.OldBinCount,
                adjustment.NewBinCount,
                adjustment.ActualRunId,
                adjustment.ActualRunRevisionId,
                adjustment.FruitProfileId,
                adjustment.LotNumber,
                adjustment.VarietyCode,
                adjustment.CropYear,
                adjustment.WarehouseId,
                adjustment.RoomId,
                adjustment.Source,
                adjustment.InventoryInvariantVersion
            },
            Parent = parent is null ? null : new
            {
                parent.Id,
                parent.InventoryAdjustmentId,
                parent.ActualRunId,
                parent.ActualRunRevisionId,
                parent.ReversesBinsRunEntryId,
                parent.TransactionType,
                parent.BinsRun,
                parent.FruitProfileId,
                parent.LotNumber,
                parent.VarietyCode,
                parent.InventoryStatus,
                parent.WarehouseId,
                parent.RoomId,
                parent.CropYear,
                parent.PreviousAvailableBins,
                parent.NewAvailableBins
            },
            Original = original is null ? null : new
            {
                original.Id,
                original.ActualRunId,
                original.ActualRunRevisionId,
                original.TransactionType,
                original.BinsRun,
                original.FruitProfileId,
                original.LotNumber,
                original.VarietyCode,
                original.InventoryStatus,
                original.WarehouseId,
                original.RoomId,
                original.CropYear,
                original.PreviousAvailableBins,
                original.NewAvailableBins,
                original.IsReversed
            },
            Fruit = fruit is null ? null : new
            {
                fruit.Id,
                fruit.VarietyCode,
                fruit.Name,
                fruit.ProductionType,
                fruit.IsOrganic
            },
            Quantities = evidence is null ? null : new
            {
                evidence.AdjustmentCount,
                evidence.AdjustmentChangeAmountSum,
                evidence.BinsRunEntryCount,
                evidence.BinsRunQuantitySum,
                evidence.ReceiptCount,
                evidence.ReceiptQuantitySum
            }
        }));
        var state = issues.Count != 0 ? "Refused" : alreadyApplied ? "AlreadyApplied" : "Ready";
        return new(state, businessTime.UtcNow, targetFingerprint, protectedFingerprint, issues, evidence);
    }

    public async Task<BinsRunReversal1820CorrectionResult> RunAsync(
        BinsRunReversal1820CorrectionRequest request,
        CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused") return Failed("Preflight refused the #1820 correction. No data was changed.", preflight);
        if (preflight.State == "AlreadyApplied")
            return new(true, false, true, "The exact reviewed #1820 correction is already applied; zero writes were made.", preflight);
        if (!request.Apply) return new(true, false, false, "Dry-run passed for the exact reviewed #1820 correction.", preflight);
        if (request.AuthorizationToken != BinsRunReversal1820CorrectionConstants.ApplyAuthorizationToken)
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
        var databaseVerified = backup is not null
            && backup.Status == BackupRunStatuses.Succeeded
            && backup.VerifiedAt is not null
            && backup.RetentionProcessedAt is not null
            && backup.LeaseReleasedAt is not null
            && backup.PrunedAt is null
            && Same(backup.Sha256, request.VerifiedBackupPackageSha256);
        var restoreAttested = !appEnvironment.IsProduction
            && request.ConfirmDisposableRestore
            && request.VerifiedBackupRunId == BinsRunReversal1820CorrectionConstants.VerifiedRestoreBackupRunId
            && Same(request.VerifiedBackupPackageSha256, BinsRunReversal1820CorrectionConstants.VerifiedRestorePackageSha256)
            && backup is not null;
        if (!databaseVerified && !restoreAttested)
            return Failed("Apply requires a fully verified retained backup, or the exact reviewed run-110 package attestation on a disposable restore.", preflight);

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
            if (locked.State != "Ready"
                || locked.TargetFingerprint != preflight.TargetFingerprint
                || locked.ProtectedFingerprint != preflight.ProtectedFingerprint)
                throw new InvalidOperationException("Database state changed after reviewed preflight.");

            int updated;
            if (dbContext.Database.IsRelational())
            {
                updated = await dbContext.RoomInventoryAdjustments
                    .Where(x => x.Id == AdjustmentId && x.InventoryStatus == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.InventoryStatus, "Conventional"),
                        cancellationToken);
            }
            else
            {
                var adjustment = await dbContext.RoomInventoryAdjustments.SingleAsync(x => x.Id == AdjustmentId, cancellationToken);
                adjustment.InventoryStatus = "Conventional";
                updated = 1;
            }
            if (updated != 1)
                throw new InvalidOperationException("The exact #1820 InventoryStatus NULL target was not updated once.");
            var now = businessTime.UtcNow;
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = admin.Id,
                Action = "ReviewedProductionCorrection",
                EntityName = BinsRunReversal1820CorrectionConstants.AuditEntityName,
                EntityKey = BinsRunReversal1820CorrectionConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    RoomInventoryAdjustmentId = AdjustmentId,
                    InventoryStatus = (string?)null,
                    preflight.TargetFingerprint,
                    preflight.ProtectedFingerprint,
                    BackupRunId = request.VerifiedBackupRunId,
                    BackupPackageSha256 = request.VerifiedBackupPackageSha256
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    RoomInventoryAdjustmentId = AdjustmentId,
                    InventoryStatus = "Conventional",
                    ParentBinsRunEntryId = ParentEntryId,
                    OriginalBinsRunEntryId = OriginalEntryId,
                    InventoryQuantityDelta = 0,
                    BinsRunQuantityDelta = 0,
                    ReceiptQuantityDelta = 0,
                    CorrectionAdministrator = admin.Email,
                    Reason = request.Reason.Trim(),
                    RootCause = "The historical reversal adjustment copied blank InventoryStatus from the current aggregate snapshot while its persisted reversal entry retained the original Conventional transaction identity.",
                    Semantics = "One-column historical metadata consistency correction; quantities, balances, run evidence, receipts, treatments, transfers, and packouts are unchanged."
                }, JsonOptions),
                SourceApplication = BinsRunReversal1820CorrectionConstants.AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied"
                || postflight.ProtectedFingerprint != preflight.ProtectedFingerprint
                || postflight.Evidence?.AuditCount != 1)
                throw new InvalidOperationException("Focused post-apply verification failed.");

            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Reviewed #1820 inventory-status correction applied. BackupRunId={BackupRunId} Admin={Admin}", request.VerifiedBackupRunId, admin.Email);
            return new(true, true, false, "The exact reviewed #1820 correction completed successfully.", postflight);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "The #1820 correction failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("The correction failed and was rolled back. No partial change was retained.", await PreflightAsync(cancellationToken));
        }
    }

    private static void ValidateAdjustment(AdjustmentSnapshot? value, List<string> issues)
    {
        if (value is null) { issues.Add("RoomInventoryAdjustment #1820 is missing."); return; }
        if (value.AdjustmentType != BinsRunService.ReversalAdjustmentType || value.ChangeAmount != 8
            || value.OldBinCount != 22 || value.NewBinCount != 30 || value.ActualRunId != 9
            || value.ActualRunRevisionId != 55 || value.FruitProfileId != 17 || value.LotNumber != "1532"
            || value.VarietyCode != "BART" || value.CropYear != 2026 || value.WarehouseId != 4 || value.RoomId != 1
            || value.Source != "Actual Run #9 reversal" || value.InventoryInvariantVersion != 1)
            issues.Add("RoomInventoryAdjustment #1820 no longer matches the exact reviewed reversal evidence.");
    }

    private static void ValidateParent(EntrySnapshot? value, List<string> issues)
    {
        if (value is null) { issues.Add("BinsRunEntry #164 is missing."); return; }
        if (value.InventoryAdjustmentId != 1820 || value.ActualRunId != 9 || value.ActualRunRevisionId != 55
            || value.ReversesBinsRunEntryId != 42 || value.TransactionType != ActualRunTransactionTypes.Reversal
            || value.BinsRun != 8 || value.FruitProfileId != 17 || value.LotNumber != "1532" || value.VarietyCode != "BART"
            || value.InventoryStatus != "Conventional" || value.WarehouseId != 4 || value.RoomId != 1 || value.CropYear != 2026
            || value.PreviousAvailableBins != 22 || value.NewAvailableBins != 30)
            issues.Add("BinsRunEntry #164 no longer matches the exact reviewed reversal parent evidence.");
    }

    private static void ValidateOriginal(EntrySnapshot? value, List<string> issues)
    {
        if (value is null) { issues.Add("BinsRunEntry #42 is missing."); return; }
        if (value.ActualRunId != 9 || value.ActualRunRevisionId != 9 || value.TransactionType != ActualRunTransactionTypes.Depletion
            || value.BinsRun != 8 || value.FruitProfileId != 17 || value.LotNumber != "1532" || value.VarietyCode != "BART"
            || value.InventoryStatus != "Conventional" || value.WarehouseId != 4 || value.RoomId != 1 || value.CropYear != 2026
            || value.PreviousAvailableBins != 24 || value.NewAvailableBins != 16 || !value.IsReversed)
            issues.Add("BinsRunEntry #42 no longer matches the exact reviewed original depletion evidence.");
    }

    private static void ValidateFruit(FruitSnapshot? value, List<string> issues)
    {
        if (value is null || value.VarietyCode != "BART" || value.Name != "Bartlett"
            || value.ProductionType != "Conventional" || value.IsOrganic)
            issues.Add("FruitProfile #17 no longer matches BART / Bartlett / Conventional / IsOrganic=false.");
    }

    private static void ValidateAppliedAudit(IReadOnlyList<AuditLog> audits, List<string> issues)
    {
        if (audits.Count != 1 || audits[0].Action != "ReviewedProductionCorrection"
            || !AuditHasExpectedValues(audits[0].AfterValuesJson))
            issues.Add("The applied #1820 state does not have exactly one matching reviewed correction audit.");
    }

    private static bool AuditHasExpectedValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.GetProperty("roomInventoryAdjustmentId").GetInt64() == AdjustmentId
                && root.GetProperty("inventoryStatus").GetString() == "Conventional"
                && root.GetProperty("parentBinsRunEntryId").GetInt64() == ParentEntryId
                && root.GetProperty("originalBinsRunEntryId").GetInt64() == OriginalEntryId
                && root.GetProperty("inventoryQuantityDelta").GetInt32() == 0
                && root.GetProperty("binsRunQuantityDelta").GetInt32() == 0
                && root.GetProperty("receiptQuantityDelta").GetInt32() == 0;
        }
        catch (JsonException) { return false; }
        catch (KeyNotFoundException) { return false; }
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static BinsRunReversal1820CorrectionResult Failed(string message, BinsRunReversal1820CorrectionPreflight preflight) =>
        new(false, false, false, message, preflight);
}
