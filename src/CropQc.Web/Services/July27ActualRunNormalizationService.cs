using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IJuly27ActualRunNormalizationService
{
    Task<July27ActualRunNormalizationPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<July27ActualRunNormalizationResult> RunAsync(July27ActualRunNormalizationRequest request, CancellationToken cancellationToken);
}

public sealed record July27ActualRunNormalizationRequest(
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

public sealed record July27ActualRunNormalizationResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    long? ActualRunId,
    long? ActualRunRevisionId,
    long? RunExpectationId,
    July27ActualRunNormalizationPreflight Preflight);

public sealed record July27ActualRunNormalizationPreflight(
    string State,
    DateTimeOffset GeneratedAtUtc,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues,
    IReadOnlyList<July27ActualRunTargetLine> TargetLines,
    July27ActualRunReportingBaseline Reporting,
    long? ActualRunId,
    long? ActualRunRevisionId,
    long? RunExpectationId);

public sealed record July27ActualRunTargetLine(
    long BinsRunEntryId,
    long ReceiptId,
    long InventoryAdjustmentId,
    long SourceInventoryAdjustmentId,
    int BinsRun,
    int PreviousAvailableBins,
    int NewAvailableBins,
    string Grower,
    int? GrowerLotId,
    string GrowerNumber,
    string Lot,
    string Variety,
    string ProductionType,
    bool IsOrganic,
    DateTimeOffset RunAt,
    DateTimeOffset RecordedAt,
    int RecordedByUserId,
    string RecordedByEmail);

public sealed record July27ActualRunReportingBaseline(
    int AllBins,
    int WpBins,
    int EbsBins,
    string GroupingSha256,
    int GroupCount);

public static class July27ActualRunNormalizationConstants
{
    public const string CommandName = "--normalize-july-27-2026-actual-run";
    public const string ApplyAuthorizationToken = "APPLY_REVIEWED_JULY_27_2026_ACTUAL_RUN_NORMALIZATION";
    public const string RevisionOperationKey = "historical-actualrun-20260727-wp-bart-1084-28-29-30";
    public const string AuditEntityName = "July27ActualRunNormalization";
    public const string AuditEntityKey = RevisionOperationKey;
    public const string HistoricalOperatorEmail = "alexis@wp-packing.com";
    public const long VerifiedRestoreBackupRunId = 62;
    public const string VerifiedRestorePackageSha256 = "af54589c20c5921681a00f9e01cad801907673fc4bc6f42bfb6d8b81e03603ba";
}

