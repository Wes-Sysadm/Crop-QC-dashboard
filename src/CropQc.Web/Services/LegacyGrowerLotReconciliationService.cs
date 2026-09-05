using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public static class LegacyGrowerLotReconciliationClassifications
{
    public const string AutoResolvableReviewedGrowerLot = "AutoResolvableReviewedGrowerLot";
    public const string NeedsReconciliation = "NeedsReconciliation";
}

public sealed record LegacyGrowerLotReconciliationCandidate(
    string Classification,
    string Reason,
    int WarehouseId,
    string Facility,
    int RoomId,
    string Room,
    int CropYear,
    string GrowerNumber,
    string Lot,
    int FruitProfileId,
    string Variety,
    string ProductionType,
    bool IsOrganic,
    int CurrentBins,
    int? TargetGrowerLotId,
    string? TargetGrower,
    string StateToken,
    IReadOnlyList<long> PositiveSourceAdjustmentIds);

public sealed record LegacyGrowerLotReconciliationDiagnostic(
    int AutoResolvableReviewedPositions,
    int AutoResolvableReviewedBins,
    int NeedsReconciliationPositions,
    int NeedsReconciliationBins,
    IReadOnlyList<LegacyGrowerLotReconciliationCandidate> Positions);

public sealed record LegacyGrowerLotReconciliationRequest(
    bool Apply,
    int WarehouseId,
    int RoomId,
    int CropYear,
    string GrowerNumber,
    string Lot,
    int FruitProfileId,
    bool IsOrganic,
    int TargetGrowerLotId,
    int ExpectedCurrentBins,
    string ExpectedStateToken,
    string OperationKey,
    string RequestedBy,
    string Reason);

public sealed record LegacyGrowerLotReconciliationResult(
    string State,
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    Guid? CorrectionId = null,
    int CurrentBins = 0,
    int TargetGrowerLotId = 0);

public interface ILegacyGrowerLotReconciliationService
{
    Task<LegacyGrowerLotReconciliationDiagnostic> AnalyzeAsync(CancellationToken cancellationToken);
    Task<LegacyGrowerLotReconciliationResult> RunAsync(
        LegacyGrowerLotReconciliationRequest request,
        CancellationToken cancellationToken);
}

