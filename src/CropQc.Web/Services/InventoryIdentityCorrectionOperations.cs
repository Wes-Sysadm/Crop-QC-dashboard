using System.Data;
using System.Globalization;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public static class InventoryIdentityDiagnosticClassifications
{
    public const string ConfirmedAffected = "ConfirmedAffected";
    public const string LikelyAffected = "LikelyAffected";
    public const string Safe = "Safe";
    public const string AmbiguousNeedsReview = "AmbiguousNeedsReview";
}

public sealed record InventoryIdentityDiagnosticFinding(
    Guid OverrideId,
    long ReceiptId,
    string ReceiptNumber,
    string Classification,
    InventoryIdentityKey? Source,
    InventoryIdentityKey? Target,
    int SourceCurrentBins,
    int TargetCurrentBins,
    int NegativeArtifactBins,
    int ObsoleteTreatmentBins,
    IReadOnlyList<long> TransferIds,
    string Reason);

public sealed record InventoryIdentityDiagnosticReport(
    int TotalOverrides,
    int ConfirmedAffected,
    int LikelyAffected,
    int Safe,
    int AmbiguousNeedsReview,
    IReadOnlyList<InventoryIdentityDiagnosticFinding> Findings);

public interface IInventoryIdentityCorrectionDiagnosticService
{
    Task<InventoryIdentityDiagnosticReport> AnalyzeAsync(CancellationToken cancellationToken);
}

public sealed record InventoryIdentityReadinessResult(bool IsReady, IReadOnlyList<string> Issues);

public interface IInventoryIdentityReadinessService
{
    Task<InventoryIdentityReadinessResult> VerifyAsync(CancellationToken cancellationToken);
}

public sealed class InventoryIdentityReadinessService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IInventoryIdentityService identities) : IInventoryIdentityReadinessService
{
    public async Task<InventoryIdentityReadinessResult> VerifyAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var corrections = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .Include(x => x.InventoryAdjustments).Include(x => x.TreatmentLineageMovements)
            .Where(x => x.IsActive).ToListAsync(cancellationToken);
        var snapshots = await ledger.GetSnapshotsAsync(null, null, cancellationToken);
        foreach (var correction in corrections)
        {
            var source = new InventoryIdentityKey(correction.SourceCropYear, correction.SourceGrowerLotId, correction.SourceFruitProfileId);
            try
            {
                var resolution = await identities.ResolveAsync(source, cancellationToken);
                if (!resolution.IsSuperseded) issues.Add($"Correction {correction.Id} does not resolve away from its source.");
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(exception.Message);
            }
            if (!correction.IsComplete || correction.InventoryAdjustments.Count != correction.ExpectedAdjustmentCount
                || correction.TreatmentLineageMovements.Count != correction.ExpectedTreatmentMovementCount)
                issues.Add($"Correction {correction.Id} is incomplete or has a parent-count mismatch.");
            var obsoleteBins = snapshots.Where(x => x.CropYear == source.CropYear
                && x.GrowerLotId == source.GrowerLotId && x.FruitProfileId == source.FruitProfileId && x.CurrentBins > 0)
                .Sum(x => x.CurrentBins);
            if (obsoleteBins > 0) issues.Add($"Correction {correction.Id} leaves {obsoleteBins} current bins on obsolete identity {source}.");
            var obsoleteTreatment = await dbContext.TreatmentLineageSegments.AsNoTracking()
                .Where(x => x.CurrentBins > 0 && x.CropYear == source.CropYear
                    && x.GrowerLotId == source.GrowerLotId && x.FruitProfileId == source.FruitProfileId)
                .SumAsync(x => (int?)x.CurrentBins, cancellationToken) ?? 0;
            if (obsoleteTreatment > 0) issues.Add($"Correction {correction.Id} leaves {obsoleteTreatment} treatment-lineage bins on obsolete identity {source}.");
            if (await dbContext.RoomInventoryAdjustments.AsNoTracking().AnyAsync(x => x.AdjustmentAt > correction.CreatedAt
                && x.CropYear == source.CropYear && x.GrowerLotId == source.GrowerLotId
                && x.FruitProfileId == source.FruitProfileId && x.ChangeAmount > 0
                && x.InventoryIdentityCorrectionId != correction.Id, cancellationToken))
                issues.Add($"Obsolete identity {source} was recreated after correction {correction.Id}.");
        }
        return new(issues.Count == 0, issues);
    }
}