public sealed class July27ActualRunNormalizationService(
    CropQcDbContext dbContext,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    IRunExpectationService runExpectationService,
    IInventoryDeductionInvariantService inventoryInvariant,
    ILogger<July27ActualRunNormalizationService> logger) : IJuly27ActualRunNormalizationService
{
    private static readonly long[] TargetEntryIds = [28, 29, 30];
    private static readonly long[] TargetAdjustmentIds = [89, 90, 91];
    private static readonly DateTimeOffset HistoricalRunAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset PacificDayStartUtc = DateTimeOffset.Parse("2026-07-27T07:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset PacificDayEndUtc = DateTimeOffset.Parse("2026-07-28T07:00:00Z", CultureInfo.InvariantCulture);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly ExpectedLine[] ExpectedLines =
    [
        new(28, 92, 89, 82, 64, 64, 0, "WINDY POINT", null, "1084", "1084", DateTimeOffset.Parse("2026-07-28T05:11:26.393444Z", CultureInfo.InvariantCulture)),
        new(29, 96, 90, 86, 62, 62, 0, "WP Orchard Conventional", 398, "1084", "1084", DateTimeOffset.Parse("2026-07-28T05:11:51.587430Z", CultureInfo.InvariantCulture)),
        new(30, 94, 91, 84, 58, 64, 6, "WP Orchard Conventional", 398, "1084", "1084", DateTimeOffset.Parse("2026-07-28T05:12:15.093102Z", CultureInfo.InvariantCulture))
    ];

    private static readonly ExpectedAdjustment[] ExpectedAdjustments =
    [
        new(89, 92, null, 17, "WINDY POINT", "1084", 64, -64, 0, "BART", 8),
        new(90, 96, 398, 17, "WP Orchard Conventional", "1084", 62, -62, 0, "BART", 8),
        new(91, 94, 398, 17, "WP Orchard Conventional", "1084", 64, -58, 6, "BART", 8)
    ];

    public async Task<July27ActualRunNormalizationPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        var issues = new List<string>();
        var entryQuery = dbContext.BinsRunEntries.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Include(x => x.CreatedByUser);
        var dateEntries = dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? (await entryQuery.OrderBy(x => x.Id).ToListAsync(cancellationToken))
                .Where(x => x.RunAt >= PacificDayStartUtc && x.RunAt < PacificDayEndUtc)
                .ToList()
            : await entryQuery
                .Where(x => x.RunAt >= PacificDayStartUtc && x.RunAt < PacificDayEndUtc)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
        var entries = dateEntries.Where(x => TargetEntryIds.Contains(x.Id)).OrderBy(x => x.Id).ToList();
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => TargetAdjustmentIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var revision = await dbContext.ActualRunRevisions.AsNoTracking()
            .Include(x => x.ActualRun)
            .SingleOrDefaultAsync(x => x.OperationKey == July27ActualRunNormalizationConstants.RevisionOperationKey, cancellationToken);
        var auditCount = await dbContext.AuditLogs.AsNoTracking()
            .CountAsync(x => x.EntityName == July27ActualRunNormalizationConstants.AuditEntityName
                && x.EntityKey == July27ActualRunNormalizationConstants.AuditEntityKey, cancellationToken);
        var targetExpectationSources = await dbContext.RunExpectationSources.AsNoTracking()
            .Include(x => x.RunExpectation)
            .Where(x => TargetEntryIds.Contains(x.BinsRunEntryId))
            .ToListAsync(cancellationToken);

        if (dateEntries.Count != ExpectedLines.Length || !dateEntries.Select(x => x.Id).SequenceEqual(TargetEntryIds))
        {
            issues.Add("The Pacific 2026-07-27 Bins Run population is not exactly entries 28, 29, and 30; the physical-run grouping is ambiguous.");
        }
        if (entries.Count != ExpectedLines.Length)
        {
            issues.Add("One or more reviewed target Bins Run entries are missing.");
        }
        if (adjustments.Count != ExpectedAdjustments.Length)
        {
            issues.Add("One or more reviewed depletion adjustments are missing.");
        }

        foreach (var expected in ExpectedLines)
        {
            var entry = entries.SingleOrDefault(x => x.Id == expected.Id);
            if (entry is null) continue;
            ValidateLine(entry, expected, revision, issues);
        }
        foreach (var expected in ExpectedAdjustments)
        {
            var adjustment = adjustments.SingleOrDefault(x => x.Id == expected.Id);
            if (adjustment is null) continue;
            ValidateAdjustment(adjustment, expected, revision, issues);
        }

        var operatorUser = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == 8 && x.Email == July27ActualRunNormalizationConstants.HistoricalOperatorEmail && x.IsActive,
            cancellationToken);
        if (operatorUser is null)
        {
            issues.Add("The reviewed historical operator identity (user 8, alexis@wp-packing.com) is missing, inactive, or changed.");
        }
        if (await dbContext.BinsRunEntries.AsNoTracking().AnyAsync(
            x => x.ReversesBinsRunEntryId != null && TargetEntryIds.Contains(x.ReversesBinsRunEntryId.Value), cancellationToken))
        {
            issues.Add("A target Bins Run entry has a reversal row.");
        }

        var duplicateRuns = await dbContext.ActualRuns.AsNoTracking()
            .Where(x => x.RunAt == HistoricalRunAt && x.RunFacilityCodeSnapshot == EmploymentFacilities.Wp)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (revision is null && duplicateRuns.Count != 0)
        {
            issues.Add("An unrecognized WP Actual Run already exists at the reviewed historical timestamp.");
        }
        if (revision is not null && (duplicateRuns.Count != 1 || duplicateRuns[0] != revision.ActualRunId))
        {
            issues.Add("The deterministic revision does not uniquely identify the WP Actual Run at the reviewed historical timestamp.");
        }

        RunExpectation? expectation = null;
        if (revision is not null)
        {
            expectation = await dbContext.RunExpectations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ActualRunRevisionId == revision.Id, cancellationToken);
        }
        var isApplied = revision is not null && expectation is not null;
        if (revision is null && (auditCount != 0 || targetExpectationSources.Count != 0))
        {
            issues.Add("A normalization audit or target expectation source exists without the deterministic revision.");
        }
        if (revision is not null)
        {
            ValidateAppliedShape(revision, expectation, entries, adjustments, auditCount, targetExpectationSources, issues);
        }

        var targetLines = entries.Select(x => new July27ActualRunTargetLine(
            x.Id,
            x.ReceiptId ?? 0,
            x.InventoryAdjustmentId,
            x.SourceInventoryAdjustmentId ?? 0,
            x.BinsRun,
            x.PreviousAvailableBins,
            x.NewAvailableBins,
            x.GrowerName,
            x.GrowerLotId,
            x.GrowerNumberSnapshot ?? "",
            x.LotNumber,
            x.ReportingVarietyCodeSnapshot ?? "",
            x.ProductionTypeSnapshot ?? "",
            x.IsOrganicSnapshot ?? false,
            x.RunAt,
            x.CreatedAt,
            x.CreatedByUserId ?? 0,
            x.CreatedByUser?.Email ?? "")).ToList();
        var targetFingerprint = Sha256(string.Join('\n', targetLines.Select(CanonicalTargetLine)));
        var protectedSnapshot = await CaptureProtectedSnapshotAsync(cancellationToken);
        var state = issues.Count != 0 ? "Refused" : isApplied ? "AlreadyApplied" : "Ready";
        return new July27ActualRunNormalizationPreflight(
            state,
            now,
            targetFingerprint,
            protectedSnapshot.Fingerprint,
            issues,
            targetLines,
            protectedSnapshot.Reporting,
            revision?.ActualRunId,
            revision?.Id,
            expectation?.Id);
    }

    public async Task<July27ActualRunNormalizationResult> RunAsync(
        July27ActualRunNormalizationRequest request,
        CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused")
        {
            return Failed("Preflight refused the historical normalization. No data was changed.", preflight);
        }
        if (preflight.State == "AlreadyApplied")
        {
            return new(true, false, true, "The exact reviewed normalization is already complete; no data was changed.", preflight.ActualRunId, preflight.ActualRunRevisionId, preflight.RunExpectationId, preflight);
        }
        if (!request.Apply)
        {
            return new(true, false, false, "Dry-run preflight passed for the exact three reviewed historical rows.", null, null, null, preflight);
        }
        if (!string.Equals(request.AuthorizationToken, July27ActualRunNormalizationConstants.ApplyAuthorizationToken, StringComparison.Ordinal))
        {
            return Failed("Apply requires the exact reviewed authorization token.", preflight);
        }
        if (appEnvironment.IsProduction && !request.ConfirmProduction)
        {
            return Failed("Production apply requires --confirm-production.", preflight);
        }
        if (request.VerifiedBackupRunId is null)
        {
            return Failed("Apply requires an explicit fully verified backup run ID.", preflight);
        }
        var backup = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId.Value, cancellationToken);
        var hasDatabaseVerifiedBackup = backup is not null
            && backup.Status == BackupRunStatuses.Succeeded
            && backup.VerifiedAt is not null
            && backup.RetentionProcessedAt is not null
            && backup.LeaseReleasedAt is not null
            && backup.PrunedAt is null;
        var hasVerifiedDisposableRestoreAttestation = !appEnvironment.IsProduction
            && request.ConfirmDisposableRestore
            && request.VerifiedBackupRunId == July27ActualRunNormalizationConstants.VerifiedRestoreBackupRunId
            && string.Equals(
                request.VerifiedBackupPackageSha256,
                July27ActualRunNormalizationConstants.VerifiedRestorePackageSha256,
                StringComparison.OrdinalIgnoreCase)
            && backup is not null;
        if (!hasDatabaseVerifiedBackup && !hasVerifiedDisposableRestoreAttestation)
        {
            return Failed("The supplied backup run is not a current, fully verified successful backup with retention complete and its lease released. A non-production restored-copy rehearsal may instead use the exact reviewed run-62 package attestation.", preflight);
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Failed("Apply requires a correction reason.", preflight);
        }
        if (!string.Equals(request.ExpectedTargetFingerprint, preflight.TargetFingerprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return Failed("The reviewed target or protected-data fingerprint does not match current database state.", preflight);
        }
        var correctionAdmin = await dbContext.Users.SingleOrDefaultAsync(
            x => x.Email == request.RequestedByEmail
                && x.IsActive
                && x.UserRoles.Any(userRole => userRole.Role.IsActive && userRole.Role.Name == BuiltInRoleNames.Admin),
            cancellationToken);
        if (correctionAdmin is null)
        {
            return Failed("The correction administrator is missing, inactive, or does not hold the active built-in Admin role.", preflight);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var lockedPreflight = await PreflightAsync(cancellationToken);
            if (lockedPreflight.State != "Ready"
                || !string.Equals(lockedPreflight.TargetFingerprint, preflight.TargetFingerprint, StringComparison.Ordinal)
                || !string.Equals(lockedPreflight.ProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Database state changed after preflight; normalization was rolled back.");
            }

            var historicalOperator = await dbContext.Users.SingleAsync(x => x.Id == 8, cancellationToken);
            var now = businessTime.UtcNow;
            var actualRun = new ActualRun
            {
                Status = ActualRunStatuses.Active,
                CurrentRevisionNumber = 1,
                ConcurrencyVersion = 1,
                RunAt = HistoricalRunAt,
                Notes = "Historical normalization of reviewed Bins Run entries 28, 29, and 30; no inventory movement created.",
                CreatedByUserId = historicalOperator.Id,
                CreatedAt = now,
                UpdatedByUserId = historicalOperator.Id,
                UpdatedAt = now,
                RunFacilityWarehouseId = 4,
                RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
                RunFacilityAssignmentSource = RunFacilityAssignmentSources.HistoricalBackfill,
                RunFacilityAssignedByUserId = historicalOperator.Id,
                RunFacilityAssignedAt = now
            };
            dbContext.ActualRuns.Add(actualRun);
            await dbContext.SaveChangesAsync(cancellationToken);

            var revision = new ActualRunRevision
            {
                ActualRunId = actualRun.Id,
                RevisionNumber = 1,
                OperationType = ActualRunRevisionTypes.Create,
                OperationKey = July27ActualRunNormalizationConstants.RevisionOperationKey,
                IsCurrent = true,
                Reason = request.Reason.Trim(),
                CreatedByUserId = historicalOperator.Id,
                CreatedAt = now
            };
            dbContext.ActualRunRevisions.Add(revision);
            await dbContext.SaveChangesAsync(cancellationToken);

            var entries = await dbContext.BinsRunEntries
                .Where(x => TargetEntryIds.Contains(x.Id))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var adjustments = await dbContext.RoomInventoryAdjustments
                .Where(x => TargetAdjustmentIds.Contains(x.Id))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var entry in entries)
            {
                entry.ActualRunId = actualRun.Id;
                entry.ActualRunRevisionId = revision.Id;
                entry.TransactionType = ActualRunTransactionTypes.Depletion;
            }
            foreach (var adjustment in adjustments)
            {
                adjustment.ActualRunId = actualRun.Id;
                adjustment.ActualRunRevisionId = revision.Id;
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            var expectation = await runExpectationService.CreateFrozenAsync(
                actualRun,
                revision,
                entries,
                correctionAdmin.Id,
                now,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = correctionAdmin.Id,
                Action = "HistoricalNormalize",
                EntityName = July27ActualRunNormalizationConstants.AuditEntityName,
                EntityKey = July27ActualRunNormalizationConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    EntryIds = TargetEntryIds,
                    AdjustmentIds = TargetAdjustmentIds,
                    TransactionType = ActualRunTransactionTypes.Legacy,
                    ActualRunId = (long?)null,
                    RunAt = HistoricalRunAt,
                    HistoricalOperator = July27ActualRunNormalizationConstants.HistoricalOperatorEmail,
                    TargetFingerprint = preflight.TargetFingerprint,
                    ProtectedFingerprint = preflight.ProtectedFingerprint,
                    BackupRunId = backup!.Id,
                    BackupVerification = hasDatabaseVerifiedBackup ? "DatabaseRecord" : "VerifiedRun62PackageAttestation",
                    VerifiedBackupPackageSha256 = hasVerifiedDisposableRestoreAttestation ? request.VerifiedBackupPackageSha256 : null
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    actualRun.Id,
                    ActualRunRevisionId = revision.Id,
                    RunExpectationId = expectation.Id,
                    SourceEntryIds = expectation.Sources.OrderBy(x => x.BinsRunEntryId).Select(x => x.BinsRunEntryId),
                    HistoricalOperator = July27ActualRunNormalizationConstants.HistoricalOperatorEmail,
                    CorrectionAdministrator = correctionAdmin.Email,
                    Reason = request.Reason.Trim()
                }, JsonOptions),
                SourceApplication = "CropQc.Web reviewed historical normalization command",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await inventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);

            dbContext.ChangeTracker.Clear();
            var postSnapshot = await CaptureProtectedSnapshotAsync(cancellationToken);
            if (!string.Equals(postSnapshot.Fingerprint, preflight.ProtectedFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Protected inventory, receipt, QC, packout, or reporting state changed; normalization was rolled back.");
            }
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied")
            {
                throw new InvalidOperationException("The focused post-apply verifier did not recognize the exact completed shape; normalization was rolled back.");
            }
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Reviewed July 27 historical Actual Run normalization applied. ActualRunId={ActualRunId} RevisionId={RevisionId} ExpectationId={ExpectationId} BackupRunId={BackupRunId} CorrectionAdmin={CorrectionAdmin}",
                actualRun.Id,
                revision.Id,
                expectation.Id,
                backup!.Id,
                correctionAdmin.Email);
            return new(true, true, false, "The exact reviewed historical normalization completed successfully.", actualRun.Id, revision.Id, expectation.Id, postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "July 27 historical Actual Run normalization failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            var failedPreflight = await PreflightAsync(cancellationToken);
            return Failed("Normalization failed and was rolled back. Review restricted logs.", failedPreflight);
        }
    }

    private static void ValidateLine(BinsRunEntry entry, ExpectedLine expected, ActualRunRevision? revision, List<string> issues)
    {
        var coreMatches = entry.ReceiptId == expected.ReceiptId
            && entry.InventoryAdjustmentId == expected.AdjustmentId
            && entry.SourceInventoryAdjustmentId == expected.SourceAdjustmentId
            && entry.BinsRun == expected.Bins
            && entry.PreviousAvailableBins == expected.Previous
            && entry.NewAvailableBins == expected.Next
            && entry.WarehouseId == 4
            && entry.RoomId == 1
            && entry.CropYear is null
            && entry.FruitProfileId == 17
            && entry.GrowerLotId == expected.GrowerLotId
            && entry.GrowerName == expected.Grower
            && entry.GrowerNumberSnapshot == expected.GrowerNumber
            && entry.LotNumber == expected.Lot
            && entry.RunAt == HistoricalRunAt
            && entry.CreatedAt == expected.RecordedAt
            && entry.CreatedByUserId == 8
            && entry.CreatedByUser?.Email == July27ActualRunNormalizationConstants.HistoricalOperatorEmail
            && entry.ReportingFacilityWarehouseId == 4
            && entry.ReportingFacilityCodeSnapshot == EmploymentFacilities.Wp
            && entry.ReportingCropYearSnapshot == 2026
            && entry.ReportingFruitProfileIdSnapshot == 17
            && entry.ReportingVarietyCodeSnapshot == "BART"
            && entry.ProductionTypeSnapshot == "Conventional"
            && entry.IsOrganicSnapshot == false
            && entry.FruitProfile?.VarietyCode == "BART"
            && entry.FruitProfile.ProductionType == "Conventional"
            && !entry.FruitProfile.IsOrganic
            && !entry.IsReversed
            && entry.ReversesBinsRunEntryId is null;
        if (!coreMatches)
        {
            issues.Add($"Bins Run entry {entry.Id} no longer matches the reviewed quantity, identity, timestamp, facility, variety/organic, operator, or reversal evidence.");
        }

        var expectedUnapplied = revision is null
            && entry.TransactionType == ActualRunTransactionTypes.Legacy
            && entry.ActualRunId is null
            && entry.ActualRunRevisionId is null;
        var expectedApplied = revision is not null
            && entry.TransactionType == ActualRunTransactionTypes.Depletion
            && entry.ActualRunId == revision.ActualRunId
            && entry.ActualRunRevisionId == revision.Id;
        if (!expectedUnapplied && !expectedApplied)
        {
            issues.Add($"Bins Run entry {entry.Id} has unexpected Actual Run linkage or transaction type.");
        }
    }

    private static void ValidateAdjustment(RoomInventoryAdjustment adjustment, ExpectedAdjustment expected, ActualRunRevision? revision, List<string> issues)
    {
        var coreMatches = adjustment.ReceiptId == expected.ReceiptId
            && adjustment.GrowerLotId == expected.GrowerLotId
            && adjustment.FruitProfileId == expected.FruitProfileId
            && adjustment.GrowerName == expected.Grower
            && adjustment.LotNumber == expected.Lot
            && adjustment.OldBinCount == expected.Old
            && adjustment.ChangeAmount == expected.Change
            && adjustment.NewBinCount == expected.Next
            && adjustment.VarietyCode == expected.Variety
            && adjustment.WarehouseId == 4
            && adjustment.RoomId == 1
            && adjustment.CropYear is null
            && adjustment.AdjustmentType == "BinsRun"
            && adjustment.Source == "Bins Run"
            && adjustment.CreatedByUserId == expected.CreatedByUserId
            && adjustment.InventoryInvariantVersion == 0
            && adjustment.InventoryOperationKey is null
            && adjustment.RoomTransferId is null
            && adjustment.ReceiptInventoryOverrideId is null;
        if (!coreMatches)
        {
            issues.Add($"Room inventory adjustment {adjustment.Id} no longer matches the reviewed historical depletion evidence.");
        }
        var expectedUnapplied = revision is null && adjustment.ActualRunId is null && adjustment.ActualRunRevisionId is null;
        var expectedApplied = revision is not null && adjustment.ActualRunId == revision.ActualRunId && adjustment.ActualRunRevisionId == revision.Id;
        if (!expectedUnapplied && !expectedApplied)
        {
            issues.Add($"Room inventory adjustment {adjustment.Id} has unexpected Actual Run linkage.");
        }
    }

    private static void ValidateAppliedShape(
        ActualRunRevision revision,
        RunExpectation? expectation,
        IReadOnlyList<BinsRunEntry> entries,
        IReadOnlyList<RoomInventoryAdjustment> adjustments,
        int auditCount,
        IReadOnlyList<RunExpectationSource> expectationSources,
        List<string> issues)
    {
        var run = revision.ActualRun;
        if (run.Status != ActualRunStatuses.Active
            || run.RunAt != HistoricalRunAt
            || run.RunProjectionId is not null
            || run.CurrentRevisionNumber != 1
            || run.ConcurrencyVersion != 1
            || run.CreatedByUserId != 8
            || run.UpdatedByUserId != 8
            || run.RunFacilityWarehouseId != 4
            || run.RunFacilityCodeSnapshot != EmploymentFacilities.Wp
            || run.RunFacilityAssignmentSource != RunFacilityAssignmentSources.HistoricalBackfill
            || run.RunFacilityAssignedByUserId != 8
            || revision.RevisionNumber != 1
            || revision.OperationType != ActualRunRevisionTypes.Create
            || !revision.IsCurrent
            || revision.CreatedByUserId != 8)
        {
            issues.Add("The deterministic Actual Run or revision has an incompatible shape.");
        }
        if (expectation is null
            || expectation.ActualRunId != run.Id
            || expectation.RevisionNumber != 1
            || expectation.FacilityWarehouseId != 4
            || expectation.FacilitySnapshot != EmploymentFacilities.Wp
            || expectation.RunAtSnapshot != HistoricalRunAt
            || expectation.TotalBins != 184
            || expectation.CalculationVersion != RunExpectationCalculationVersions.Current)
        {
            issues.Add("The deterministic revision is missing its exact frozen 184-bin Run Expectation.");
        }
        if (entries.Count != 3 || entries.Any(x => x.ActualRunId != run.Id || x.ActualRunRevisionId != revision.Id || x.TransactionType != ActualRunTransactionTypes.Depletion))
        {
            issues.Add("The deterministic Actual Run does not own exactly the three reviewed depletion entries.");
        }
        if (adjustments.Count != 3 || adjustments.Any(x => x.ActualRunId != run.Id || x.ActualRunRevisionId != revision.Id))
        {
            issues.Add("The deterministic revision does not own exactly the three reviewed existing depletion adjustments.");
        }
        if (auditCount != 1)
        {
            issues.Add("The deterministic correction audit marker is missing or duplicated.");
        }
        if (expectation is not null
            && (expectationSources.Count != 3
                || expectationSources.Any(x => x.RunExpectationId != expectation.Id)
                || !expectationSources.Select(x => x.BinsRunEntryId).OrderBy(x => x).SequenceEqual(TargetEntryIds)))
        {
            issues.Add("The frozen expectation sources are not exactly entries 28, 29, and 30.");
        }
    }

    private async Task<ProtectedSnapshot> CaptureProtectedSnapshotAsync(CancellationToken cancellationToken)
    {
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).Select(x => new
        {
            x.Id,
            x.CropYear,
            x.ReceiptId,
            x.RoomDepletionId,
            x.WarehouseId,
            x.RoomId,
            x.GrowerLotId,
            x.FruitProfileId,
            x.GrowerName,
            x.LotNumber,
            x.PoolStart,
            x.VarietyCode,
            x.OldBinCount,
            x.ChangeAmount,
            x.NewBinCount,
            x.AdjustmentType,
            x.Source,
            x.InventoryStatus,
            x.Reason,
            x.Notes,
            x.AdjustmentAt,
            x.CreatedByUserId,
            x.CreatedAt,
            x.InventoryInvariantVersion,
            x.InventoryOperationKey,
            x.RoomTransferId,
            x.ReceiptInventoryOverrideId
        }).ToListAsync(cancellationToken);
        var entries = await dbContext.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).Select(x => new
        {
            x.Id,
            x.ReceiptId,
            x.SourceInventoryAdjustmentId,
            x.InventoryAdjustmentId,
            x.WarehouseId,
            x.RoomId,
            x.CropYear,
            x.GrowerLotId,
            x.FruitProfileId,
            x.GrowerName,
            x.LotNumber,
            x.PoolStart,
            x.VarietyCode,
            x.InventoryStatus,
            x.PreviousAvailableBins,
            x.BinsRun,
            x.NewAvailableBins,
            x.Notes,
            x.RunAt,
            x.CreatedByUserId,
            x.CreatedAt,
            x.IsReconciled,
            x.IsReversed,
            x.ReversesBinsRunEntryId,
            x.ReportingFacilityWarehouseId,
            x.ReportingFacilityCodeSnapshot,
            x.ReportingFacilityAssignmentSource,
            x.ReportingCropYearSnapshot,
            x.ReportingFruitProfileIdSnapshot,
            x.ReportingVarietyCodeSnapshot,
            x.ProductionTypeSnapshot,
            x.IsOrganicSnapshot,
            x.GrowerNumberSnapshot
        }).ToListAsync(cancellationToken);
        var reporting = await CaptureReportingAsync(cancellationToken);
        var inventoryCore = JsonSerializer.Serialize(new
        {
            Adjustments = adjustments,
            Entries = entries,
            ReceiptCount = await dbContext.Receipts.AsNoTracking().CountAsync(cancellationToken),
            QcSampleCount = await dbContext.QcSamples.AsNoTracking().CountAsync(cancellationToken),
            QcReadingCount = await dbContext.QcFruitReadings.AsNoTracking().CountAsync(cancellationToken),
            PackoutRunCount = await dbContext.PackoutRuns.AsNoTracking().CountAsync(cancellationToken),
            PackoutLineCount = await dbContext.PackoutReportLines.AsNoTracking().CountAsync(cancellationToken),
            PackoutAllocationCount = await dbContext.PackoutSourceAllocations.AsNoTracking().CountAsync(cancellationToken),
            reporting
        });
        return new(Sha256(inventoryCore), reporting);
    }

    private async Task<July27ActualRunReportingBaseline> CaptureReportingAsync(CancellationToken cancellationToken)
    {
        var groups = await AuthoritativeRunReportingQuery.ApplyValidRules(dbContext.BinsRunEntries.AsNoTracking())
            .GroupBy(x => new
            {
                Facility = x.ActualRunId != null ? x.ActualRun!.RunFacilityCodeSnapshot! : x.ReportingFacilityCodeSnapshot!,
                CropYear = x.ReportingCropYearSnapshot!.Value,
                FruitProfileId = x.ReportingFruitProfileIdSnapshot!.Value,
                Variety = x.ReportingVarietyCodeSnapshot!,
                ProductionType = x.ProductionTypeSnapshot!,
                IsOrganic = x.IsOrganicSnapshot!.Value,
                Grower = x.GrowerNumberSnapshot!,
                x.LotNumber
            })
            .Select(x => new
            {
                x.Key.Facility,
                x.Key.CropYear,
                x.Key.FruitProfileId,
                x.Key.Variety,
                x.Key.ProductionType,
                x.Key.IsOrganic,
                x.Key.Grower,
                x.Key.LotNumber,
                Bins = x.Sum(y => y.BinsRun)
            })
            .ToListAsync(cancellationToken);
        var canonical = groups.OrderBy(x => x.Facility).ThenBy(x => x.CropYear).ThenBy(x => x.FruitProfileId)
            .ThenBy(x => x.Variety).ThenBy(x => x.ProductionType).ThenBy(x => x.IsOrganic)
            .ThenBy(x => x.Grower).ThenBy(x => x.LotNumber)
            .Select(x => string.Join('|', x.Facility, x.CropYear, x.FruitProfileId, x.Variety, x.ProductionType, x.IsOrganic, x.Grower, x.LotNumber, x.Bins));
        return new(
            groups.Sum(x => x.Bins),
            groups.Where(x => x.Facility == EmploymentFacilities.Wp).Sum(x => x.Bins),
            groups.Where(x => x.Facility == EmploymentFacilities.Ebs).Sum(x => x.Bins),
            Sha256(string.Join('\n', canonical)),
            groups.Count);
    }

    private static string CanonicalTargetLine(July27ActualRunTargetLine x) => string.Join('|',
        x.BinsRunEntryId, x.ReceiptId, x.InventoryAdjustmentId, x.SourceInventoryAdjustmentId,
        x.BinsRun, x.PreviousAvailableBins, x.NewAvailableBins, x.Grower, x.GrowerLotId,
        x.GrowerNumber, x.Lot, x.Variety, x.ProductionType, x.IsOrganic,
        x.RunAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        x.RecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        x.RecordedByUserId, x.RecordedByEmail);

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static July27ActualRunNormalizationResult Failed(string message, July27ActualRunNormalizationPreflight preflight) =>
        new(false, false, false, message, preflight.ActualRunId, preflight.ActualRunRevisionId, preflight.RunExpectationId, preflight);

    private sealed record ExpectedLine(long Id, long ReceiptId, long AdjustmentId, long SourceAdjustmentId, int Bins, int Previous, int Next, string Grower, int? GrowerLotId, string GrowerNumber, string Lot, DateTimeOffset RecordedAt);
    private sealed record ExpectedAdjustment(long Id, long ReceiptId, int? GrowerLotId, int FruitProfileId, string Grower, string Lot, int Old, int Change, int Next, string Variety, int CreatedByUserId);
    private sealed record ProtectedSnapshot(string Fingerprint, July27ActualRunReportingBaseline Reporting);
}