public sealed class LegacyGrowerLotReconciliationService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    ICanonicalGrowerService canonicalGrowers,
    IRoomTreatmentService treatmentService,
    IInventoryDeductionInvariantService invariant,
    IBusinessTimeService businessTime) : ILegacyGrowerLotReconciliationService
{
    private const string SourceApplication = "CropQc.Web reviewed legacy Grower Lot reconciliation";

    public async Task<LegacyGrowerLotReconciliationDiagnostic> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var snapshots = (await ledger.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CurrentBins > 0 && x.GrowerLotId is null)
            .OrderBy(x => x.Facility).ThenBy(x => x.Room).ThenBy(x => x.GrowerNumber ?? x.Lot)
            .ThenBy(x => x.FruitProfileId)
            .ToList();
        var resolver = await canonicalGrowers.LoadResolutionSetAsync(cancellationToken);
        var activeLots = await dbContext.GrowerLots.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var result = new List<LegacyGrowerLotReconciliationCandidate>(snapshots.Count);
        foreach (var snapshot in snapshots)
            result.Add(await ResolveCandidateAsync(snapshot, resolver, activeLots, snapshots, cancellationToken));
        var resolvable = result.Where(x => x.Classification == LegacyGrowerLotReconciliationClassifications.AutoResolvableReviewedGrowerLot).ToList();
        var ambiguous = result.Where(x => x.Classification == LegacyGrowerLotReconciliationClassifications.NeedsReconciliation).ToList();
        return new(resolvable.Count, resolvable.Sum(x => x.CurrentBins), ambiguous.Count, ambiguous.Sum(x => x.CurrentBins), result);
    }

    public async Task<LegacyGrowerLotReconciliationResult> RunAsync(
        LegacyGrowerLotReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        var requestError = ValidateRequest(request);
        if (requestError is not null) return Failed("State C", requestError);

        var existing = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationKey == request.OperationKey, cancellationToken);
        if (existing is not null)
            return await VerifyAlreadyAppliedAsync(existing, request, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var diagnostic = await AnalyzeAsync(cancellationToken);
            var matches = diagnostic.Positions.Where(x =>
                x.WarehouseId == request.WarehouseId && x.RoomId == request.RoomId
                && x.CropYear == request.CropYear && x.FruitProfileId == request.FruitProfileId
                && x.IsOrganic == request.IsOrganic
                && Normalize(x.GrowerNumber) == Normalize(request.GrowerNumber)
                && Normalize(x.Lot) == Normalize(request.Lot)).ToList();
            if (matches.Count != 1)
                return await RollbackAsync(transaction, Failed("State C", "The exact current legacy position could not be resolved uniquely."), cancellationToken);
            var candidate = matches[0];
            if (candidate.Classification != LegacyGrowerLotReconciliationClassifications.AutoResolvableReviewedGrowerLot
                || candidate.TargetGrowerLotId != request.TargetGrowerLotId)
                return await RollbackAsync(transaction, Failed("State C", candidate.Reason), cancellationToken);
            if (candidate.CurrentBins != request.ExpectedCurrentBins || candidate.StateToken != request.ExpectedStateToken)
                return await RollbackAsync(transaction, Failed("State C", "The current legacy position changed after review. Refresh the diagnostic and re-review the exact quantity."), cancellationToken);
            if (!request.Apply)
                return await RollbackAsync(transaction, new("State A", true, false, false,
                    "The exact reviewed legacy position is ready for Grower Lot reconciliation.", null,
                    candidate.CurrentBins, request.TargetGrowerLotId), cancellationToken);

            var actor = await dbContext.Users.SingleOrDefaultAsync(
                x => x.IsActive && x.Email == request.RequestedBy, cancellationToken);
            if (actor is null)
                return await RollbackAsync(transaction, Failed("State C", "The requested-by active user could not be resolved."), cancellationToken);

            var source = (await ledger.GetSnapshotsAsync(request.WarehouseId, [request.RoomId], request.FruitProfileId, cancellationToken))
                .Single(x => x.CurrentBins == request.ExpectedCurrentBins && x.GrowerLotId is null
                    && x.CropYear == request.CropYear
                    && Normalize(x.GrowerNumber) == Normalize(request.GrowerNumber)
                    && Normalize(x.Lot) == Normalize(request.Lot));
            var targetGrowerLot = await dbContext.GrowerLots.AsNoTracking()
                .SingleAsync(x => x.Id == request.TargetGrowerLotId && x.IsActive, cancellationToken);
            var profile = await dbContext.FruitProfiles.AsNoTracking()
                .SingleAsync(x => x.Id == request.FruitProfileId && x.IsActive, cancellationToken);
            var existingTargetBins = (await ledger.GetSnapshotsAsync(request.WarehouseId, [request.RoomId], request.FruitProfileId, cancellationToken))
                .Where(x => x.CurrentBins > 0 && x.CropYear == request.CropYear
                    && x.GrowerLotId == request.TargetGrowerLotId)
                .Sum(x => x.CurrentBins);
            var now = businessTime.UtcNow;
            var correction = new InventoryIdentityCorrection
            {
                Id = Guid.NewGuid(),
                OperationKey = request.OperationKey,
                SourceCropYear = request.CropYear,
                SourceGrowerLotId = null,
                SourceFruitProfileId = request.FruitProfileId,
                TargetCropYear = request.CropYear,
                TargetGrowerLotId = request.TargetGrowerLotId,
                TargetFruitProfileId = request.FruitProfileId,
                Reason = request.Reason.Trim(),
                CreatedByUserId = actor.Id,
                CreatedAt = now,
                SourceIdentitySnapshotJson = JsonSerializer.Serialize(new
                {
                    request.WarehouseId,
                    request.RoomId,
                    request.CropYear,
                    GrowerLotId = (int?)null,
                    request.GrowerNumber,
                    request.Lot,
                    request.FruitProfileId,
                    request.IsOrganic,
                    CurrentBins = candidate.CurrentBins,
                    candidate.StateToken,
                    candidate.PositiveSourceAdjustmentIds
                }),
                TargetIdentitySnapshotJson = JsonSerializer.Serialize(new
                {
                    request.WarehouseId,
                    request.RoomId,
                    request.CropYear,
                    GrowerLotId = request.TargetGrowerLotId,
                    Grower = targetGrowerLot.Grower,
                    GrowerNumber = targetGrowerLot.LotNumber,
                    request.FruitProfileId,
                    request.IsOrganic,
                    ExistingTargetBins = existingTargetBins,
                    CurrentBins = existingTargetBins + candidate.CurrentBins
                }),
                ExpectedAdjustmentCount = 2,
                IsActive = true,
                IsComplete = false
            };
            dbContext.InventoryIdentityCorrections.Add(correction);
            AddAdjustment(null, source.Grower, source.GrowerNumber ?? source.Lot, -candidate.CurrentBins,
                candidate.CurrentBins, 0, "Remove reviewed legacy position with missing Grower Lot");
            AddAdjustment(request.TargetGrowerLotId, targetGrowerLot.Grower, targetGrowerLot.LotNumber,
                candidate.CurrentBins, existingTargetBins, existingTargetBins + candidate.CurrentBins,
                "Add the same bins to the unique reviewed canonical Grower Lot");

            var target = source with
            {
                GrowerLotId = request.TargetGrowerLotId,
                Grower = targetGrowerLot.Grower,
                GrowerNumber = targetGrowerLot.LotNumber,
                Lot = targetGrowerLot.LotNumber,
                PoolStart = targetGrowerLot.PoolStart,
                StoredVarietyCode = profile.VarietyCode,
                Variety = profile.VarietyCode,
                VarietyName = profile.Name,
                FruitType = profile.FruitType,
                ProductionType = profile.ProductionType,
                IsOrganic = profile.IsOrganic,
                InventoryStatus = source.InventoryStatus,
                CurrentBins = source.CurrentBins
            };
            var lineage = await treatmentService.ReclassifyIdentityAsync(source, target, correction, now, actor.Id, cancellationToken);
            if (!lineage.Success) throw new InvalidOperationException(lineage.Error);
            correction.ExpectedTreatmentMovementCount = correction.TreatmentLineageMovements.Count;
            correction.IsComplete = true;
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "LegacyGrowerLotReconciliation",
                EntityName = nameof(InventoryIdentityCorrection),
                EntityKey = correction.Id.ToString("D", CultureInfo.InvariantCulture),
                UserId = actor.Id,
                BeforeValuesJson = correction.SourceIdentitySnapshotJson,
                AfterValuesJson = correction.TargetIdentitySnapshotJson,
                SourceApplication = SourceApplication,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new("State A", true, true, false,
                "The reviewed legacy Grower Lot reconciliation applied exactly once.", correction.Id,
                candidate.CurrentBins, request.TargetGrowerLotId);

            void AddAdjustment(int? growerLotId, string grower, string growerNumber, int change, int oldBins, int newBins, string notes)
            {
                var adjustment = new RoomInventoryAdjustment
                {
                    CropYear = request.CropYear,
                    WarehouseId = request.WarehouseId,
                    RoomId = request.RoomId,
                    GrowerLotId = growerLotId,
                    FruitProfileId = request.FruitProfileId,
                    GrowerName = grower,
                    LotNumber = growerNumber,
                    VarietyCode = profile.VarietyCode,
                    InventoryStatus = source.InventoryStatus,
                    OldBinCount = oldBins,
                    ChangeAmount = change,
                    NewBinCount = newBins,
                    AdjustmentType = InventoryIdentityWriteGuard.AdjustmentType,
                    Source = "Reviewed legacy Grower Lot reconciliation",
                    Reason = correction.Reason,
                    Notes = notes,
                    AdjustmentAt = now,
                    CreatedByUserId = actor.Id,
                    CreatedAt = now,
                    InventoryInvariantVersion = 0,
                    InventoryOperationKey = $"identity-correction:{request.OperationKey}:{correction.InventoryAdjustments.Count + 1}",
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
            return Failed("State C", $"The reviewed legacy Grower Lot reconciliation rolled back: {exception.Message}");
        }
    }

    private async Task<LegacyGrowerLotReconciliationCandidate> ResolveCandidateAsync(
        RoomInventoryLedgerSnapshot snapshot,
        CanonicalGrowerResolutionSet resolver,
        IReadOnlyList<GrowerLot> activeLots,
        IReadOnlyList<RoomInventoryLedgerSnapshot> allMissingGrowerLotSnapshots,
        CancellationToken cancellationToken)
    {
        var positiveIds = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.RoomId == snapshot.RoomId && x.WarehouseId == snapshot.WarehouseId
                && x.CropYear == snapshot.CropYear && x.GrowerLotId == null
                && x.FruitProfileId == snapshot.FruitProfileId && x.ChangeAmount > 0)
            .Select(x => new
            {
                x.Id,
                x.GrowerName,
                x.LotNumber,
                x.ReceiptId,
                ReceiptGrowerNumber = x.Receipt == null ? null : x.Receipt.GrowerNumber,
                ReceiptGrowerName = x.Receipt == null ? null : x.Receipt.GrowerName,
                ReceiptLot = x.Receipt == null ? null : x.Receipt.LotCode
            })
            .ToListAsync(cancellationToken);
        positiveIds = positiveIds.Where(x => Normalize(x.LotNumber) == Normalize(snapshot.Lot)).ToList();
        var token = StateToken(snapshot, positiveIds.Select(x => x.Id));
        LegacyGrowerLotReconciliationCandidate Needs(string reason) => new(
            LegacyGrowerLotReconciliationClassifications.NeedsReconciliation, reason,
            snapshot.WarehouseId, snapshot.Facility, snapshot.RoomId, snapshot.Room,
            snapshot.CropYear ?? 0, snapshot.GrowerNumber ?? "", snapshot.Lot,
            snapshot.FruitProfileId ?? 0, snapshot.Variety, snapshot.ProductionType,
            snapshot.IsOrganic == true, snapshot.CurrentBins, null, null, token,
            positiveIds.Select(x => x.Id).OrderBy(x => x).ToArray());

        if (snapshot.CropYear is null || snapshot.FruitProfileId is null || snapshot.IsOrganic is null)
            return Needs("Crop year, Fruit Profile, or Organic/Conventional identity is incomplete.");
        if (string.IsNullOrWhiteSpace(snapshot.GrowerNumber))
            return Needs("Grower Number is missing.");
        if (Normalize(snapshot.GrowerNumber) != Normalize(snapshot.Lot))
            return Needs("Grower Number and immutable lot evidence disagree.");
        var canonical = resolver.Resolve(snapshot.Grower, snapshot.GrowerNumber);
        if (!canonical.IsMapped || canonical.CanonicalGrowerId is null)
            return Needs("The Grower Number does not resolve to one reviewed active canonical grower.");
        if (!HistoricalNameMatchesReviewedGrower(snapshot.Grower, canonical, resolver))
            return Needs("The source grower name conflicts with the reviewed Grower Number mapping.");
        var candidates = activeLots.Where(x => Normalize(x.LotNumber) == Normalize(snapshot.Lot)).ToList();
        if (candidates.Count != 1)
            return Needs($"Expected exactly one active reviewed Grower Lot for {snapshot.Lot}; found {candidates.Count}.");
        var target = candidates[0];
        var targetCanonical = resolver.Resolve(target.Grower, target.LotNumber);
        if (!targetCanonical.IsMapped || targetCanonical.CanonicalGrowerId != canonical.CanonicalGrowerId)
            return Needs("The unique Grower Lot conflicts with the reviewed canonical grower mapping.");
        var profile = await dbContext.FruitProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == snapshot.FruitProfileId.Value && x.IsActive, cancellationToken);
        if (profile is null || profile.IsOrganic != snapshot.IsOrganic
            || !string.Equals(profile.ProductionType, snapshot.ProductionType, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(snapshot.InventoryStatus)
                && !string.Equals(profile.ProductionType, snapshot.InventoryStatus, StringComparison.OrdinalIgnoreCase))
            return Needs("Fruit Profile and Organic/Conventional identity conflict.");
        if (positiveIds.Count == 0 || positiveIds.Any(x => x.ReceiptId is null
                || Normalize(x.ReceiptGrowerNumber) != Normalize(snapshot.GrowerNumber)
                || Normalize(x.ReceiptLot) != Normalize(snapshot.Lot)
                || Normalize(x.LotNumber) != Normalize(snapshot.Lot)
                || !HistoricalNameMatchesReviewedGrower(x.GrowerName, canonical, resolver)
                || !HistoricalNameMatchesReviewedGrower(x.ReceiptGrowerName, canonical, resolver)))
            return Needs("Positive source provenance contains missing or conflicting grower evidence.");
        var conflictingCurrent = allMissingGrowerLotSnapshots.Any(x => x != snapshot
            && x.RoomId == snapshot.RoomId && x.CropYear == snapshot.CropYear
            && x.FruitProfileId == snapshot.FruitProfileId && Normalize(x.Lot) == Normalize(snapshot.Lot)
            && Normalize(x.GrowerNumber) != Normalize(snapshot.GrowerNumber));
        if (conflictingCurrent)
            return Needs("Another current position contains conflicting identity evidence.");
        return new(LegacyGrowerLotReconciliationClassifications.AutoResolvableReviewedGrowerLot,
            $"Unique reviewed canonical Grower Number and active Grower Lot {target.Id} agree with every positive source row; Fruit Profile {profile.Id} remains unchanged.",
            snapshot.WarehouseId, snapshot.Facility, snapshot.RoomId, snapshot.Room,
            snapshot.CropYear.Value, snapshot.GrowerNumber, snapshot.Lot, snapshot.FruitProfileId.Value,
            snapshot.Variety, snapshot.ProductionType, snapshot.IsOrganic.Value, snapshot.CurrentBins,
            target.Id, target.Grower, token, positiveIds.Select(x => x.Id).OrderBy(x => x).ToArray());
    }

    private async Task<LegacyGrowerLotReconciliationResult> VerifyAlreadyAppliedAsync(
        InventoryIdentityCorrection correction,
        LegacyGrowerLotReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId == correction.Id)
            .OrderBy(x => x.InventoryOperationKey).ToListAsync(cancellationToken);
        var snapshots = await ledger.GetSnapshotsAsync(request.WarehouseId, [request.RoomId], request.FruitProfileId, cancellationToken);
        var sourceBins = snapshots.Where(x => x.GrowerLotId is null && x.CropYear == request.CropYear
            && Normalize(x.GrowerNumber) == Normalize(request.GrowerNumber)
            && Normalize(x.Lot) == Normalize(request.Lot)).Sum(x => x.CurrentBins);
        var exact = correction is { IsActive: true, IsComplete: true, SourceGrowerLotId: null }
            && correction.SourceCropYear == request.CropYear
            && correction.SourceFruitProfileId == request.FruitProfileId
            && correction.TargetCropYear == request.CropYear
            && correction.TargetGrowerLotId == request.TargetGrowerLotId
            && correction.TargetFruitProfileId == request.FruitProfileId
            && correction.CorrectedReceiptId is null && correction.ReceiptInventoryOverrideId is null
            && correction.ExpectedAdjustmentCount == 2 && rows.Count == 2
            && rows.Sum(x => x.ChangeAmount) == 0
            && rows.Count(x => x.GrowerLotId is null && x.ChangeAmount == -request.ExpectedCurrentBins) == 1
            && rows.Count(x => x.GrowerLotId == request.TargetGrowerLotId && x.ChangeAmount == request.ExpectedCurrentBins) == 1
            && sourceBins == 0;
        return exact
            ? new("State B", true, false, true, "The reviewed legacy Grower Lot reconciliation is already applied.",
                correction.Id, request.ExpectedCurrentBins, request.TargetGrowerLotId)
            : Failed("State C", "A partial or incompatible legacy Grower Lot reconciliation operation already exists.");
    }

    private static string? ValidateRequest(LegacyGrowerLotReconciliationRequest request)
    {
        if (request.WarehouseId <= 0 || request.RoomId <= 0 || request.CropYear <= 0
            || request.FruitProfileId <= 0 || request.TargetGrowerLotId <= 0 || request.ExpectedCurrentBins <= 0)
            return "The reviewed source/target identity and quantity are required.";
        if (string.IsNullOrWhiteSpace(request.GrowerNumber) || string.IsNullOrWhiteSpace(request.Lot)
            || string.IsNullOrWhiteSpace(request.ExpectedStateToken) || string.IsNullOrWhiteSpace(request.OperationKey))
            return "Grower Number, lot, state token, and operation key are required.";
        if (string.IsNullOrWhiteSpace(request.Reason)) return "A reviewed reconciliation reason is required.";
        return null;
    }

    private static string StateToken(RoomInventoryLedgerSnapshot snapshot, IEnumerable<long> positiveAdjustmentIds)
    {
        var value = string.Join('|', snapshot.WarehouseId, snapshot.RoomId, snapshot.CropYear,
            Normalize(snapshot.GrowerNumber), Normalize(snapshot.Lot), snapshot.FruitProfileId,
            snapshot.IsOrganic, snapshot.CurrentBins, snapshot.LatestAdjustmentId,
            string.Join(',', positiveAdjustmentIds.OrderBy(x => x)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string Normalize(string? value) =>
        CanonicalGrowerService.NormalizeGrowerNumber(value);

    private static bool HistoricalNameMatchesReviewedGrower(
        string? historicalName,
        CanonicalGrowerIdentity reviewedGrower,
        CanonicalGrowerResolutionSet resolver)
    {
        if (string.IsNullOrWhiteSpace(historicalName)) return false;
        var resolved = resolver.Resolve(historicalName, null);
        if (resolved.IsMapped && resolved.CanonicalGrowerId == reviewedGrower.CanonicalGrowerId) return true;

        // Older receipts sometimes retained the base grower name before Organic/Conventional
        // and origin qualifiers were added. A word-boundary prefix is acceptable only after the
        // immutable grower number has uniquely resolved the reviewed canonical grower.
        var historicalKey = CanonicalGrowerService.NormalizeGrowerKey(historicalName);
        var reviewedKey = CanonicalGrowerService.NormalizeGrowerKey(reviewedGrower.DisplayName);
        return historicalKey.Length >= 5
            && (reviewedKey.StartsWith(historicalKey + "_", StringComparison.Ordinal)
                || historicalKey.StartsWith(reviewedKey + "_", StringComparison.Ordinal));
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    private static async Task<LegacyGrowerLotReconciliationResult> RollbackAsync(
        IDbContextTransaction? transaction,
        LegacyGrowerLotReconciliationResult result,
        CancellationToken cancellationToken)
    {
        if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
        return result;
    }

    private static LegacyGrowerLotReconciliationResult Failed(string state, string message) =>
        new(state, false, false, false, message);
}