public sealed class InventoryIdentityCorrectionDiagnosticService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger) : IInventoryIdentityCorrectionDiagnosticService
{
    public async Task<InventoryIdentityDiagnosticReport> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var overrides = await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .Include(x => x.Receipt)
            .Include(x => x.InventoryAdjustments)
            .Where(x => x.IsComplete && x.ActionType == ReceiptInventoryOverrideActionTypes.InventoryReclassification)
            .OrderBy(x => x.ReceiptId).ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var snapshots = await ledger.GetSnapshotsAsync(null, null, cancellationToken);
        var findings = new List<InventoryIdentityDiagnosticFinding>();
        foreach (var operation in overrides)
        {
            var source = ReadIdentity(operation.BeforeReceiptSnapshotJson);
            var target = ReadIdentity(operation.AfterReceiptSnapshotJson);
            if (source is null || target is null || source == target)
            {
                findings.Add(new(operation.Id, operation.ReceiptId, operation.Receipt.CompuTechReceiptId,
                    InventoryIdentityDiagnosticClassifications.AmbiguousNeedsReview, source, target, 0, 0, 0, 0, [],
                    "The historical before/after identity is incomplete or indistinguishable."));
                continue;
            }
            var sourceRows = snapshots.Where(x => Matches(x, source)).ToList();
            var targetRows = snapshots.Where(x => Matches(x, target)).ToList();
            var sourcePositive = sourceRows.Where(x => x.CurrentBins > 0).Sum(x => x.CurrentBins);
            var targetPositive = targetRows.Where(x => x.CurrentBins > 0).Sum(x => x.CurrentBins);
            var negativeArtifact = -sourceRows.Where(x => x.CurrentBins < 0).Sum(x => x.CurrentBins)
                - targetRows.Where(x => x.CurrentBins < 0).Sum(x => x.CurrentBins);
            var treatmentBins = await dbContext.TreatmentLineageSegments.AsNoTracking()
                .Where(x => x.CurrentBins > 0 && x.CropYear == source.CropYear
                    && x.GrowerLotId == source.GrowerLotId && x.FruitProfileId == source.FruitProfileId)
                .SumAsync(x => (int?)x.CurrentBins, cancellationToken) ?? 0;
            var transferIds = await dbContext.RoomTransfers.AsNoTracking()
                .Where(x => x.CropYear == source.CropYear && x.GrowerLotId == source.GrowerLotId
                    && x.FruitProfileId == source.FruitProfileId && x.TransferredAt <= operation.CreatedAt)
                .Select(x => x.Id).OrderBy(x => x).ToListAsync(cancellationToken);
            var staleRoom = operation.InventoryAdjustments.Any(x => x.ChangeAmount < 0)
                && sourceRows.Any(x => x.CurrentBins > 0 && operation.InventoryAdjustments.All(a => a.RoomId != x.RoomId));
            var classification = sourcePositive > 0 && (negativeArtifact > 0 || treatmentBins > 0 || staleRoom)
                ? InventoryIdentityDiagnosticClassifications.ConfirmedAffected
                : sourcePositive > 0 || negativeArtifact > 0
                    ? InventoryIdentityDiagnosticClassifications.LikelyAffected
                    : sourceRows.Count == 0 || sourceRows.All(x => x.CurrentBins == 0)
                        ? InventoryIdentityDiagnosticClassifications.Safe
                        : InventoryIdentityDiagnosticClassifications.AmbiguousNeedsReview;
            findings.Add(new(operation.Id, operation.ReceiptId, operation.Receipt.CompuTechReceiptId,
                classification, source, target, sourcePositive, targetPositive, negativeArtifact, treatmentBins,
                transferIds, staleRoom ? "Correction adjustments did not follow the current source room." : "Classified from authoritative current ledger and treatment lineage."));
        }
        return new(overrides.Count,
            findings.Count(x => x.Classification == InventoryIdentityDiagnosticClassifications.ConfirmedAffected),
            findings.Count(x => x.Classification == InventoryIdentityDiagnosticClassifications.LikelyAffected),
            findings.Count(x => x.Classification == InventoryIdentityDiagnosticClassifications.Safe),
            findings.Count(x => x.Classification == InventoryIdentityDiagnosticClassifications.AmbiguousNeedsReview),
            findings);
    }

    private static InventoryIdentityKey? ReadIdentity(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var cropYear = root.GetProperty("cropYear").GetInt32();
            var growerLot = root.GetProperty("growerLotId");
            var fruit = root.GetProperty("fruitProfileId");
            return growerLot.ValueKind == JsonValueKind.Number && fruit.ValueKind == JsonValueKind.Number
                ? new(cropYear, growerLot.GetInt32(), fruit.GetInt32())
                : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Matches(RoomInventoryLedgerSnapshot snapshot, InventoryIdentityKey key) =>
        snapshot.CropYear == key.CropYear && snapshot.GrowerLotId == key.GrowerLotId && snapshot.FruitProfileId == key.FruitProfileId;
}

