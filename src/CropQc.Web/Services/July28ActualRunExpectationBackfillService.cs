using System.Data;
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

public interface IJuly28ActualRunExpectationBackfillService
{
    Task<July28ActualRunExpectationBackfillPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<July28ActualRunExpectationBackfillResult> RunAsync(July28ActualRunExpectationBackfillRequest request, CancellationToken cancellationToken);
}

public sealed record July28ActualRunExpectationBackfillRequest(
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

public sealed record July28ActualRunExpectationBackfillResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    long? RunExpectationId,
    int SourceCount,
    July28ActualRunExpectationBackfillPreflight Preflight);

public sealed record July28ActualRunExpectationBackfillPreflight(
    string State,
    DateTimeOffset GeneratedAtUtc,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues,
    July28ActualRunExpectationEvidence? Evidence,
    July28ActualRunExpectationIntegrity Integrity,
    long? RunExpectationId,
    int SourceCount,
    int PackoutRunCount);

public sealed record July28ActualRunExpectationEvidence(
    long ActualRunId,
    DateTimeOffset RunAt,
    string Status,
    int CurrentRevisionNumber,
    long RevisionId,
    string RevisionOperationType,
    string RevisionOperationKey,
    bool RevisionIsCurrent,
    int HistoricalOperatorUserId,
    string HistoricalOperatorEmail,
    int FacilityWarehouseId,
    string Facility,
    string FacilityAssignmentSource,
    IReadOnlyList<long> BinsRunEntryIds,
    IReadOnlyList<int> Bins,
    IReadOnlyList<long> InventoryAdjustmentIds,
    IReadOnlyList<long> SourceInventoryAdjustmentIds,
    int TotalBins,
    IReadOnlyList<int> RoomIds,
    IReadOnlyList<int> CropYears,
    IReadOnlyList<string> GrowerNumbers,
    IReadOnlyList<string> Lots,
    IReadOnlyList<int> FruitProfileIds,
    IReadOnlyList<string> Varieties,
    IReadOnlyList<string> ProductionTypes,
    IReadOnlyList<bool> OrganicStates);

public sealed record July28ActualRunExpectationIntegrity(
    int AdjustmentCount,
    int AdjustmentQuantity,
    string AdjustmentSha256,
    int BinsRunEntryCount,
    int BinsRunQuantity,
    string BinsRunEntrySha256,
    string CurrentInventorySha256,
    int ReceiptCount,
    int ReceiptBins,
    string ReceiptSha256,
    int TransferCount,
    int TransferBins,
    string TransferSha256,
    int GrowerLotCount,
    string GrowerLotSha256,
    int ActualRunPhysicalBins,
    string ActualRunPhysicalSha256,
    July27ActualRunReportingBaseline Reporting);

public static class July28ActualRunExpectationBackfillConstants
{
    public const string CommandName = "--backfill-july-28-actual-run-expectation";
    public const string ApplyAuthorizationToken = "APPLY_REVIEWED_JULY_28_ACTUAL_RUN_EXPECTATION_BACKFILL";
    public const string AuditEntityName = "July28ActualRunExpectationBackfill";
    public const string AuditEntityKey = "actual-run-1-revision-1-expectation";
    public const string HistoricalOperatorEmail = "alexis@wp-packing.com";
    public const long VerifiedRestoreBackupRunId = 62;
    public const string VerifiedRestorePackageSha256 = "af54589c20c5921681a00f9e01cad801907673fc4bc6f42bfb6d8b81e03603ba";
}

