using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class ActualRun3ReportingIdentityCorrectionConstants
{
    public const string CommandName = "--correct-actual-run-3-reporting-identity";
    public const string ApplyAuthorizationToken = "APPLY_REVIEWED_ACTUAL_RUN_3_REPORTING_IDENTITY";
    public const string AssignmentSource = "ReviewedProdCorrection:20260826-run3-reporting-id";
    public const string AuditEntityName = "ActualRunReportingIdentityCorrection";
    public const string AuditEntityKey = "actual-run-3-entry-33";
    public const string AuditSource = "CropQc.Web reviewed Actual Run 3 reporting identity correction command";
}

public sealed record ActualRun3ReportingIdentityCorrectionRequest(
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

public sealed record ActualRun3ReportingIdentityTargetState(
    int? ReportingFacilityWarehouseId,
    string? ReportingFacilityCodeSnapshot,
    string? ReportingFacilityAssignmentSource,
    DateTimeOffset? ReportingFacilityAssignedAt,
    int? ReportingFacilityAssignedByUserId,
    string? ProductionTypeSnapshot,
    bool? IsOrganicSnapshot,
    string? GrowerNumberSnapshot,
    int? ReportingCropYearSnapshot,
    int? ReportingFruitProfileIdSnapshot,
    string? ReportingVarietyCodeSnapshot);

public sealed record ActualRun3ReportingIdentityEvidence(
    long ActualRunId,
    string ActualRunStatus,
    int CurrentRevisionNumber,
    DateTimeOffset RunAt,
    DateOnly PacificRunDate,
    string? RunFacilityCode,
    string? SalesDesk,
    long RevisionId,
    int RevisionNumber,
    bool RevisionIsCurrent,
    long BinsRunEntryId,
    long? EntryActualRunId,
    long? EntryRevisionId,
    int BinsRun,
    bool IsReversed,
    string TransactionType,
    int? FruitProfileId,
    string? VarietyCode,
    int? GrowerLotId,
    string LotNumber,
    int? CropYear,
    int WarehouseId,
    long InventoryAdjustmentId,
    int? EntryPreviousAvailableBins,
    int? EntryNewAvailableBins,
    string? EntryGrowerName,
    int AdjustmentChangeAmount,
    int? AdjustmentFruitProfileId,
    int? AdjustmentGrowerLotId,
    string AdjustmentLotNumber,
    string? AdjustmentVarietyCode,
    int? AdjustmentCropYear,
    int AdjustmentWarehouseId,
    long? AdjustmentActualRunId,
    long? AdjustmentRevisionId,
    int? AdjustmentOldBinCount,
    int AdjustmentNewBinCount,
    string AdjustmentType,
    string? AdjustmentSource,
    string? AdjustmentGrowerName,
    string FruitProfileVarietyCode,
    string FruitProfileProductionType,
    bool FruitProfileIsOrganic,
    string GrowerLotGrower,
    string GrowerLotNumber);

public sealed record ActualRun3ReportingIdentityCorrectionPreflight(
    string State,
    DateTimeOffset GeneratedAtUtc,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Issues,
    ActualRun3ReportingIdentityEvidence? Evidence,
    ActualRun3ReportingIdentityTargetState? Target,
    int AuditCount);

public sealed record ActualRun3ReportingIdentityCorrectionResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    ActualRun3ReportingIdentityCorrectionPreflight Preflight);

public interface IActualRun3ReportingIdentityCorrectionService
{
    Task<ActualRun3ReportingIdentityCorrectionPreflight> PreflightAsync(CancellationToken cancellationToken);
    Task<ActualRun3ReportingIdentityCorrectionResult> RunAsync(
        ActualRun3ReportingIdentityCorrectionRequest request,
        CancellationToken cancellationToken);
}

