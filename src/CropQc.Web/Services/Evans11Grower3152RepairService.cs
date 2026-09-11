using System.Data;
using System.Globalization;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record Evans11Grower3152RepairResult(
    string State,
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    long? BackupRunId = null,
    string? BackupSha256 = null,
    Guid? CorrectionId = null);

public sealed class Evans11Grower3152RepairService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IRoomTreatmentService treatmentService,
    IInventoryDeductionInvariantService invariant,
    IBackupService backupService,
    IBusinessTimeService businessTime,
    IConfiguration configuration,
    ILogger<Evans11Grower3152RepairService> logger)
{
    private const long ReceiptId = 1391;
    private const string ReceiptNumber = "TR109381";
    private const int RoomId = 21;
    private const int WarehouseId = 1;
    private const int CropYear = 2026;
    private const int SourceGrowerLotId = 513;
    private const int TargetGrowerLotId = 511;
    private const int FruitProfileId = 2;
    private const int Bins = 10;
    private const long PhantomTargetSegmentId = 38;
    private const long SourceTreatmentSegmentId = 70;
    private const string ExistingCorrectionOperationKey = "6f24ff89cfcf427aa43d4bee4f07a14d";
    private static readonly Guid ExistingCorrectionId = Guid.Parse("7dbadcc9-9ad5-4217-bf94-1e18cc16f599");
    private const string AuditAction = "Evans11Grower3152Receipt1391Repair";
    private const string RepairSource = "Evans Street 11 bounded grower identity repair";
    private const string RepairReason = "Move the 10 current bins from grower 3162 to corrected grower 3152 for receipt TR109381.";

    public async Task<Evans11Grower3152RepairResult> RunAsync(
        bool apply,
        bool createBackup,
        string requestedBy,
        long? verifiedBackupRunId,
        string? verifiedBackupSha256,
        CancellationToken cancellationToken)
    {
        var state = await InspectAsync(cancellationToken);
        if (state.AlreadyApplied)
            return new("State B", true, false, true,
                "Evans Street 11 grower repair is already applied; no writes were performed.",
                verifiedBackupRunId, verifiedBackupSha256, ExistingCorrectionId);
        if (!state.Ready)
            return Fail("State C", state.Message);

        if (!apply)
        {
            if (!createBackup)
                return new("State A", true, false, false,
                    "Exact reviewed broken state is Ready for bounded repair.", null, null, ExistingCorrectionId);

            var currentCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"];
            var recent = await dbContext.BackupRunRecords.AsNoTracking()
                .Where(x => x.BackupType == BackupRunTypes.PreDeployment
                    && x.Status == BackupRunStatuses.Succeeded
                    && x.VerifiedAt != null
                    && x.Sha256 != null
                    && x.RequestedBy == requestedBy
                    && x.StartedAt >= businessTime.UtcNow.AddHours(-4)
                    && (currentCommit == null || x.DeployedCommit == currentCommit))
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (recent is not null)
            {
                return new("State A", true, false, false,
                    "Exact reviewed broken state is Ready; an already-verified pre-deployment backup for this deployed commit was reused.",
                    recent.Id, recent.Sha256, ExistingCorrectionId);
            }

            var backup = await backupService.RunBackupAsync(BackupRunTypes.PreDeployment, requestedBy, cancellationToken);
            if (!backup.Success || backup.WasSkipped || backup.RunId is null)
                return Fail("State C", $"Pre-deployment backup did not complete successfully: {backup.Message}");

            var verified = await dbContext.BackupRunRecords.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == backup.RunId.Value, cancellationToken);
            if (verified is null || verified.Status != BackupRunStatuses.Succeeded || verified.VerifiedAt is null
                || string.IsNullOrWhiteSpace(verified.Sha256))
                return Fail("State C", "Pre-deployment backup completed without an exact verified read-back record.");

            return new("State A", true, false, false,
                "Exact reviewed broken state is Ready and the pre-deployment backup is verified.",
                verified.Id, verified.Sha256, ExistingCorrectionId);
        }

        var backupCheck = await ValidateBackupAsync(requestedBy, verifiedBackupRunId, verifiedBackupSha256, cancellationToken);
        if (backupCheck is not null) return Fail("State C", backupCheck);

        var actor = await dbContext.Users.SingleOrDefaultAsync(
            x => x.IsActive && x.Email == requestedBy, cancellationToken);
        if (actor is null) return Fail("State C", "The requested-by active user could not be resolved.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            dbContext.ChangeTracker.Clear();
            state = await InspectAsync(cancellationToken);
            if (state.AlreadyApplied)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new("State B", true, false, true,
                    "Evans Street 11 grower repair is already applied; no writes were performed.",
                    verifiedBackupRunId, verifiedBackupSha256, ExistingCorrectionId);
            }
            if (!state.Ready)
                throw new InvalidOperationException($"Exact reviewed state changed after backup: {state.Message}");

            var correction = await dbContext.InventoryIdentityCorrections
                .Include(x => x.InventoryAdjustments)
                .Include(x => x.TreatmentLineageMovements)
                .SingleAsync(x => x.Id == ExistingCorrectionId, cancellationToken);
            var profile = await dbContext.FruitProfiles.AsNoTracking()
                .SingleAsync(x => x.Id == FruitProfileId, cancellationToken);
            var sourceGrower = await dbContext.GrowerLots.AsNoTracking()
                .SingleAsync(x => x.Id == SourceGrowerLotId, cancellationToken);
            var targetGrower = await dbContext.GrowerLots.AsNoTracking()
                .SingleAsync(x => x.Id == TargetGrowerLotId, cancellationToken);
            var phantomTargetSegment = await dbContext.TreatmentLineageSegments
                .SingleAsync(x => x.Id == PhantomTargetSegmentId, cancellationToken);

            var snapshots = await ledger.GetSnapshotsAsync(WarehouseId, new[] { RoomId }, cancellationToken);
            var sourceSnapshot = snapshots.Single(x => x.RoomId == RoomId && x.CropYear == CropYear
                && x.GrowerLotId == SourceGrowerLotId && x.FruitProfileId == FruitProfileId && x.CurrentBins == Bins);
            var targetSnapshot = sourceSnapshot with
            {
                GrowerLotId = TargetGrowerLotId,
                Grower = targetGrower.Grower,
                GrowerNumber = targetGrower.LotNumber,
                Lot = targetGrower.LotNumber,
                InventoryStatus = "",
                CurrentBins = Bins
            };

            var now = businessTime.UtcNow;
            correction.ExpectedAdjustmentCount = 2;
            correction.ExpectedTreatmentMovementCount = 2;
            correction.IsComplete = true;

            AddAdjustment(correction, sourceGrower, profile, -Bins, Bins, 0, "source",
                "Remove the 10 bins still attributed to 3162 from Evans Street 11.");
            AddAdjustment(correction, targetGrower, profile, Bins, 0, Bins, "target",
                "Add the same 10 bins to corrected grower 3152 in Evans Street 11.");

            if (phantomTargetSegment.CurrentBins != 4 || phantomTargetSegment.TreatmentState != TreatmentLineageStates.Untreated
                || phantomTargetSegment.TreatmentSignature != "u")
                throw new InvalidOperationException("The reviewed stale 3152 treatment segment no longer matches the expected 4 untreated bins.");

            phantomTargetSegment.CurrentBins = 0;
            phantomTargetSegment.UpdatedAt = now;
            phantomTargetSegment.ConcurrencyVersion++;
            var retirement = new TreatmentLineageMovement
            {
                OperationKey = $"identity-correction:{correction.OperationKey}:room:{RoomId}:ledger-retirement:{PhantomTargetSegmentId}",
                MovementType = TreatmentLineageMovementTypes.IdentityReclassificationRetirement,
                SourceSegment = phantomTargetSegment,
                SourceSegmentId = phantomTargetSegment.Id,
                SourceRoomId = RoomId,
                DestinationRoomId = null,
                IdentityKey = phantomTargetSegment.IdentityKey,
                TreatmentStateSnapshot = phantomTargetSegment.TreatmentState,
                TreatmentSignatureSnapshot = phantomTargetSegment.TreatmentSignature,
                ReceiptId = phantomTargetSegment.ReceiptId,
                BinCount = 4,
                InventoryIdentityCorrection = correction,
                InventoryIdentityCorrectionId = correction.Id,
                OccurredAt = now,
                CreatedByUserId = actor.Id,
                CreatedAt = now
            };
            correction.TreatmentLineageMovements.Add(retirement);
            dbContext.TreatmentLineageMovements.Add(retirement);

            var lineage = await treatmentService.ReclassifyIdentityAsync(
                sourceSnapshot, targetSnapshot, correction, now, actor.Id, cancellationToken, targetExistingCurrentBins: 0);
            if (!lineage.Success) throw new InvalidOperationException(lineage.Error);
            if (correction.TreatmentLineageMovements.Count != 2)
                throw new InvalidOperationException($"Expected exactly 2 treatment correction movements but found {correction.TreatmentLineageMovements.Count}.");

            await invariant.ValidateBeforeCommitAsync(cancellationToken);

            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = AuditAction,
                EntityName = nameof(InventoryIdentityCorrection),
                EntityKey = correction.Id.ToString("D", CultureInfo.InvariantCulture),
                UserId = actor.Id,
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    Receipt = ReceiptNumber,
                    Evans11 = new { Grower3152 = 0, Grower3162 = 10 },
                    Treatment = new { Grower3152 = 4, Grower3162 = 10 }
                }),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    Receipt = ReceiptNumber,
                    Evans11 = new { Grower3152 = 10, Grower3162 = 0 },
                    Treatment = new { Grower3152 = 10, Grower3162 = 0 },
                    RoomTotalChange = 0,
                    verifiedBackupRunId,
                    verifiedBackupSha256
                }),
                SourceApplication = "CropQc.Web bounded repair",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            var verify = await InspectAsync(cancellationToken);
            if (!verify.AlreadyApplied)
                throw new InvalidOperationException($"Post-write read-back did not match State B: {verify.Message}");

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Applied Evans Street 11 receipt {Receipt} grower repair. Correction {CorrectionId}; backup run {BackupRunId}.",
                ReceiptNumber, ExistingCorrectionId, verifiedBackupRunId);
            return new("State A", true, true, false,
                "Evans Street 11 bounded repair applied exactly once and verified.",
                verifiedBackupRunId, verifiedBackupSha256, ExistingCorrectionId);

            void AddAdjustment(
                InventoryIdentityCorrection parent,
                GrowerLot grower,
                FruitProfile fruitProfile,
                int change,
                int oldBins,
                int newBins,
                string side,
                string note)
            {
                var adjustment = new RoomInventoryAdjustment
                {
                    CropYear = CropYear,
                    ReceiptId = null,
                    WarehouseId = WarehouseId,
                    RoomId = RoomId,
                    GrowerLotId = grower.Id,
                    FruitProfileId = FruitProfileId,
                    GrowerName = grower.Grower,
                    LotNumber = grower.LotNumber,
                    VarietyCode = fruitProfile.VarietyCode,
                    InventoryStatus = null,
                    OldBinCount = oldBins,
                    ChangeAmount = change,
                    NewBinCount = newBins,
                    AdjustmentType = InventoryIdentityWriteGuard.AdjustmentType,
                    Source = RepairSource,
                    Reason = RepairReason,
                    Notes = note,
                    AdjustmentAt = now,
                    CreatedByUserId = actor.Id,
                    CreatedAt = now,
                    InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
                    InventoryOperationKey = $"identity-correction:{parent.OperationKey}:evans11:{side}",
                    InventoryIdentityCorrection = parent,
                    InventoryIdentityCorrectionId = parent.Id
                };
                parent.InventoryAdjustments.Add(adjustment);
                dbContext.RoomInventoryAdjustments.Add(adjustment);
            }
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            logger.LogError(exception, "Evans Street 11 grower repair rolled back.");
            return Fail("State C", $"Repair rolled back: {exception.Message}");
        }
    }

    private async Task<string?> ValidateBackupAsync(
        string requestedBy,
        long? backupRunId,
        string? backupSha256,
        CancellationToken cancellationToken)
    {
        if (backupRunId is null || string.IsNullOrWhiteSpace(backupSha256))
            return "Apply requires the exact verified pre-deployment backup run ID and SHA-256.";
        var backup = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == backupRunId.Value, cancellationToken);
        if (backup is null || backup.BackupType != BackupRunTypes.PreDeployment
            || backup.Status != BackupRunStatuses.Succeeded || backup.VerifiedAt is null
            || !string.Equals(backup.Sha256, backupSha256, StringComparison.OrdinalIgnoreCase)
            || backup.RequestedBy != requestedBy)
            return "The supplied backup does not match a verified pre-deployment backup record.";
        var currentCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"];
        if (!string.IsNullOrWhiteSpace(currentCommit)
            && !string.Equals(backup.DeployedCommit, currentCommit, StringComparison.OrdinalIgnoreCase))
            return "The verified backup was not created by this exact deployed commit.";
        if (backup.StartedAt < businessTime.UtcNow.AddHours(-4))
            return "The verified pre-deployment backup is older than the allowed repair window.";
        return null;
    }

    private async Task<(bool Ready, bool AlreadyApplied, string Message)> InspectAsync(CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ReceiptId, cancellationToken);
        if (receipt is null || receipt.CompuTechReceiptId != ReceiptNumber || receipt.CropYear != CropYear
            || receipt.WarehouseId != WarehouseId || receipt.GrowerLotId != TargetGrowerLotId
            || receipt.GrowerNumber != "3152" || receipt.LotCode != "3152"
            || receipt.FruitProfileId != FruitProfileId || receipt.BinCount != Bins || receipt.IsDeleted)
            return (false, false, "Receipt TR109381 no longer matches the reviewed corrected identity and quantity.");

        var correction = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ExistingCorrectionId, cancellationToken);
        if (correction is null || correction.OperationKey != ExistingCorrectionOperationKey
            || correction.CorrectedReceiptId != ReceiptId || !correction.IsActive
            || correction.SourceCropYear != CropYear || correction.SourceGrowerLotId != SourceGrowerLotId
            || correction.SourceFruitProfileId != FruitProfileId || correction.TargetCropYear != CropYear
            || correction.TargetGrowerLotId != TargetGrowerLotId || correction.TargetFruitProfileId != FruitProfileId
            || correction.Reason != "Wrong lot")
            return (false, false, "The durable receipt identity correction no longer matches the reviewed evidence.");

        var snapshots = await ledger.GetSnapshotsAsync(WarehouseId, new[] { RoomId }, cancellationToken);
        int Current(int growerLotId) => snapshots
            .Where(x => x.RoomId == RoomId && x.CropYear == CropYear && x.GrowerLotId == growerLotId
                && x.FruitProfileId == FruitProfileId)
            .Sum(x => x.CurrentBins);
        var sourceCurrent = Current(SourceGrowerLotId);
        var targetCurrent = Current(TargetGrowerLotId);
        var linkedAdjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId == ExistingCorrectionId)
            .OrderBy(x => x.InventoryOperationKey)
            .ToListAsync(cancellationToken);
        var linkedMovements = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId == ExistingCorrectionId)
            .OrderBy(x => x.OperationKey)
            .ToListAsync(cancellationToken);
        var sourceTreatment = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => x.RoomId == RoomId && x.CropYear == CropYear && x.GrowerLotId == SourceGrowerLotId
                && x.FruitProfileId == FruitProfileId && x.CurrentBins > 0)
            .SumAsync(x => (int?)x.CurrentBins, cancellationToken) ?? 0;
        var targetTreatment = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => x.RoomId == RoomId && x.CropYear == CropYear && x.GrowerLotId == TargetGrowerLotId
                && x.FruitProfileId == FruitProfileId && x.CurrentBins > 0)
            .SumAsync(x => (int?)x.CurrentBins, cancellationToken) ?? 0;
        var auditCount = await dbContext.AuditLogs.AsNoTracking().CountAsync(x => x.Action == AuditAction
            && x.EntityName == nameof(InventoryIdentityCorrection)
            && x.EntityKey == ExistingCorrectionId.ToString("D"), cancellationToken);

        var stateB = correction.IsComplete && correction.ExpectedAdjustmentCount == 2
            && correction.ExpectedTreatmentMovementCount == 2
            && sourceCurrent == 0 && targetCurrent == Bins
            && sourceTreatment == 0 && targetTreatment == Bins
            && linkedAdjustments.Count == 2
            && linkedAdjustments.Sum(x => x.ChangeAmount) == 0
            && linkedAdjustments.Count(x => x.RoomId == RoomId && x.GrowerLotId == SourceGrowerLotId
                && x.FruitProfileId == FruitProfileId && x.ChangeAmount == -Bins && x.OldBinCount == Bins && x.NewBinCount == 0) == 1
            && linkedAdjustments.Count(x => x.RoomId == RoomId && x.GrowerLotId == TargetGrowerLotId
                && x.FruitProfileId == FruitProfileId && x.ChangeAmount == Bins && x.OldBinCount == 0 && x.NewBinCount == Bins) == 1
            && linkedMovements.Count == 2
            && linkedMovements.Sum(x => x.BinCount) == 14
            && auditCount == 1;
        if (stateB) return (false, true, "Repair is already applied.");

        if (linkedAdjustments.Count != 0 || linkedMovements.Count != 0 || auditCount != 0
            || correction.ExpectedAdjustmentCount != 0 || correction.ExpectedTreatmentMovementCount != 0
            || !correction.IsComplete)
            return (false, false, "A partial or incompatible prior repair is attached to receipt TR109381.");

        var phantom = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == PhantomTargetSegmentId, cancellationToken);
        var sourceSegment = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == SourceTreatmentSegmentId, cancellationToken);
        if (sourceCurrent != Bins || targetCurrent != 0
            || sourceTreatment != Bins || targetTreatment != 4
            || phantom is null || phantom.RoomId != RoomId || phantom.CropYear != CropYear
            || phantom.GrowerLotId != TargetGrowerLotId || phantom.FruitProfileId != FruitProfileId
            || phantom.CurrentBins != 4 || phantom.TreatmentState != TreatmentLineageStates.Untreated
            || phantom.TreatmentSignature != "u"
            || sourceSegment is null || sourceSegment.RoomId != RoomId || sourceSegment.CropYear != CropYear
            || sourceSegment.GrowerLotId != SourceGrowerLotId || sourceSegment.FruitProfileId != FruitProfileId
            || sourceSegment.CurrentBins != Bins || sourceSegment.TreatmentState != TreatmentLineageStates.Untreated
            || sourceSegment.TreatmentSignature != "u")
            return (false, false,
                $"Evans Street 11 no longer matches the exact reviewed broken state (3152={targetCurrent}, 3162={sourceCurrent}, treatment3152={targetTreatment}, treatment3162={sourceTreatment}).");

        var laterLedger = await dbContext.RoomInventoryAdjustments.AsNoTracking().AnyAsync(x =>
            x.RoomId == RoomId && x.AdjustmentAt > correction.CreatedAt && x.InventoryIdentityCorrectionId == null
            && x.CropYear == CropYear && x.FruitProfileId == FruitProfileId
            && (x.GrowerLotId == SourceGrowerLotId || x.GrowerLotId == TargetGrowerLotId), cancellationToken);
        var laterTreatment = await dbContext.TreatmentLineageMovements.AsNoTracking().AnyAsync(x =>
            x.OccurredAt > correction.CreatedAt
            && (x.SourceSegmentId == SourceTreatmentSegmentId || x.DestinationSegmentId == SourceTreatmentSegmentId
                || x.SourceSegmentId == PhantomTargetSegmentId || x.DestinationSegmentId == PhantomTargetSegmentId), cancellationToken);
        if (laterLedger || laterTreatment)
            return (false, false, "Current Evans Street 11 inventory changed after the reviewed receipt correction.");

        return (true, false, "Exact reviewed broken state is Ready for bounded repair.");
    }

    private static Evans11Grower3152RepairResult Fail(string state, string message) =>
        new(state, false, false, false, message, null, null, ExistingCorrectionId);
}