public sealed class July28ActualRunExpectationBackfillService(
    CropQcDbContext dbContext,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    IRunExpectationService runExpectationService,
    IInventoryDeductionInvariantService inventoryInvariant,
    ILogger<July28ActualRunExpectationBackfillService> logger) : IJuly28ActualRunExpectationBackfillService
{
    private const long TargetRunId = 1;
    private const long TargetRevisionId = 1;
    private const long TargetEntryId = 31;
    private const long TargetAdjustmentId = 115;
    private const long TargetSourceAdjustmentId = 114;
    private static readonly DateTimeOffset HistoricalRunAt = Parse("2026-07-29T00:31:00Z");
    private static readonly DateTimeOffset HistoricalCreatedAt = Parse("2026-07-31T00:32:42.065870Z");
    private static readonly DateTimeOffset DepletionAdjustmentCreatedAt = Parse("2026-07-31T00:32:42.289775Z");
    private static readonly DateTimeOffset SourceAdjustmentAt = Parse("2026-07-30T22:31:00Z");
    private static readonly DateTimeOffset SourceAdjustmentCreatedAt = Parse("2026-07-30T22:33:35.036268Z");
    private static readonly DateTimeOffset FacilityAssignedAt = Parse("2026-08-05T04:28:53.562855Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<July28ActualRunExpectationBackfillPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var run = await dbContext.ActualRuns.AsNoTracking()
            .Include(x => x.CreatedByUser)
            .SingleOrDefaultAsync(x => x.Id == TargetRunId, cancellationToken);
        var revisions = await dbContext.ActualRunRevisions.AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Where(x => x.ActualRunId == TargetRunId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var currentRevisions = revisions.Where(x => x.IsCurrent).ToList();
        var revision = currentRevisions.SingleOrDefault();
        var entries = await dbContext.BinsRunEntries.AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.FruitProfile)
            .Where(x => x.ActualRunId == TargetRunId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var adjustment = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetAdjustmentId, cancellationToken);
        var sourceAdjustment = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetSourceAdjustmentId, cancellationToken);
        var expectations = await dbContext.RunExpectations.AsNoTracking()
            .Include(x => x.CreatedByUser).ThenInclude(x => x!.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.Sources)
            .AsSplitQuery()
            .Where(x => x.ActualRunId == TargetRunId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var audits = await dbContext.AuditLogs.AsNoTracking()
            .Where(x => x.EntityName == July28ActualRunExpectationBackfillConstants.AuditEntityName
                && x.EntityKey == July28ActualRunExpectationBackfillConstants.AuditEntityKey)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var packouts = await dbContext.PackoutRuns.AsNoTracking()
            .Where(x => x.ActualRunId == TargetRunId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (run is null)
        {
            issues.Add("Reviewed Actual Run #1 is missing.");
        }
        else
        {
            ValidateRun(run, issues);
        }
        if (revisions.Count != 1 || currentRevisions.Count != 1 || revision is null)
        {
            issues.Add("Actual Run #1 must have exactly one revision and exactly one current revision.");
        }
        else
        {
            ValidateRevision(revision, issues);
        }
        ValidateEntries(entries, revision, issues);
        ValidateAdjustment(adjustment, sourceAdjustment, issues);

        if (await dbContext.BinsRunEntries.AsNoTracking().AnyAsync(
            x => x.ReversesBinsRunEntryId == TargetEntryId, cancellationToken))
        {
            issues.Add("Bins Run entry 31 has a reversal row.");
        }

        RunExpectation? expectation = expectations.SingleOrDefault();
        var isApplied = expectation is not null;
        if (expectations.Count > 1)
        {
            issues.Add("Actual Run #1 has more than one Run Expectation.");
        }
        if (!isApplied && audits.Count != 0)
        {
            issues.Add("A backfill audit exists without the expected Run Expectation.");
        }
        if (!isApplied && packouts.Count != 0)
        {
            issues.Add("Actual Run #1 has an unexpected PackoutRun before expectation backfill.");
        }
        if (isApplied)
        {
            ValidateAppliedShape(expectation!, audits, packouts, issues);
        }

        var integrity = await CaptureIntegrityAsync(cancellationToken);
        var evidence = run is null || revision is null ? null : BuildEvidence(run, revision, entries);
        var targetFingerprint = Sha256(JsonSerializer.Serialize(evidence));
        var protectedFingerprint = Sha256(JsonSerializer.Serialize(integrity));
        var state = issues.Count != 0 ? "Refused" : isApplied ? "AlreadyApplied" : "Ready";
        return new(
            state,
            businessTime.UtcNow,
            targetFingerprint,
            protectedFingerprint,
            issues,
            evidence,
            integrity,
            expectation?.Id,
            expectation?.Sources.Count ?? 0,
            packouts.Count);
    }

    public async Task<July28ActualRunExpectationBackfillResult> RunAsync(
        July28ActualRunExpectationBackfillRequest request,
        CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused")
        {
            return Failed("Preflight refused the historical expectation backfill. No data was changed.", preflight);
        }
        if (preflight.State == "AlreadyApplied")
        {
            return new(true, false, true, "The exact reviewed expectation backfill is already complete; no data was changed.", preflight.RunExpectationId, preflight.SourceCount, preflight);
        }
        if (!request.Apply)
        {
            return new(true, false, false, "Dry-run preflight passed for the exact reviewed July 28 Actual Run #1 revision.", null, 0, preflight);
        }
        if (!string.Equals(request.AuthorizationToken, July28ActualRunExpectationBackfillConstants.ApplyAuthorizationToken, StringComparison.Ordinal))
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
            && request.VerifiedBackupRunId == July28ActualRunExpectationBackfillConstants.VerifiedRestoreBackupRunId
            && string.Equals(
                request.VerifiedBackupPackageSha256,
                July28ActualRunExpectationBackfillConstants.VerifiedRestorePackageSha256,
                StringComparison.OrdinalIgnoreCase)
            && backup is not null;
        if (!hasDatabaseVerifiedBackup && !hasVerifiedDisposableRestoreAttestation)
        {
            return Failed("The supplied backup run is not fully verified, retained, and lease-released. A non-production restored-copy rehearsal may instead use the exact reviewed run-62 package attestation.", preflight);
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
                throw new InvalidOperationException("Database state changed after preflight; expectation backfill was rolled back.");
            }

            var run = await dbContext.ActualRuns.SingleAsync(x => x.Id == TargetRunId, cancellationToken);
            var revision = await dbContext.ActualRunRevisions.SingleAsync(x => x.Id == TargetRevisionId, cancellationToken);
            var entries = await dbContext.BinsRunEntries
                .Where(x => x.ActualRunId == TargetRunId
                    && x.ActualRunRevisionId == TargetRevisionId
                    && x.TransactionType == ActualRunTransactionTypes.Depletion
                    && !x.IsReversed)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var now = businessTime.UtcNow;
            var expectation = await runExpectationService.CreateFrozenAsync(
                run,
                revision,
                entries,
                correctionAdmin.Id,
                now,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = correctionAdmin.Id,
                Action = "HistoricalExpectationBackfill",
                EntityName = July28ActualRunExpectationBackfillConstants.AuditEntityName,
                EntityKey = July28ActualRunExpectationBackfillConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    ActualRunId = TargetRunId,
                    ActualRunRevisionId = TargetRevisionId,
                    RunExpectationCount = 0,
                    EntryIds = new[] { TargetEntryId },
                    HistoricalOperator = July28ActualRunExpectationBackfillConstants.HistoricalOperatorEmail,
                    HistoricalRunAt,
                    TargetFingerprint = preflight.TargetFingerprint,
                    ProtectedFingerprint = preflight.ProtectedFingerprint,
                    BackupRunId = backup!.Id,
                    BackupVerification = hasDatabaseVerifiedBackup ? "DatabaseRecord" : "VerifiedRun62PackageAttestation",
                    VerifiedBackupPackageSha256 = hasVerifiedDisposableRestoreAttestation ? request.VerifiedBackupPackageSha256 : null
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    expectation.Id,
                    expectation.ActualRunId,
                    expectation.ActualRunRevisionId,
                    expectation.RunAtSnapshot,
                    expectation.CalculatedAt,
                    expectation.TotalBins,
                    SourceEntryIds = expectation.Sources.OrderBy(x => x.BinsRunEntryId).Select(x => x.BinsRunEntryId),
                    HistoricalOperator = July28ActualRunExpectationBackfillConstants.HistoricalOperatorEmail,
                    CorrectionAdministrator = correctionAdmin.Email,
                    Reason = request.Reason.Trim(),
                    Semantics = "Historical frozen-expectation backfill calculated at execution time using current authoritative calculation code."
                }, JsonOptions),
                SourceApplication = "CropQc.Web reviewed July 28 expectation backfill command",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await inventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);

            dbContext.ChangeTracker.Clear();
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied"
                || !string.Equals(postflight.ProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.Ordinal)
                || postflight.RunExpectationId != expectation.Id
                || postflight.SourceCount != entries.Count)
            {
                throw new InvalidOperationException("The protected-data or focused post-apply verifier failed; expectation backfill was rolled back.");
            }
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Reviewed July 28 Actual Run expectation backfill applied. ActualRunId={ActualRunId} RevisionId={RevisionId} ExpectationId={ExpectationId} SourceCount={SourceCount} CalculatedAt={CalculatedAt} BackupRunId={BackupRunId} CorrectionAdmin={CorrectionAdmin}",
                TargetRunId,
                TargetRevisionId,
                expectation.Id,
                expectation.Sources.Count,
                expectation.CalculatedAt,
                backup!.Id,
                correctionAdmin.Email);
            return new(true, true, false, "The exact reviewed historical frozen-expectation backfill completed successfully.", expectation.Id, expectation.Sources.Count, postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "July 28 Actual Run expectation backfill failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            var failedPreflight = await PreflightAsync(cancellationToken);
            return Failed("Expectation backfill failed and was rolled back. Review restricted logs.", failedPreflight);
        }
    }

    private static void ValidateRun(ActualRun run, List<string> issues)
    {
        if (run.Status != ActualRunStatuses.Active
            || run.CurrentRevisionNumber != 1
            || run.ConcurrencyVersion != 1
            || run.RunAt != HistoricalRunAt
            || run.CreatedAt != HistoricalCreatedAt
            || run.CreatedByUserId != 8
            || run.CreatedByUser?.Email != July28ActualRunExpectationBackfillConstants.HistoricalOperatorEmail
            || !run.CreatedByUser.IsActive
            || run.RunFacilityWarehouseId != 4
            || run.RunFacilityCodeSnapshot != EmploymentFacilities.Wp
            || run.RunFacilityAssignmentSource != "ReviewedProductionBackfill:20260804-run40"
            || run.RunFacilityAssignedByUserId is not null
            || run.RunFacilityAssignedAt != FacilityAssignedAt
            || run.RunProjectionId is not null
            || run.CanceledAt is not null)
        {
            issues.Add("Actual Run #1 no longer matches the reviewed active revision, timestamp, operator, facility, or historical assignment evidence.");
        }
    }

    private static void ValidateRevision(ActualRunRevision revision, List<string> issues)
    {
        if (revision.Id != TargetRevisionId
            || revision.RevisionNumber != 1
            || revision.OperationType != ActualRunRevisionTypes.Create
            || revision.OperationKey != "2dc80673fb2a40c8a3a4fbd3a75658b0"
            || !revision.IsCurrent
            || revision.CreatedAt != HistoricalCreatedAt
            || revision.CreatedByUserId != 8
            || revision.CreatedByUser?.Email != July28ActualRunExpectationBackfillConstants.HistoricalOperatorEmail)
        {
            issues.Add("Actual Run #1 current revision no longer matches reviewed revision #1 evidence.");
        }
    }

    private static void ValidateEntries(IReadOnlyList<BinsRunEntry> entries, ActualRunRevision? revision, List<string> issues)
    {
        if (entries.Count != 1 || entries[0].Id != TargetEntryId)
        {
            issues.Add("Actual Run #1 entry set is not exactly reviewed Bins Run entry 31.");
            return;
        }
        var entry = entries[0];
        if (revision is null
            || entry.ActualRunRevisionId != revision.Id
            || entry.TransactionType != ActualRunTransactionTypes.Depletion
            || entry.IsReversed
            || entry.ReversesBinsRunEntryId is not null
            || entry.BinsRun != 184
            || entry.PreviousAvailableBins != 261
            || entry.NewAvailableBins != 77
            || entry.InventoryAdjustmentId != TargetAdjustmentId
            || entry.SourceInventoryAdjustmentId != TargetSourceAdjustmentId
            || entry.WarehouseId != 4
            || entry.RoomId != 1
            || entry.CropYear != 2026
            || entry.GrowerLotId != 398
            || entry.FruitProfileId != 17
            || entry.GrowerName != "WP Orchard Conventional"
            || entry.GrowerNumberSnapshot != "1084"
            || entry.LotNumber != "1084"
            || entry.RunAt != HistoricalRunAt
            || entry.CreatedAt != HistoricalCreatedAt
            || entry.CreatedByUserId != 8
            || entry.CreatedByUser?.Email != July28ActualRunExpectationBackfillConstants.HistoricalOperatorEmail
            || entry.ReportingFacilityWarehouseId != 4
            || entry.ReportingFacilityCodeSnapshot != EmploymentFacilities.Wp
            || entry.ReportingFacilityAssignmentSource != "ReviewedProductionBackfill:20260804-run40"
            || entry.ReportingFacilityAssignedByUserId is not null
            || entry.ReportingFacilityAssignedAt != FacilityAssignedAt
            || entry.ReportingCropYearSnapshot != 2026
            || entry.ReportingFruitProfileIdSnapshot != 17
            || entry.ReportingVarietyCodeSnapshot != "BART"
            || entry.ProductionTypeSnapshot != "Conventional"
            || entry.IsOrganicSnapshot != false
            || entry.FruitProfile?.Name != "Bartlett"
            || entry.FruitProfile.VarietyCode != "BART"
            || entry.FruitProfile.ProductionType != "Conventional"
            || entry.FruitProfile.IsOrganic)
        {
            issues.Add("Bins Run entry 31 no longer matches the reviewed 184-bin room, crop year, grower/lot, variety/organic, operator, adjustment, or timestamp evidence.");
        }
    }

    private static void ValidateAdjustment(
        RoomInventoryAdjustment? adjustment,
        RoomInventoryAdjustment? sourceAdjustment,
        List<string> issues)
    {
        if (adjustment is null
            || adjustment.ActualRunId != TargetRunId
            || adjustment.ActualRunRevisionId != TargetRevisionId
            || adjustment.OldBinCount != 261
            || adjustment.ChangeAmount != -184
            || adjustment.NewBinCount != 77
            || adjustment.WarehouseId != 4
            || adjustment.RoomId != 1
            || adjustment.FruitProfileId != 17
            || adjustment.GrowerName != "WP Orchard Conventional"
            || adjustment.LotNumber != "1084"
            || adjustment.VarietyCode != "BART"
            || adjustment.AdjustmentType != "BinsRun"
            || adjustment.Source != "Actual Run #1"
            || adjustment.AdjustmentAt != HistoricalRunAt
            || adjustment.CreatedByUserId != 8
            || adjustment.CreatedAt != DepletionAdjustmentCreatedAt
            || adjustment.InventoryInvariantVersion != 1)
        {
            issues.Add("Linked depletion adjustment 115 no longer matches the reviewed -184-bin Actual Run #1 evidence.");
        }
        if (sourceAdjustment is null
            || sourceAdjustment.Id != TargetSourceAdjustmentId
            || sourceAdjustment.ReceiptId != 122
            || sourceAdjustment.WarehouseId != 4
            || sourceAdjustment.RoomId != 1
            || sourceAdjustment.CropYear != 2026
            || sourceAdjustment.GrowerLotId != 398
            || sourceAdjustment.FruitProfileId != 17
            || sourceAdjustment.GrowerName != "WP Orchard Conventional"
            || sourceAdjustment.LotNumber != "1084"
            || sourceAdjustment.OldBinCount is not null
            || sourceAdjustment.ChangeAmount != 56
            || sourceAdjustment.NewBinCount != 56
            || sourceAdjustment.AdjustmentType != "ReceiptAdd"
            || sourceAdjustment.Source != "Receiving inventory added"
            || sourceAdjustment.AdjustmentAt != SourceAdjustmentAt
            || sourceAdjustment.CreatedByUserId != 5
            || sourceAdjustment.CreatedAt != SourceAdjustmentCreatedAt
            || sourceAdjustment.ActualRunId is not null
            || sourceAdjustment.ActualRunRevisionId is not null
            || sourceAdjustment.InventoryInvariantVersion != 0)
        {
            issues.Add("Reviewed source adjustment 114 is missing or changed.");
        }
    }

    private static void ValidateAppliedShape(
        RunExpectation expectation,
        IReadOnlyList<AuditLog> audits,
        IReadOnlyList<PackoutRun> packouts,
        List<string> issues)
    {
        var adminCreator = expectation.CreatedByUser is not null
            && expectation.CreatedByUser.IsActive
            && expectation.CreatedByUser.UserRoles.Any(x => x.Role.IsActive && x.Role.Name == BuiltInRoleNames.Admin);
        if (expectation.ActualRunId != TargetRunId
            || expectation.ActualRunRevisionId != TargetRevisionId
            || expectation.RevisionNumber != 1
            || expectation.FacilityWarehouseId != 4
            || expectation.FacilitySnapshot != EmploymentFacilities.Wp
            || expectation.RunAtSnapshot != HistoricalRunAt
            || expectation.TotalBins != 184
            || expectation.CalculationVersion != RunExpectationCalculationVersions.Current
            || expectation.CalculatedAt <= HistoricalCreatedAt
            || !adminCreator)
        {
            issues.Add("The existing Run Expectation does not match the reviewed historical backfill structure.");
        }
        if (expectation.Sources.Count != 1)
        {
            issues.Add("The existing Run Expectation must have exactly one source.");
        }
        else
        {
            var source = expectation.Sources.Single();
            if (source.BinsRunEntryId != TargetEntryId
                || source.WarehouseId != 4
                || source.RoomId != 1
                || source.FacilitySnapshot != EmploymentFacilities.Wp
                || source.CropYearSnapshot != 2026
                || source.GrowerLotId != 398
                || source.FruitProfileId != 17
                || source.GrowerSnapshot != "WP Orchard Conventional"
                || source.LotSnapshot != "1084"
                || source.VarietySnapshot != "Bartlett"
                || source.ProductionTypeSnapshot != "Conventional"
                || source.IsOrganicSnapshot
                || source.BinsContributed != 184
                || source.ContributionPercent != 100m)
            {
                issues.Add("The existing Run Expectation source does not exactly represent Bins Run entry 31.");
            }
        }
        if (audits.Count != 1)
        {
            issues.Add("The historical expectation backfill audit is missing or duplicated.");
        }
        if (packouts.Any(x => x.RunExpectationId != expectation.Id))
        {
            issues.Add("An existing PackoutRun is not linked to the reviewed backfilled expectation and revision.");
        }
    }

    private async Task<July28ActualRunExpectationIntegrity> CaptureIntegrityAsync(CancellationToken cancellationToken)
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
            x.AdjustmentAt,
            x.CreatedByUserId,
            x.CreatedAt,
            x.InventoryInvariantVersion,
            x.InventoryOperationKey,
            x.RoomTransferId,
            x.ReceiptInventoryOverrideId,
            x.ActualRunId,
            x.ActualRunRevisionId
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
            x.PreviousAvailableBins,
            x.BinsRun,
            x.NewAvailableBins,
            x.RunAt,
            x.CreatedByUserId,
            x.CreatedAt,
            x.IsReversed,
            x.ReversesBinsRunEntryId,
            x.ActualRunId,
            x.ActualRunRevisionId,
            x.TransactionType,
            x.ReportingFacilityWarehouseId,
            x.ReportingFacilityCodeSnapshot,
            x.ReportingCropYearSnapshot,
            x.ReportingFruitProfileIdSnapshot,
            x.ReportingVarietyCodeSnapshot,
            x.ProductionTypeSnapshot,
            x.IsOrganicSnapshot,
            x.GrowerNumberSnapshot
        }).ToListAsync(cancellationToken);
        var receipts = await dbContext.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new
        {
            x.Id,
            x.CropYear,
            x.ReceivedAt,
            x.CompuTechReceiptId,
            x.ReceiptType,
            x.WarehouseId,
            x.RoomId,
            x.FruitProfileId,
            x.GrowerLotId,
            x.CanonicalOrchardBlockId,
            x.GrowerNumber,
            x.PoolStart,
            x.GrowerName,
            x.LotCode,
            x.BinCount,
            x.CreatedAt,
            x.UpdatedAt,
            x.ConcurrencyVersion,
            x.IsTestData,
            x.IsDeleted,
            x.DeletedAt,
            x.DeletedByUserId,
            x.DeleteReason
        }).ToListAsync(cancellationToken);
        var transfers = await dbContext.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new
        {
            x.Id,
            x.OperationKey,
            x.SourceWarehouseId,
            x.SourceRoomId,
            x.DestinationWarehouseId,
            x.DestinationRoomId,
            x.CropYear,
            x.GrowerLotId,
            x.FruitProfileId,
            x.GrowerName,
            x.LotNumber,
            x.PoolStart,
            x.VarietyCode,
            x.InventoryStatus,
            x.BinCount,
            x.Reason,
            x.TransferredAt,
            x.CreatedByUserId,
            x.CreatedAt,
            x.IsReversed,
            x.ReversedAt,
            x.ReversedByUserId,
            x.ReverseReason,
            x.ReversesRoomTransferId
        }).ToListAsync(cancellationToken);
        var growerLots = await dbContext.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new
        {
            x.Id,
            x.Grower,
            x.LotNumber,
            x.PoolStart,
            x.Notes,
            x.IsActive,
            x.CreatedAt,
            x.UpdatedAt
        }).ToListAsync(cancellationToken);
        var currentInventory = adjustments
            .GroupBy(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.GrowerName, x.LotNumber, x.PoolStart, x.VarietyCode, x.InventoryStatus })
            .Select(x => new
            {
                x.Key.WarehouseId,
                x.Key.RoomId,
                x.Key.CropYear,
                x.Key.GrowerLotId,
                x.Key.FruitProfileId,
                x.Key.GrowerName,
                x.Key.LotNumber,
                x.Key.PoolStart,
                x.Key.VarietyCode,
                x.Key.InventoryStatus,
                Bins = x.Sum(y => y.ChangeAmount)
            })
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.RoomId).ThenBy(x => x.CropYear)
            .ThenBy(x => x.GrowerLotId).ThenBy(x => x.FruitProfileId).ThenBy(x => x.GrowerName)
            .ThenBy(x => x.LotNumber).ThenBy(x => x.PoolStart).ThenBy(x => x.VarietyCode)
            .ThenBy(x => x.InventoryStatus)
            .ToList();
        var physicalRuns = entries
            .Where(x => x.ActualRunId is not null && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
            .GroupBy(x => x.ActualRunId!.Value)
            .Select(x => new { ActualRunId = x.Key, Bins = x.Sum(y => y.BinsRun), EntryIds = x.Select(y => y.Id).OrderBy(y => y).ToArray() })
            .OrderBy(x => x.ActualRunId)
            .ToList();
        var reporting = await CaptureReportingAsync(cancellationToken);

        return new(
            adjustments.Count,
            adjustments.Sum(x => x.ChangeAmount),
            Sha256(JsonSerializer.Serialize(adjustments)),
            entries.Count,
            entries.Sum(x => x.BinsRun),
            Sha256(JsonSerializer.Serialize(entries)),
            Sha256(JsonSerializer.Serialize(currentInventory)),
            receipts.Count,
            receipts.Sum(x => x.BinCount),
            Sha256(JsonSerializer.Serialize(receipts)),
            transfers.Count,
            transfers.Sum(x => x.BinCount),
            Sha256(JsonSerializer.Serialize(transfers)),
            growerLots.Count,
            Sha256(JsonSerializer.Serialize(growerLots)),
            physicalRuns.Sum(x => x.Bins),
            Sha256(JsonSerializer.Serialize(physicalRuns)),
            reporting);
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

    private static July28ActualRunExpectationEvidence BuildEvidence(
        ActualRun run,
        ActualRunRevision revision,
        IReadOnlyList<BinsRunEntry> entries) => new(
            run.Id,
            run.RunAt,
            run.Status,
            run.CurrentRevisionNumber,
            revision.Id,
            revision.OperationType,
            revision.OperationKey,
            revision.IsCurrent,
            run.CreatedByUserId ?? 0,
            run.CreatedByUser?.Email ?? "",
            run.RunFacilityWarehouseId ?? 0,
            run.RunFacilityCodeSnapshot ?? "",
            run.RunFacilityAssignmentSource ?? "",
            entries.Select(x => x.Id).ToList(),
            entries.Select(x => x.BinsRun).ToList(),
            entries.Select(x => x.InventoryAdjustmentId).ToList(),
            entries.Where(x => x.SourceInventoryAdjustmentId is not null).Select(x => x.SourceInventoryAdjustmentId!.Value).ToList(),
            entries.Sum(x => x.BinsRun),
            entries.Select(x => x.RoomId).Distinct().OrderBy(x => x).ToList(),
            entries.Where(x => x.CropYear is not null).Select(x => x.CropYear!.Value).Distinct().OrderBy(x => x).ToList(),
            entries.Where(x => !string.IsNullOrWhiteSpace(x.GrowerNumberSnapshot)).Select(x => x.GrowerNumberSnapshot!).Distinct().OrderBy(x => x).ToList(),
            entries.Select(x => x.LotNumber).Distinct().OrderBy(x => x).ToList(),
            entries.Where(x => x.FruitProfileId is not null).Select(x => x.FruitProfileId!.Value).Distinct().OrderBy(x => x).ToList(),
            entries.Where(x => !string.IsNullOrWhiteSpace(x.ReportingVarietyCodeSnapshot)).Select(x => x.ReportingVarietyCodeSnapshot!).Distinct().OrderBy(x => x).ToList(),
            entries.Where(x => !string.IsNullOrWhiteSpace(x.ProductionTypeSnapshot)).Select(x => x.ProductionTypeSnapshot!).Distinct().OrderBy(x => x).ToList(),
            entries.Where(x => x.IsOrganicSnapshot is not null).Select(x => x.IsOrganicSnapshot!.Value).Distinct().OrderBy(x => x).ToList());

    private static July28ActualRunExpectationBackfillResult Failed(
        string message,
        July28ActualRunExpectationBackfillPreflight preflight) =>
        new(false, false, false, message, preflight.RunExpectationId, preflight.SourceCount, preflight);

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