public sealed class ActualRun3ReportingIdentityCorrectionService(
    CropQcDbContext dbContext,
    AppEnvironmentOptions appEnvironment,
    IBusinessTimeService businessTime,
    ILogger<ActualRun3ReportingIdentityCorrectionService> logger) : IActualRun3ReportingIdentityCorrectionService
{
    private const long TargetRunId = 3;
    private const long TargetRevisionId = 3;
    private const long TargetEntryId = 33;
    private const long TargetAdjustmentId = 117;
    private const int TargetWarehouseId = 4;
    private const int TargetFruitProfileId = 19;
    private const int TargetGrowerLotId = 394;
    private static readonly DateTimeOffset TargetRunAt = DateTimeOffset.Parse("2026-07-31T00:33:00Z");
    private static readonly DateOnly TargetPacificDate = new(2026, 7, 30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ActualRun3ReportingIdentityCorrectionPreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var run = await dbContext.ActualRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetRunId, cancellationToken);
        var revision = await dbContext.ActualRunRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetRevisionId, cancellationToken);
        var entry = await dbContext.BinsRunEntries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetEntryId, cancellationToken);
        var adjustment = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetAdjustmentId, cancellationToken);
        var fruitProfile = await dbContext.FruitProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetFruitProfileId, cancellationToken);
        var growerLot = await dbContext.GrowerLots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == TargetGrowerLotId, cancellationToken);
        var audits = await dbContext.AuditLogs.AsNoTracking()
            .Where(x => x.EntityName == ActualRun3ReportingIdentityCorrectionConstants.AuditEntityName
                && x.EntityKey == ActualRun3ReportingIdentityCorrectionConstants.AuditEntityKey)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        ValidateRun(run, issues);
        ValidateRevision(revision, issues);
        ValidateEntry(entry, issues);
        ValidateAdjustment(adjustment, issues);
        ValidateFruitProfile(fruitProfile, issues);
        ValidateGrowerLot(growerLot, issues);

        ActualRun3ReportingIdentityEvidence? evidence = null;
        ActualRun3ReportingIdentityTargetState? target = null;
        if (run is not null && revision is not null && entry is not null && adjustment is not null
            && fruitProfile is not null && growerLot is not null)
        {
            evidence = BuildEvidence(run, revision, entry, adjustment, fruitProfile, growerLot);
            target = BuildTarget(entry);
        }

        var allNull = target is not null && TargetIsCompletelyNull(target);
        var alreadyApplied = target is not null && TargetIsExactApplied(target);
        if (allNull && audits.Count != 0)
        {
            issues.Add("A correction audit exists while the reporting identity target remains null.");
        }
        else if (alreadyApplied)
        {
            ValidateAppliedAudit(audits, target!, issues);
        }
        else if (target is not null && !allNull)
        {
            issues.Add("Bins Run entry 33 has partial or conflicting reporting identity. Refusing to overwrite it.");
        }

        var targetFingerprint = Sha256(JsonSerializer.Serialize(new { evidence, target, AuditCount = audits.Count }));
        var protectedFingerprint = Sha256(JsonSerializer.Serialize(evidence));
        var state = issues.Count > 0 ? "Refused" : alreadyApplied ? "AlreadyApplied" : "Ready";
        return new(
            state,
            businessTime.UtcNow,
            targetFingerprint,
            protectedFingerprint,
            issues,
            evidence,
            target,
            audits.Count);
    }

    public async Task<ActualRun3ReportingIdentityCorrectionResult> RunAsync(
        ActualRun3ReportingIdentityCorrectionRequest request,
        CancellationToken cancellationToken)
    {
        var preflight = await PreflightAsync(cancellationToken);
        if (preflight.State == "Refused")
        {
            return Failed("Preflight refused the Actual Run #3 reporting identity correction. No data was changed.", preflight);
        }
        if (preflight.State == "AlreadyApplied")
        {
            return new(true, false, true, "The exact reviewed Actual Run #3 reporting identity correction is already applied; zero writes were made.", preflight);
        }
        if (!request.Apply)
        {
            return new(true, false, false, "Dry-run passed for the exact reviewed Actual Run #3 / Bins Run entry #33 reporting identity correction.", preflight);
        }
        if (!string.Equals(request.AuthorizationToken, ActualRun3ReportingIdentityCorrectionConstants.ApplyAuthorizationToken, StringComparison.Ordinal))
        {
            return Failed("Apply requires the exact reviewed authorization token.", preflight);
        }
        if (appEnvironment.IsProduction && !request.ConfirmProduction)
        {
            return Failed("Production apply requires --confirm-production.", preflight);
        }
        if (!appEnvironment.IsProduction && !request.ConfirmDisposableRestore)
        {
            return Failed("Non-production apply requires --confirm-disposable-restore.", preflight);
        }
        if (request.VerifiedBackupRunId is null || string.IsNullOrWhiteSpace(request.VerifiedBackupPackageSha256))
        {
            return Failed("Apply requires the exact fully verified backup run ID and package SHA-256.", preflight);
        }
        var backup = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId.Value, cancellationToken);
        if (backup is null
            || backup.Status != BackupRunStatuses.Succeeded
            || backup.VerifiedAt is null
            || backup.RetentionProcessedAt is null
            || backup.LeaseReleasedAt is null
            || backup.PrunedAt is not null
            || !string.Equals(backup.Sha256, request.VerifiedBackupPackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Failed("The supplied backup is not fully verified, retained, lease-released, and SHA-matched.", preflight);
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Failed("Apply requires a correction reason.", preflight);
        }
        if (!string.Equals(request.ExpectedTargetFingerprint, preflight.TargetFingerprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return Failed("The reviewed target or protected fingerprint does not match current database state.", preflight);
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
                throw new InvalidOperationException("Database state changed after reviewed preflight.");
            }

            var entry = await dbContext.BinsRunEntries.SingleAsync(x => x.Id == TargetEntryId, cancellationToken);
            var now = businessTime.UtcNow;
            entry.ReportingFacilityWarehouseId = TargetWarehouseId;
            entry.ReportingFacilityCodeSnapshot = EmploymentFacilities.Wp;
            entry.ReportingFacilityAssignmentSource = ActualRun3ReportingIdentityCorrectionConstants.AssignmentSource;
            entry.ReportingFacilityAssignedAt = now;
            entry.ReportingFacilityAssignedByUserId = null;
            entry.ProductionTypeSnapshot = "Organic";
            entry.IsOrganicSnapshot = true;
            entry.GrowerNumberSnapshot = "1080";
            entry.ReportingCropYearSnapshot = 2026;
            entry.ReportingFruitProfileIdSnapshot = TargetFruitProfileId;
            entry.ReportingVarietyCodeSnapshot = "ORBA";

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = correctionAdmin.Id,
                Action = "ReviewedProductionCorrection",
                EntityName = ActualRun3ReportingIdentityCorrectionConstants.AuditEntityName,
                EntityKey = ActualRun3ReportingIdentityCorrectionConstants.AuditEntityKey,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    ActualRunId = TargetRunId,
                    ActualRunRevisionId = TargetRevisionId,
                    BinsRunEntryId = TargetEntryId,
                    ReportingIdentity = preflight.Target,
                    preflight.TargetFingerprint,
                    preflight.ProtectedFingerprint,
                    BackupRunId = backup.Id,
                    BackupPackageSha256 = backup.Sha256
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    ActualRunId = TargetRunId,
                    ActualRunRevisionId = TargetRevisionId,
                    BinsRunEntryId = TargetEntryId,
                    entry.ReportingFacilityWarehouseId,
                    entry.ReportingFacilityCodeSnapshot,
                    entry.ReportingFacilityAssignmentSource,
                    entry.ReportingFacilityAssignedAt,
                    entry.ReportingFacilityAssignedByUserId,
                    entry.ProductionTypeSnapshot,
                    entry.IsOrganicSnapshot,
                    entry.GrowerNumberSnapshot,
                    entry.ReportingCropYearSnapshot,
                    entry.ReportingFruitProfileIdSnapshot,
                    entry.ReportingVarietyCodeSnapshot,
                    CorrectionAdministrator = correctionAdmin.Email,
                    Reason = request.Reason.Trim(),
                    Semantics = "Reporting identity snapshot correction only; physical run, inventory, quantity, date, grower lot, fruit profile, and Sales Desk remain unchanged."
                }, JsonOptions),
                SourceApplication = ActualRun3ReportingIdentityCorrectionConstants.AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.ChangeTracker.Clear();
            var postflight = await PreflightAsync(cancellationToken);
            if (postflight.State != "AlreadyApplied"
                || !string.Equals(postflight.ProtectedFingerprint, preflight.ProtectedFingerprint, StringComparison.Ordinal)
                || postflight.AuditCount != 1)
            {
                throw new InvalidOperationException("Focused post-apply verification failed.");
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Reviewed Actual Run #3 reporting identity correction applied. EntryId={EntryId} BackupRunId={BackupRunId} CorrectionAdmin={CorrectionAdmin}",
                TargetEntryId,
                backup.Id,
                correctionAdmin.Email);
            return new(true, true, false, "The exact reviewed Actual Run #3 reporting identity correction completed successfully.", postflight);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Actual Run #3 reporting identity correction failed and was rolled back.");
            dbContext.ChangeTracker.Clear();
            return Failed("The correction failed and was rolled back. No partial reporting identity was retained.", await PreflightAsync(cancellationToken));
        }
    }

    private void ValidateRun(ActualRun? run, List<string> issues)
    {
        if (run is null)
        {
            issues.Add("Actual Run #3 is missing.");
            return;
        }
        if (run.Status != ActualRunStatuses.Active) issues.Add("Actual Run #3 is not Active.");
        if (run.CurrentRevisionNumber != 1) issues.Add("Actual Run #3 current revision is not 1.");
        if (run.RunAt != TargetRunAt || businessTime.PacificDate(run.RunAt) != TargetPacificDate)
            issues.Add("Actual Run #3 RunAt no longer represents July 30, 2026 Pacific at the reviewed instant.");
        if (run.RunFacilityWarehouseId != TargetWarehouseId || run.RunFacilityCodeSnapshot != EmploymentFacilities.Wp)
            issues.Add("Actual Run #3 facility is not the reviewed WP / Warehouse 4 identity.");
        if (run.SalesDeskNameSnapshot != "Domex") issues.Add("Actual Run #3 Sales Desk is not Domex.");
    }

    private static void ValidateRevision(ActualRunRevision? revision, List<string> issues)
    {
        if (revision is null)
        {
            issues.Add("Actual Run revision #3 is missing.");
            return;
        }
        if (revision.ActualRunId != TargetRunId || revision.RevisionNumber != 1 || !revision.IsCurrent)
            issues.Add("Actual Run revision #3 is not the current revision 1 for Actual Run #3.");
    }

    private static void ValidateEntry(BinsRunEntry? entry, List<string> issues)
    {
        if (entry is null)
        {
            issues.Add("Bins Run entry #33 is missing.");
            return;
        }
        if (entry.ActualRunId != TargetRunId || entry.ActualRunRevisionId != TargetRevisionId)
            issues.Add("Bins Run entry #33 has the wrong Actual Run or revision parent.");
        if (entry.BinsRun != 173 || entry.IsReversed || entry.TransactionType != ActualRunTransactionTypes.Depletion)
            issues.Add("Bins Run entry #33 is not the reviewed active 173-bin depletion.");
        if (entry.RunAt != TargetRunAt) issues.Add("Bins Run entry #33 RunAt changed.");
        if (entry.FruitProfileId != TargetFruitProfileId || entry.VarietyCode != "ORBA")
            issues.Add("Bins Run entry #33 fruit identity is not Fruit Profile 19 / ORBA.");
        if (entry.GrowerLotId != TargetGrowerLotId || entry.LotNumber != "1080")
            issues.Add("Bins Run entry #33 grower-lot identity is not Grower Lot 394 / 1080.");
        if (entry.CropYear != 2026 || entry.WarehouseId != TargetWarehouseId)
            issues.Add("Bins Run entry #33 crop year or warehouse changed.");
        if (entry.InventoryAdjustmentId != TargetAdjustmentId)
            issues.Add("Bins Run entry #33 does not point to Room Inventory Adjustment #117.");
        if (entry.PreviousAvailableBins != 225 || entry.NewAvailableBins != 52 || entry.GrowerName != "WINDY POINT")
            issues.Add("Bins Run entry #33 no longer has the reviewed WINDY POINT 225-to-52 balance evidence.");
    }

    private static void ValidateAdjustment(RoomInventoryAdjustment? adjustment, List<string> issues)
    {
        if (adjustment is null)
        {
            issues.Add("Room Inventory Adjustment #117 is missing.");
            return;
        }
        if (adjustment.ChangeAmount != -173
            || adjustment.FruitProfileId != TargetFruitProfileId
            || adjustment.GrowerLotId != TargetGrowerLotId
            || adjustment.LotNumber != "1080"
            || adjustment.VarietyCode != "ORBA"
            || adjustment.CropYear != 2026
            || adjustment.WarehouseId != TargetWarehouseId
            || adjustment.ActualRunId != TargetRunId
            || adjustment.ActualRunRevisionId != TargetRevisionId
            || adjustment.OldBinCount != 225
            || adjustment.NewBinCount != 52
            || adjustment.AdjustmentType != BinsRunService.AdjustmentType
            || adjustment.Source != "Actual Run #3"
            || adjustment.GrowerName != "WINDY POINT")
        {
            issues.Add("Room Inventory Adjustment #117 no longer proves the exact reviewed 173-bin ORBA depletion.");
        }
    }

    private static void ValidateFruitProfile(FruitProfile? fruitProfile, List<string> issues)
    {
        if (fruitProfile is null
            || fruitProfile.VarietyCode != "ORBA"
            || fruitProfile.ProductionType != "Organic"
            || !fruitProfile.IsOrganic)
        {
            issues.Add("Fruit Profile #19 no longer proves ORBA / Organic / IsOrganic=true.");
        }
    }

    private static void ValidateGrowerLot(GrowerLot? growerLot, List<string> issues)
    {
        if (growerLot is null || growerLot.LotNumber != "1080" || growerLot.Grower != "WP ORCHARD ORG CHIL")
        {
            issues.Add("Grower Lot #394 no longer proves WP ORCHARD ORG CHIL / grower number 1080.");
        }
    }

    private static void ValidateAppliedAudit(
        IReadOnlyList<AuditLog> audits,
        ActualRun3ReportingIdentityTargetState target,
        List<string> issues)
    {
        if (audits.Count != 1
            || audits[0].Action != "ReviewedProductionCorrection"
            || audits[0].SourceApplication != ActualRun3ReportingIdentityCorrectionConstants.AuditSource
            || audits[0].CreatedAt != target.ReportingFacilityAssignedAt)
        {
            issues.Add("The applied reporting identity does not have exactly one matching reviewed correction audit.");
        }
    }

    private ActualRun3ReportingIdentityEvidence BuildEvidence(
        ActualRun run,
        ActualRunRevision revision,
        BinsRunEntry entry,
        RoomInventoryAdjustment adjustment,
        FruitProfile fruitProfile,
        GrowerLot growerLot) =>
        new(
            run.Id,
            run.Status,
            run.CurrentRevisionNumber,
            run.RunAt,
            businessTime.PacificDate(run.RunAt),
            run.RunFacilityCodeSnapshot,
            run.SalesDeskNameSnapshot,
            revision.Id,
            revision.RevisionNumber,
            revision.IsCurrent,
            entry.Id,
            entry.ActualRunId,
            entry.ActualRunRevisionId,
            entry.BinsRun,
            entry.IsReversed,
            entry.TransactionType,
            entry.FruitProfileId,
            entry.VarietyCode,
            entry.GrowerLotId,
            entry.LotNumber,
            entry.CropYear,
            entry.WarehouseId,
            entry.InventoryAdjustmentId,
            entry.PreviousAvailableBins,
            entry.NewAvailableBins,
            entry.GrowerName,
            adjustment.ChangeAmount,
            adjustment.FruitProfileId,
            adjustment.GrowerLotId,
            adjustment.LotNumber,
            adjustment.VarietyCode,
            adjustment.CropYear,
            adjustment.WarehouseId,
            adjustment.ActualRunId,
            adjustment.ActualRunRevisionId,
            adjustment.OldBinCount,
            adjustment.NewBinCount,
            adjustment.AdjustmentType,
            adjustment.Source,
            adjustment.GrowerName,
            fruitProfile.VarietyCode,
            fruitProfile.ProductionType,
            fruitProfile.IsOrganic,
            growerLot.Grower,
            growerLot.LotNumber);

    private static ActualRun3ReportingIdentityTargetState BuildTarget(BinsRunEntry entry) =>
        new(
            entry.ReportingFacilityWarehouseId,
            entry.ReportingFacilityCodeSnapshot,
            entry.ReportingFacilityAssignmentSource,
            entry.ReportingFacilityAssignedAt,
            entry.ReportingFacilityAssignedByUserId,
            entry.ProductionTypeSnapshot,
            entry.IsOrganicSnapshot,
            entry.GrowerNumberSnapshot,
            entry.ReportingCropYearSnapshot,
            entry.ReportingFruitProfileIdSnapshot,
            entry.ReportingVarietyCodeSnapshot);

    private static bool TargetIsCompletelyNull(ActualRun3ReportingIdentityTargetState target) =>
        target.ReportingFacilityWarehouseId is null
        && target.ReportingFacilityCodeSnapshot is null
        && target.ReportingFacilityAssignmentSource is null
        && target.ReportingFacilityAssignedAt is null
        && target.ReportingFacilityAssignedByUserId is null
        && target.ProductionTypeSnapshot is null
        && target.IsOrganicSnapshot is null
        && target.GrowerNumberSnapshot is null
        && target.ReportingCropYearSnapshot is null
        && target.ReportingFruitProfileIdSnapshot is null
        && target.ReportingVarietyCodeSnapshot is null;

    private static bool TargetIsExactApplied(ActualRun3ReportingIdentityTargetState target) =>
        target.ReportingFacilityWarehouseId == TargetWarehouseId
        && target.ReportingFacilityCodeSnapshot == EmploymentFacilities.Wp
        && target.ReportingFacilityAssignmentSource == ActualRun3ReportingIdentityCorrectionConstants.AssignmentSource
        && target.ReportingFacilityAssignedAt is not null
        && target.ReportingFacilityAssignedByUserId is null
        && target.ProductionTypeSnapshot == "Organic"
        && target.IsOrganicSnapshot == true
        && target.GrowerNumberSnapshot == "1080"
        && target.ReportingCropYearSnapshot == 2026
        && target.ReportingFruitProfileIdSnapshot == TargetFruitProfileId
        && target.ReportingVarietyCodeSnapshot == "ORBA";

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ActualRun3ReportingIdentityCorrectionResult Failed(
        string message,
        ActualRun3ReportingIdentityCorrectionPreflight preflight) =>
        new(false, false, false, message, preflight);
}