public sealed record Tr508901RepairResult(string State, bool Success, bool Applied, bool AlreadyApplied, string Message, Guid? CorrectionId = null);

public interface ITr508901InventoryRepairService
{
    Task<Tr508901RepairResult> RunAsync(bool apply, string requestedBy, CancellationToken cancellationToken);
}

public sealed class Tr508901InventoryRepairService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IRoomTreatmentService treatmentService,
    IInventoryDeductionInvariantService invariant,
    IBusinessTimeService businessTime) : ITr508901InventoryRepairService
{
    private static readonly InventoryIdentityKey Source = new(2026, 538, 18);
    private static readonly InventoryIdentityKey Target = new(2026, 538, 26);
    private const string OperationKey = "tr508901-systemic-identity-repair-v1";

    public async Task<Tr508901RepairResult> RunAsync(bool apply, string requestedBy, CancellationToken cancellationToken)
    {
        var existing = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationKey == OperationKey, cancellationToken);
        var receipt = await dbContext.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1229, cancellationToken);
        if (receipt is null || receipt.CompuTechReceiptId != "TR508901" || receipt.CropYear != 2026
            || receipt.GrowerLotId != 538 || receipt.FruitProfileId != 26 || receipt.BinCount != 40)
            return Fail("State C", "TR508901 Receipt identity or quantity differs from the reviewed evidence.");

        var snapshots = await ledger.GetSnapshotsAsync(null, null, cancellationToken);
        var adjustment2094 = await dbContext.RoomInventoryAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 2094, cancellationToken);
        var adjustment2212 = await dbContext.RoomInventoryAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 2212, cancellationToken);
        var segment356 = await dbContext.TreatmentLineageSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 356, cancellationToken);
        if (adjustment2094 is null || adjustment2212 is null || segment356 is null)
            return Fail("State C", "Required immutable TR508901 ledger or treatment evidence is absent.");
        var room8 = adjustment2094.RoomId;
        var room10 = adjustment2212.RoomId;
        int Current(int roomId, InventoryIdentityKey identity) => snapshots
            .Where(x => x.RoomId == roomId && x.CropYear == identity.CropYear
                && x.GrowerLotId == identity.GrowerLotId && x.FruitProfileId == identity.FruitProfileId)
            .Sum(x => x.CurrentBins);
        var stateB = existing is { IsComplete: true, IsActive: true }
            && Current(room10, Source) == 0 && Current(room8, Source) == 0
            && Current(room10, Target) == 40 && Current(room8, Target) == 0
            && segment356.CurrentBins == 0
            && await dbContext.TreatmentLineageSegments.AsNoTracking().AnyAsync(x => x.RoomId == room10
                && x.CropYear == 2026 && x.GrowerLotId == 538 && x.FruitProfileId == 26
                && x.CurrentBins == 40 && x.TreatmentState == TreatmentLineageStates.Untreated
                && x.TreatmentSignature == "u", cancellationToken);
        if (stateB) return new("State B", true, false, true, "TR508901 repair is already applied.", existing!.Id);
        if (existing is not null) return Fail("State C", "A partial or incompatible TR508901 repair record exists.");

        var requiredAdjustmentIds = new long[] { 2094, 2130, 2131, 2211, 2212, 2213, 2214 };
        var requiredRows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => requiredAdjustmentIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);
        var noLaterMovement = !await dbContext.TreatmentLineageMovements.AsNoTracking()
            .AnyAsync(x => x.SourceSegmentId == 356 && x.Id > 263, cancellationToken);
        var stateA = requiredRows.Count == requiredAdjustmentIds.Length
            && Current(room10, Source) == 40 && Current(room8, Source) == -40
            && Current(room10, Target) == 0 && Current(room8, Target) == 40
            && segment356.RoomId == room10 && segment356.CropYear == 2026 && segment356.GrowerLotId == 538
            && segment356.FruitProfileId == 18 && segment356.CurrentBins == 40
            && segment356.TreatmentState == TreatmentLineageStates.Untreated && segment356.TreatmentSignature == "u"
            && noLaterMovement;
        if (!stateA) return Fail("State C", "TR508901 does not match the exact reviewed broken state.");
        if (!apply) return new("State A", true, false, false, "TR508901 exact broken state is Ready for bounded repair.");

        var actor = await dbContext.Users.SingleOrDefaultAsync(x => x.IsActive && x.Email == requestedBy, cancellationToken);
        if (actor is null) return Fail("State C", "The requested-by active user could not be resolved.");
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var now = businessTime.UtcNow;
            var correction = new InventoryIdentityCorrection
            {
                Id = Guid.NewGuid(),
                OperationKey = OperationKey,
                SourceCropYear = 2026,
                SourceGrowerLotId = 538,
                SourceFruitProfileId = 18,
                TargetCropYear = 2026,
                TargetGrowerLotId = 538,
                TargetFruitProfileId = 26,
                CorrectedReceiptId = receipt.Id,
                Reason = "Bounded TR508901 stale-room identity repair",
                CreatedByUserId = actor.Id,
                CreatedAt = now,
                SourceIdentitySnapshotJson = JsonSerializer.Serialize(Source),
                TargetIdentitySnapshotJson = JsonSerializer.Serialize(Target),
                ExpectedAdjustmentCount = 4,
                IsActive = true,
                IsComplete = false
            };
            dbContext.InventoryIdentityCorrections.Add(correction);
            var sourceProfile = await dbContext.FruitProfiles.AsNoTracking().SingleAsync(x => x.Id == 18, cancellationToken);
            var targetProfile = await dbContext.FruitProfiles.AsNoTracking().SingleAsync(x => x.Id == 26, cancellationToken);
            var growerLot = await dbContext.GrowerLots.AsNoTracking().SingleAsync(x => x.Id == 538, cancellationToken);
            Add(room10, Source, sourceProfile, -40, 40, 0, "Remove obsolete identity from physical room");
            Add(room10, Target, targetProfile, 40, 0, 40, "Add canonical identity to physical room");
            Add(room8, Target, targetProfile, -40, 40, 0, "Remove stale-room target artifact");
            Add(room8, Source, sourceProfile, 40, -40, 0, "Clear stale-room negative source artifact");

            var sourceSnapshot = snapshots.Single(x => x.RoomId == room10 && Matches(x, Source) && x.CurrentBins == 40);
            var targetSnapshot = sourceSnapshot with
            {
                CropYear = 2026,
                GrowerLotId = 538,
                FruitProfileId = 26,
                Grower = growerLot.Grower,
                GrowerNumber = growerLot.LotNumber,
                Lot = growerLot.LotNumber,
                StoredVarietyCode = targetProfile.VarietyCode,
                Variety = targetProfile.VarietyCode,
                VarietyName = targetProfile.Name,
                FruitType = targetProfile.FruitType,
                ProductionType = targetProfile.ProductionType,
                IsOrganic = targetProfile.IsOrganic,
                InventoryStatus = targetProfile.ProductionType
            };
            var lineage = await treatmentService.ReclassifyIdentityAsync(sourceSnapshot, targetSnapshot, correction, now, actor.Id, cancellationToken);
            if (!lineage.Success) throw new InvalidOperationException(lineage.Error);
            correction.ExpectedTreatmentMovementCount = correction.TreatmentLineageMovements.Count;
            correction.IsComplete = true;
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "TR508901InventoryIdentityRepair",
                EntityName = nameof(InventoryIdentityCorrection),
                EntityKey = correction.Id.ToString("D", CultureInfo.InvariantCulture),
                UserId = actor.Id,
                BeforeValuesJson = JsonSerializer.Serialize(new { MCD10FP18 = 40, MCD08FP18 = -40, MCD08FP26 = 40, Segment356 = "FP18/40/u" }),
                AfterValuesJson = JsonSerializer.Serialize(new { MCD10FP18 = 0, MCD08FP18 = 0, MCD08FP26 = 0, MCD10FP26 = 40, Treatment = "FP26/40/u", HistoricalRowsRetained = requiredAdjustmentIds }),
                SourceApplication = "CropQc.Web bounded repair",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new("State A", true, true, false, "TR508901 bounded repair applied exactly once.", correction.Id);

            void Add(int roomId, InventoryIdentityKey identity, FruitProfile profile, int change, int oldBins, int newBins, string note)
            {
                var adjustment = new RoomInventoryAdjustment
                {
                    CropYear = identity.CropYear,
                    ReceiptId = receipt.Id,
                    WarehouseId = roomId == room10 ? adjustment2212.WarehouseId : adjustment2094.WarehouseId,
                    RoomId = roomId,
                    GrowerLotId = identity.GrowerLotId,
                    FruitProfileId = identity.FruitProfileId,
                    GrowerName = growerLot.Grower,
                    LotNumber = growerLot.LotNumber,
                    VarietyCode = profile.VarietyCode,
                    InventoryStatus = profile.ProductionType,
                    OldBinCount = oldBins,
                    ChangeAmount = change,
                    NewBinCount = newBins,
                    AdjustmentType = InventoryIdentityWriteGuard.AdjustmentType,
                    Source = "TR508901 bounded identity repair",
                    Reason = correction.Reason,
                    Notes = note,
                    AdjustmentAt = now,
                    CreatedByUserId = actor.Id,
                    CreatedAt = now,
                    InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
                    InventoryOperationKey = $"identity-correction:{OperationKey}:{correction.InventoryAdjustments.Count + 1}",
                    InventoryIdentityCorrection = correction,
                    InventoryIdentityCorrectionId = correction.Id
                };
                correction.InventoryAdjustments.Add(adjustment);
                dbContext.RoomInventoryAdjustments.Add(adjustment);
            }
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Fail("State C", $"TR508901 repair rolled back: {exception.Message}");
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    private static bool Matches(RoomInventoryLedgerSnapshot x, InventoryIdentityKey key) =>
        x.CropYear == key.CropYear && x.GrowerLotId == key.GrowerLotId && x.FruitProfileId == key.FruitProfileId;
    private static Tr508901RepairResult Fail(string state, string message) => new(state, false, false, false, message);
}
