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

public sealed record OperationalInventoryDiagnostic(
    int PositivePositions,
    int PositiveBins,
    int OperablePositions,
    int OperableBins,
    int NeedsReconciliationPositions,
    int NeedsReconciliationBins,
    int UnresolvedGrowers,
    int MissingGrowerLots,
    int DisplayKeyCollisions,
    int ReceiptIdentityDriftRows,
    int TreatmentMismatches,
    int RoomQuantityMismatches,
    int RoomsPreventedFromTransfer,
    int RoomsPartiallyOperable,
    IReadOnlyList<OperationalInventoryRoomDiagnostic> Rooms,
    IReadOnlyList<string> PositionIssues);

public sealed record OperationalInventoryRoomDiagnostic(
    string Facility,
    int RoomId,
    string Room,
    int CurrentBins,
    int OperableBins,
    int NeedsReconciliationBins,
    int CanonicalPositions,
    int DisplayKeyCollisions,
    int MissingGrowerLots,
    int UnresolvedGrowers,
    int TreatmentMismatches,
    string TransferProjectionStatus);

public sealed record InventoryIdentityReadinessResult(
    bool IsReady,
    IReadOnlyList<string> Issues,
    OperationalInventoryDiagnostic? OperationalInventory = null);

public interface IInventoryIdentityReadinessService
{
    Task<InventoryIdentityReadinessResult> VerifyAsync(CancellationToken cancellationToken);
}

public sealed class InventoryIdentityReadinessService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IInventoryIdentityService identities,
    IRoomTreatmentService? roomTreatments = null) : IInventoryIdentityReadinessService
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
                var resolution = correction.CorrectedReceiptId is long receiptId
                    ? await identities.ResolveForReceiptAsync(source, receiptId, cancellationToken)
                    : await identities.ResolveAsync(source, cancellationToken);
                if (!resolution.IsSuperseded) issues.Add($"Correction {correction.Id} does not resolve away from its source.");
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(exception.Message);
            }
            if (!correction.IsComplete || correction.InventoryAdjustments.Count != correction.ExpectedAdjustmentCount
                || correction.TreatmentLineageMovements.Count != correction.ExpectedTreatmentMovementCount)
                issues.Add($"Correction {correction.Id} is incomplete or has a parent-count mismatch.");
            if (correction.CorrectedReceiptId is null)
            {
                var obsoleteBins = snapshots.Where(x => x.CropYear == source.CropYear
                    && x.GrowerLotId == source.GrowerLotId && x.FruitProfileId == source.FruitProfileId && x.CurrentBins > 0)
                    .Sum(x => x.CurrentBins);
                if (obsoleteBins > 0) issues.Add($"Correction {correction.Id} leaves {obsoleteBins} current bins on obsolete identity {source}.");
                var obsoleteTreatment = await dbContext.TreatmentLineageSegments.AsNoTracking()
                    .Where(x => x.CurrentBins > 0 && x.CropYear == source.CropYear
                        && x.GrowerLotId == source.GrowerLotId && x.FruitProfileId == source.FruitProfileId)
                    .SumAsync(x => (int?)x.CurrentBins, cancellationToken) ?? 0;
                if (obsoleteTreatment > 0) issues.Add($"Correction {correction.Id} leaves {obsoleteTreatment} treatment-lineage bins on obsolete identity {source}.");
            }
            if (correction.CorrectedReceiptId is null
                && await dbContext.RoomInventoryAdjustments.AsNoTracking().AnyAsync(x => x.AdjustmentAt > correction.CreatedAt
                    && x.CropYear == source.CropYear && x.GrowerLotId == source.GrowerLotId
                    && x.FruitProfileId == source.FruitProfileId && x.ChangeAmount > 0
                    && x.InventoryIdentityCorrectionId != correction.Id, cancellationToken))
                issues.Add($"Obsolete identity {source} was recreated after correction {correction.Id}.");
        }
        var positive = snapshots.Where(x => x.CurrentBins > 0).ToList();
        var positionIssues = new List<string>();
        var available = new HashSet<string>(StringComparer.Ordinal);
        var treatmentMismatchPositions = new HashSet<string>(StringComparer.Ordinal);
        var treatmentMismatches = 0;
        IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>? treatmentSelections = null;
        if (roomTreatments is not null && positive.Count > 0)
            treatmentSelections = await roomTreatments.GetSelectionsAsync(positive, cancellationToken);

        foreach (var snapshot in positive)
        {
            var reason = OperationalInventoryPosition.UnavailableReason(snapshot);
            if (reason is null && treatmentSelections is not null)
            {
                if (!treatmentSelections.TryGetValue(RoomTreatmentService.SelectionLookupKey(snapshot), out var segments)
                    || segments.Any(x => !x.IsAvailable || x.CurrentBins <= 0)
                    || segments.Sum(x => x.CurrentBins) != snapshot.CurrentBins)
                {
                    treatmentMismatches++;
                    treatmentMismatchPositions.Add(OperationalInventoryPosition.Key(snapshot));
                    reason = "Needs Reconciliation — treatment segments do not equal canonical position bins.";
                }
            }
            if (reason is null)
                available.Add(OperationalInventoryPosition.Key(snapshot));
            else
                positionIssues.Add($"{snapshot.Facility}/{snapshot.Room}: {snapshot.CurrentBins} bins; {snapshot.GrowerNumber ?? snapshot.Lot}/{snapshot.Variety}; adjustment {snapshot.LatestAdjustmentId}; {reason}");
        }

        var driftRows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId != null && x.ChangeAmount > 0)
            .Select(x => new
            {
                x.Id,
                x.GrowerLotId,
                x.FruitProfileId,
                x.CropYear,
                x.LotNumber,
                ReceiptId = x.ReceiptId!.Value,
                ReceiptNumber = x.Receipt!.CompuTechReceiptId,
                ReceiptGrowerLotId = x.Receipt!.GrowerLotId,
                ReceiptFruitProfileId = (int?)x.Receipt.FruitProfileId,
                ReceiptCropYear = (int?)x.Receipt.CropYear,
                ReceiptGrowerNumber = x.Receipt.GrowerNumber,
                ReceiptLot = x.Receipt.LotCode
            })
            .ToListAsync(cancellationToken);
        var identityDrift = driftRows.Where(x =>
            x.GrowerLotId != x.ReceiptGrowerLotId
            || x.FruitProfileId != x.ReceiptFruitProfileId
            || x.CropYear != x.ReceiptCropYear
            || (!string.IsNullOrWhiteSpace(x.LotNumber)
                && !string.Equals(x.LotNumber, x.ReceiptGrowerNumber ?? x.ReceiptLot, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var receiptIdentityDriftRows = identityDrift.Count;
        positionIssues.AddRange(identityDrift.Select(x =>
            $"Receipt identity drift contributor: {x.ReceiptNumber} / adjustment {x.Id}; historical lot {x.LotNumber}, GL {x.GrowerLotId?.ToString() ?? "none"}, FP {x.FruitProfileId?.ToString() ?? "none"}, crop {x.CropYear?.ToString() ?? "none"}; current Receipt lot {x.ReceiptGrowerNumber ?? x.ReceiptLot}, GL {x.ReceiptGrowerLotId?.ToString() ?? "none"}, FP {x.ReceiptFruitProfileId}, crop {x.ReceiptCropYear}."));

        var displayCollisions = positive
            .GroupBy(x => string.Join('|', x.RoomId, x.CropYear, x.Lot.Trim().ToUpperInvariant(),
                x.Variety.Trim().ToUpperInvariant(), x.FruitProfileId))
            .Count(x => x.Select(OperationalInventoryPosition.CanonicalIdentityKey).Distinct(StringComparer.Ordinal).Count() > 1);
        var rooms = positive.GroupBy(x => x.RoomId).ToList();
        var roomQuantityMismatches = rooms.Count(x =>
            x.Where(y => available.Contains(OperationalInventoryPosition.Key(y))).Sum(y => y.CurrentBins)
            + x.Where(y => !available.Contains(OperationalInventoryPosition.Key(y))).Sum(y => y.CurrentBins)
            != x.Sum(y => y.CurrentBins));
        if (roomQuantityMismatches > 0)
            issues.Add($"{roomQuantityMismatches} room(s) have an operational-position quantity mismatch.");
        var operable = positive.Where(x => available.Contains(OperationalInventoryPosition.Key(x))).ToList();
        var unavailable = positive.Where(x => !available.Contains(OperationalInventoryPosition.Key(x))).ToList();
        var roomDiagnostics = rooms.Select(room =>
        {
            var roomOperableBins = room.Where(x => available.Contains(OperationalInventoryPosition.Key(x))).Sum(x => x.CurrentBins);
            var roomNeedsBins = room.Sum(x => x.CurrentBins) - roomOperableBins;
            var roomDisplayCollisions = room
                .GroupBy(x => string.Join('|', x.CropYear, x.Lot.Trim().ToUpperInvariant(),
                    x.Variety.Trim().ToUpperInvariant(), x.FruitProfileId))
                .Count(x => x.Select(OperationalInventoryPosition.CanonicalIdentityKey)
                    .Distinct(StringComparer.Ordinal).Count() > 1);
            var status = roomOperableBins == 0
                ? "Blocked - every current position needs reconciliation"
                : roomNeedsBins == 0
                    ? "Operable"
                    : "Partially operable - exact positions remain available";
            var first = room.First();
            return new OperationalInventoryRoomDiagnostic(
                first.Facility, first.RoomId, first.Room, room.Sum(x => x.CurrentBins), roomOperableBins,
                roomNeedsBins, room.Count(), roomDisplayCollisions, room.Count(x => x.GrowerLotId is null),
                room.Count(x => string.IsNullOrWhiteSpace(x.GrowerNumber)),
                room.Count(x => treatmentMismatchPositions.Contains(OperationalInventoryPosition.Key(x))), status);
        }).OrderBy(x => x.Facility).ThenBy(x => x.Room).ToList();
        var operational = new OperationalInventoryDiagnostic(
            positive.Count,
            positive.Sum(x => x.CurrentBins),
            operable.Count,
            operable.Sum(x => x.CurrentBins),
            unavailable.Count,
            unavailable.Sum(x => x.CurrentBins),
            positive.Count(x => string.IsNullOrWhiteSpace(x.GrowerNumber)),
            positive.Count(x => x.GrowerLotId is null),
            displayCollisions,
            receiptIdentityDriftRows,
            treatmentMismatches,
            roomQuantityMismatches,
            rooms.Count(x => x.All(y => !available.Contains(OperationalInventoryPosition.Key(y)))),
            rooms.Count(x => x.Any(y => available.Contains(OperationalInventoryPosition.Key(y)))
                && x.Any(y => !available.Contains(OperationalInventoryPosition.Key(y)))),
            roomDiagnostics,
            positionIssues);
        return new(issues.Count == 0, issues, operational);
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
    private const string ReviewedEvidenceFingerprint = "d56279722867fbf2d8376e00b8d50bd6";
    private static readonly DateTimeOffset BadOverrideAt = DateTimeOffset.Parse("2026-09-01T14:22:48.979510Z", CultureInfo.InvariantCulture);

    public async Task<Tr508901RepairResult> RunAsync(bool apply, string requestedBy, CancellationToken cancellationToken)
    {
        var existing = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationKey == OperationKey, cancellationToken);
        var receipt = await dbContext.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1229, cancellationToken);
        if (receipt is null || receipt.CompuTechReceiptId != "TR508901" || receipt.CropYear != 2026
            || receipt.GrowerLotId != 538 || receipt.FruitProfileId != 26
            || receipt.GrowerNumber != "4101" || receipt.LotCode != "4101"
            || receipt.BinCount != 40 || receipt.IsDeleted)
            return Fail("State C", "TR508901 Receipt identity or quantity differs from the reviewed evidence.");

        var snapshots = await ledger.GetSnapshotsAsync(null, null, cancellationToken);
        var historicalAdjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => new long[] { 2094, 2130, 2131, 2211, 2212, 2213, 2214 }.Contains(x.Id))
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var adjustment2094 = historicalAdjustments.SingleOrDefault(x => x.Id == 2094);
        var adjustment2212 = historicalAdjustments.SingleOrDefault(x => x.Id == 2212);
        var segment356 = await dbContext.TreatmentLineageSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 356, cancellationToken);
        var transfers = await dbContext.RoomTransfers.AsNoTracking().Where(x => x.Id == 312 || x.Id == 317)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var movements = await dbContext.TreatmentLineageMovements.AsNoTracking().Where(x => x.Id == 245 || x.Id == 263)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var badOverride = await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == Guid.Parse("0a94d451-b4f3-4097-9907-de3697b8f1f9"), cancellationToken);
        var rooms = await dbContext.Rooms.AsNoTracking().Where(x => x.Id == 59 || x.Id == 60 || x.Id == 62)
            .ToDictionaryAsync(x => x.Id,
                x => !string.IsNullOrWhiteSpace(x.CropQcRoomName) ? x.CropQcRoomName
                    : !string.IsNullOrWhiteSpace(x.DisplayName) ? x.DisplayName
                    : x.Code,
                cancellationToken);
        var reviewedHistoryMismatch = segment356 is null
            ? "treatment segment 356"
            : ReviewedHistoryMismatch(historicalAdjustments, transfers, movements, badOverride, segment356,
                existing is null ? 40 : 0, rooms);
        if (adjustment2094 is null || adjustment2212 is null || segment356 is null
            || reviewedHistoryMismatch is not null)
            return Fail("State C", $"Required immutable TR508901 ledger or treatment evidence is absent ({reviewedHistoryMismatch ?? "required row"}).");
        var room8 = adjustment2094.RoomId;
        var room10 = adjustment2212.RoomId;
        int Current(int roomId, InventoryIdentityKey identity) => snapshots
            .Where(x => x.RoomId == roomId && x.CropYear == identity.CropYear
                && x.GrowerLotId == identity.GrowerLotId && x.FruitProfileId == identity.FruitProfileId)
            .Sum(x => x.CurrentBins);
        var correctionAdjustments = existing is null ? [] : await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId == existing.Id).OrderBy(x => x.InventoryOperationKey).ToListAsync(cancellationToken);
        var correctionMovements = existing is null ? [] : await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId == existing.Id).ToListAsync(cancellationToken);
        var repairAudits = existing is null ? [] : await dbContext.AuditLogs.AsNoTracking().Where(x =>
            x.Action == "TR508901InventoryIdentityRepair" && x.EntityName == nameof(InventoryIdentityCorrection)
            && x.EntityKey == existing.Id.ToString("D")).ToListAsync(cancellationToken);
        var targetTreatmentSegments = await dbContext.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == room10
            && x.CropYear == 2026 && x.GrowerLotId == 538 && x.FruitProfileId == 26
            && x.CurrentBins > 0).ToListAsync(cancellationToken);
        var mcd06Orda = snapshots.Where(x => x.RoomId == 58 && x.CropYear == 2026 && x.GrowerLotId == 538
            && x.FruitProfileId == 21).Sum(x => x.CurrentBins);
        var stateB = existing is { IsComplete: true, IsActive: true, ExpectedAdjustmentCount: 4, ExpectedTreatmentMovementCount: 1 }
            && existing.SourceCropYear == 2026 && existing.SourceGrowerLotId == 538 && existing.SourceFruitProfileId == 18
            && existing.TargetCropYear == 2026 && existing.TargetGrowerLotId == 538 && existing.TargetFruitProfileId == 26
            && existing.CorrectedReceiptId == 1229 && existing.ReceiptInventoryOverrideId is null
            && existing.Reason == "Bounded TR508901 stale-room identity repair"
            && existing.SourceIdentitySnapshotJson.Contains(ReviewedEvidenceFingerprint, StringComparison.Ordinal)
            && CorrectionAdjustmentsAreExact(correctionAdjustments, room8, room10, existing)
            && correctionMovements.Count == 1 && correctionMovements[0].MovementType == TreatmentLineageMovementTypes.IdentityReclassification
            && correctionMovements[0].OperationKey == $"identity-correction:{OperationKey}:room:{room10}:356"
            && correctionMovements[0].SourceSegmentId == 356 && correctionMovements[0].DestinationSegmentId is not null
            && correctionMovements[0].SourceRoomId == room10 && correctionMovements[0].DestinationRoomId == room10
            && correctionMovements[0].BinCount == 40 && correctionMovements[0].InventoryIdentityCorrectionId == existing.Id
            && correctionMovements[0].IdentityKey == "2026|538|26|4101|4101|ORDR|ORGANIC|True|"
            && correctionMovements[0].TreatmentStateSnapshot == TreatmentLineageStates.Untreated
            && correctionMovements[0].TreatmentSignatureSnapshot == "u" && correctionMovements[0].ReceiptId is null
            && correctionMovements[0].RoomTransferId is null && correctionMovements[0].RoomInventoryLossId is null
            && correctionMovements[0].BinsRunEntryId is null && correctionMovements[0].ProcessorShipmentLineId is null
            && correctionMovements[0].OutsideWarehouseTransferId is null && correctionMovements[0].InterCrewTransferId is null
            && correctionMovements[0].ReversesTreatmentLineageMovementId is null
            && correctionMovements[0].OccurredAt == existing.CreatedAt && correctionMovements[0].CreatedAt == existing.CreatedAt
            && correctionMovements[0].CreatedByUserId == existing.CreatedByUserId
            && RepairAuditIsExact(repairAudits, existing)
            && mcd06Orda == 66
            && Current(room10, Source) == 0 && Current(room8, Source) == 0
            && Current(room10, Target) == 40 && Current(room8, Target) == 0
            && segment356.CurrentBins == 0
            && targetTreatmentSegments.Count == 1
            && targetTreatmentSegments[0].Id == correctionMovements[0].DestinationSegmentId
            && targetTreatmentSegments[0].CurrentBins == 40
            && targetTreatmentSegments[0].TreatmentState == TreatmentLineageStates.Untreated
            && targetTreatmentSegments[0].TreatmentSignature == "u";
        if (stateB) return new("State B", true, false, true, "TR508901 repair is already applied.", existing!.Id);
        if (existing is not null) return Fail("State C", "A partial or incompatible TR508901 repair record exists.");

        var requiredAdjustmentIds = new long[] { 2094, 2130, 2131, 2211, 2212, 2213, 2214 };
        var noLaterMovement = !await dbContext.TreatmentLineageMovements.AsNoTracking()
            .AnyAsync(x => x.Id > 263 && (x.SourceSegmentId == 356 || x.DestinationSegmentId == 356
                || (x.OccurredAt > BadOverrideAt && x.IdentityKey == segment356.IdentityKey)), cancellationToken);
        var noLaterLedgerOrCustody = !await dbContext.RoomInventoryAdjustments.AsNoTracking().AnyAsync(x =>
            x.AdjustmentAt > BadOverrideAt && x.InventoryIdentityCorrectionId == null
            && x.CropYear == 2026 && x.GrowerLotId == 538 && (x.FruitProfileId == 18 || x.FruitProfileId == 26), cancellationToken)
            && !await dbContext.InterCrewTransfers.AsNoTracking().AnyAsync(x => x.LoadedAt > BadOverrideAt
                && x.CropYear == 2026 && x.GrowerLotId == 538 && (x.FruitProfileId == 18 || x.FruitProfileId == 26), cancellationToken)
            && !await dbContext.OutsideWarehouseTransfers.AsNoTracking().AnyAsync(x => x.TransferredAt > BadOverrideAt
                && x.CropYear == 2026 && x.GrowerLotId == 538 && (x.FruitProfileId == 18 || x.FruitProfileId == 26), cancellationToken);
        var stateA = historicalAdjustments.Count == requiredAdjustmentIds.Length
            && Current(room10, Source) == 40 && Current(room8, Source) == -40
            && Current(room10, Target) == 0 && Current(room8, Target) == 40
            && segment356.RoomId == room10 && segment356.CropYear == 2026 && segment356.GrowerLotId == 538
            && segment356.FruitProfileId == 18 && segment356.CurrentBins == 40
            && segment356.TreatmentState == TreatmentLineageStates.Untreated && segment356.TreatmentSignature == "u"
            && noLaterMovement && noLaterLedgerOrCustody;
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
                SourceIdentitySnapshotJson = JsonSerializer.Serialize(new { Identity = Source, ReviewedEvidenceFingerprint }),
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
                // InventoryStatus is a legacy optional discriminator. FruitProfile,
                // ProductionType, and IsOrganic carry the authoritative target identity;
                // keep this empty to match rollback-compatible ledger rows and prevent a
                // second treatment segment for the same canonical fruit.
                InventoryStatus = ""
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
                    // Keep the room-only reclassification independent of the Receipt's original
                    // room. The durable correction owns receipt provenance through CorrectedReceiptId.
                    ReceiptId = null,
                    WarehouseId = roomId == room10 ? adjustment2212.WarehouseId : adjustment2094.WarehouseId,
                    RoomId = roomId,
                    GrowerLotId = identity.GrowerLotId,
                    FruitProfileId = identity.FruitProfileId,
                    GrowerName = growerLot.Grower,
                    LotNumber = growerLot.LotNumber,
                    VarietyCode = profile.VarietyCode,
                    // The current production transfer implementation derives canonical
                    // Organic/Conventional identity from FruitProfile, while its legacy
                    // raw transfer ledger rows leave InventoryStatus null. Preserve that
                    // rollback-compatible row shape; the correction parent and profile
                    // retain the authoritative target identity.
                    InventoryStatus = null,
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
                    // The current production rollback binary predates the durable identity-correction
                    // parent. Keep these bounded repair rows nonblocking there while the new binary
                    // still validates every correction-linked row explicitly.
                    InventoryInvariantVersion = 0,
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

    private static string? ReviewedHistoryMismatch(
        IReadOnlyList<RoomInventoryAdjustment> adjustments,
        IReadOnlyList<RoomTransfer> transfers,
        IReadOnlyList<TreatmentLineageMovement> movements,
        ReceiptInventoryOverride? operation,
        TreatmentLineageSegment segment356,
        int expectedSegment356CurrentBins,
        IReadOnlyDictionary<int, string> rooms)
    {
        if (adjustments.Count != 7 || transfers.Count != 2 || movements.Count != 2
            || !rooms.TryGetValue(60, out var room8) || room8 != "MCD-08"
            || !rooms.TryGetValue(59, out var room7) || room7 != "MCD-07"
            || !rooms.TryGetValue(62, out var room10) || room10 != "MCD-10") return "row counts or room codes";
        var a = adjustments.ToDictionary(x => x.Id);
        var reviewedOverrideId = Guid.Parse("0a94d451-b4f3-4097-9907-de3697b8f1f9");
        var exact2094 = ExactAdjustment(a[2094], 1229, 60, 18, "ReceiptAdd", 40, null, 40,
            null, null, "Receiving inventory added", "Receipt TR508901 added 40 bins to MCD-08.",
            DateTimeOffset.Parse("2026-08-31T02:08:00Z", CultureInfo.InvariantCulture), null, null, null);
        var exact2130 = ExactAdjustment(a[2130], null, 60, 18, "TransferOut", -40, 40, 0,
            "DANJ", null, "Final Room", "Transfer to McDougall/MCD-07.",
            DateTimeOffset.Parse("2026-08-31T18:03:00Z", CultureInfo.InvariantCulture), 312,
            "transfer:4a83a3a423ef4c6ea505bbd69a4cc569:out", null);
        var exact2131 = ExactAdjustment(a[2131], null, 59, 18, "TransferIn", 40, 0, 40,
            "DANJ", null, "Final Room", "Transfer from McDougall/MCD-08.",
            DateTimeOffset.Parse("2026-08-31T18:03:00Z", CultureInfo.InvariantCulture), 312,
            "transfer:4a83a3a423ef4c6ea505bbd69a4cc569:in", null);
        var exact2211 = ExactAdjustment(a[2211], null, 59, 18, "TransferOut", -40, 40, 0,
            "DANJ", null, "Final Room", "Transfer to McDougall/MCD-10.",
            DateTimeOffset.Parse("2026-09-01T14:20:00Z", CultureInfo.InvariantCulture), 317,
            "transfer:48746aa715af419db89e55fbb8d26f27:out", null);
        var exact2212 = ExactAdjustment(a[2212], null, 62, 18, "TransferIn", 40, 0, 40,
            "DANJ", null, "Final Room", "Transfer from McDougall/MCD-07.",
            DateTimeOffset.Parse("2026-09-01T14:20:00Z", CultureInfo.InvariantCulture), 317,
            "transfer:48746aa715af419db89e55fbb8d26f27:in", null);
        var exact2213 = ExactAdjustment(a[2213], 1229, 60, 18, "ReceiptAdminOverride", -40, 40, 0,
            "DANJ", "Conventional", "Receipt Admin Override", "ReclassifyOldIdentity; override 0a94d451-b4f3-4097-9907-de3697b8f1f9.",
            BadOverrideAt, null, "receipt-override:4917d6c0612d407882466a066bfa4cbe:1", reviewedOverrideId);
        var exact2214 = ExactAdjustment(a[2214], 1229, 60, 26, "ReceiptAdminOverride", 40, 0, 40,
            "ORDR", "Organic", "Receipt Admin Override", "ReclassifyNewIdentity; override 0a94d451-b4f3-4097-9907-de3697b8f1f9.",
            BadOverrideAt, null, "receipt-override:4917d6c0612d407882466a066bfa4cbe:2", reviewedOverrideId);

        var t312 = transfers.SingleOrDefault(x => x.Id == 312);
        var t317 = transfers.SingleOrDefault(x => x.Id == 317);
        var transfersExact = ExactTransfer(t312, 60, 59, "4a83a3a423ef4c6ea505bbd69a4cc569",
                DateTimeOffset.Parse("2026-08-31T18:03:00Z", CultureInfo.InvariantCulture))
            && ExactTransfer(t317, 59, 62, "48746aa715af419db89e55fbb8d26f27",
                DateTimeOffset.Parse("2026-09-01T14:20:00Z", CultureInfo.InvariantCulture));
        var movement245 = movements.SingleOrDefault(x => x.Id == 245);
        var movement263 = movements.SingleOrDefault(x => x.Id == 263);
        const string identityKey = "2026|538|18|4101|4101|DANJ|CONVENTIONAL|False|";
        var movementsExact = ExactMovement(movement245, 342, 343, 60, 59, 312,
                "transfer:4a83a3a423ef4c6ea505bbd69a4cc569:treatment", identityKey,
                DateTimeOffset.Parse("2026-08-31T18:03:00Z", CultureInfo.InvariantCulture))
            && ExactMovement(movement263, 343, 356, 59, 62, 317,
                "transfer:48746aa715af419db89e55fbb8d26f27:treatment", identityKey,
                DateTimeOffset.Parse("2026-09-01T14:20:00Z", CultureInfo.InvariantCulture));
        var overrideExact = operation is
        {
            Id: var id,
            ReceiptId: 1229,
            ActionType: ReceiptInventoryOverrideActionTypes.InventoryReclassification,
            Reason: "Wrong Variety",
            OldReceiptBinCount: 40,
            NewReceiptBinCount: 40,
            InventoryDelta: 0,
            IsComplete: true,
            ExpectedAdjustmentCount: 2,
            OperationKey: "4917d6c0612d407882466a066bfa4cbe"
        } && id == reviewedOverrideId && operation.CreatedAt == BadOverrideAt
            && operation.BeforeReceiptSnapshotJson.Contains("\"fruitProfileId\":18", StringComparison.Ordinal)
            && operation.AfterReceiptSnapshotJson.Contains("\"fruitProfileId\":26", StringComparison.Ordinal);
        var segmentExact = segment356.WarehouseId == 3 && segment356.RoomId == 62
            && segment356.CropYear == 2026 && segment356.GrowerLotId == 538 && segment356.FruitProfileId == 18
            && segment356.IdentityKey == identityKey && segment356.CurrentBins == expectedSegment356CurrentBins
            && segment356.TreatmentState == TreatmentLineageStates.Untreated && segment356.TreatmentSignature == "u"
            && segment356.ReceiptId is null;
        var failures = new List<string>();
        if (!exact2094) failures.Add("adjustment 2094");
        if (!exact2130) failures.Add("adjustment 2130");
        if (!exact2131) failures.Add("adjustment 2131");
        if (!exact2211) failures.Add("adjustment 2211");
        if (!exact2212) failures.Add("adjustment 2212");
        if (!exact2213) failures.Add("adjustment 2213");
        if (!exact2214) failures.Add("adjustment 2214");
        if (!transfersExact) failures.Add("room transfers 312/317");
        if (!movementsExact) failures.Add("treatment movements 245/263");
        if (!overrideExact) failures.Add("receipt override");
        if (!segmentExact) failures.Add("treatment segment 356");
        return failures.Count == 0 ? null : string.Join(", ", failures);
    }

    private static bool ExactAdjustment(RoomInventoryAdjustment? x, long? receiptId, int roomId, int fruitProfileId,
        string type, int change, int? oldBins, int newBins, string? variety, string? inventoryStatus,
        string source, string notes, DateTimeOffset at, long? transferId, string? operationKey, Guid? overrideId)
    {
        if (x is null) return false;
        var expectedCreatedAt = x.Id switch
        {
            2094 => DateTimeOffset.Parse("2026-08-31T02:09:02.139319Z", CultureInfo.InvariantCulture),
            2130 => DateTimeOffset.Parse("2026-08-31T18:04:03.786701Z", CultureInfo.InvariantCulture),
            2131 => DateTimeOffset.Parse("2026-08-31T18:04:03.786884Z", CultureInfo.InvariantCulture),
            2211 => DateTimeOffset.Parse("2026-09-01T14:21:46.116844Z", CultureInfo.InvariantCulture),
            2212 => DateTimeOffset.Parse("2026-09-01T14:21:46.116953Z", CultureInfo.InvariantCulture),
            2213 or 2214 => BadOverrideAt,
            _ => DateTimeOffset.MinValue
        };
        return x.ReceiptId == receiptId && x.WarehouseId == 3 && x.RoomId == roomId
            && x.CropYear == 2026 && x.GrowerLotId == 538 && x.FruitProfileId == fruitProfileId
            && x.GrowerName == "Conconully Prs ORG CHIL" && x.LotNumber == "4101"
            && x.AdjustmentType == type && x.ChangeAmount == change && x.OldBinCount == oldBins && x.NewBinCount == newBins
            && x.VarietyCode == variety && x.InventoryStatus == inventoryStatus && x.Source == source
            && x.Reason == (type == "ReceiptAdminOverride" ? "Wrong Variety" : source)
            && x.Notes == notes && x.AdjustmentAt == at && x.CreatedAt == expectedCreatedAt && x.CreatedByUserId == 1
            && x.RoomTransferId == transferId && x.InventoryOperationKey == operationKey
            && x.ReceiptInventoryOverrideId == overrideId;
    }

    private static bool ExactTransfer(RoomTransfer? x, int sourceRoom, int destinationRoom, string key, DateTimeOffset at) =>
        x is not null && x.SourceWarehouseId == 3 && x.DestinationWarehouseId == 3
        && x.SourceRoomId == sourceRoom && x.DestinationRoomId == destinationRoom
        && x.CropYear == 2026 && x.GrowerLotId == 538 && x.FruitProfileId == 18
        && x.LotNumber == "4101" && x.VarietyCode == "DANJ" && x.BinCount == 40
        && x.OperationKey == key && x.TransferredAt == at && !x.IsReversed && x.ReversesRoomTransferId is null;

    private static bool ExactMovement(TreatmentLineageMovement? x, long sourceSegment, long destinationSegment,
        int sourceRoom, int destinationRoom, long transferId, string key, string identityKey, DateTimeOffset at) =>
        x is not null && x.SourceSegmentId == sourceSegment && x.DestinationSegmentId == destinationSegment
        && x.SourceRoomId == sourceRoom && x.DestinationRoomId == destinationRoom && x.RoomTransferId == transferId
        && x.BinCount == 40 && x.MovementType == TreatmentLineageMovementTypes.Transfer
        && x.OperationKey == key && x.IdentityKey == identityKey && x.OccurredAt == at
        && x.TreatmentStateSnapshot == TreatmentLineageStates.Untreated && x.TreatmentSignatureSnapshot == "u"
        && x.ReceiptId is null && x.ReversesTreatmentLineageMovementId is null;

    private static bool CorrectionAdjustmentsAreExact(
        IReadOnlyList<RoomInventoryAdjustment> rows,
        int room8,
        int room10,
        InventoryIdentityCorrection correction)
    {
        var expectedKeys = Enumerable.Range(1, 4)
            .Select(x => $"identity-correction:{OperationKey}:{x}").ToHashSet(StringComparer.Ordinal);
        if (rows.Count != 4 || !rows.Select(x => x.InventoryOperationKey ?? "").ToHashSet(StringComparer.Ordinal).SetEquals(expectedKeys)
            || rows.Any(x => x.InventoryIdentityCorrectionId != correction.Id
                || x.CropYear != 2026 || x.GrowerLotId != 538
                || x.AdjustmentType != InventoryIdentityWriteGuard.AdjustmentType
                || x.Source != "TR508901 bounded identity repair" || x.Reason != correction.Reason
                || x.ReceiptId is not null || x.WarehouseId != 3
                || x.CreatedByUserId != correction.CreatedByUserId
                || x.AdjustmentAt != correction.CreatedAt || x.CreatedAt != correction.CreatedAt
                || x.InventoryInvariantVersion != 0 || x.InventoryStatus is not null)) return false;
        return rows.Count(x => x.RoomId == room10 && x.FruitProfileId == 18 && x.ChangeAmount == -40 && x.OldBinCount == 40 && x.NewBinCount == 0) == 1
            && rows.Count(x => x.RoomId == room10 && x.FruitProfileId == 26 && x.ChangeAmount == 40 && x.OldBinCount == 0 && x.NewBinCount == 40) == 1
            && rows.Count(x => x.RoomId == room8 && x.FruitProfileId == 26 && x.ChangeAmount == -40 && x.OldBinCount == 40 && x.NewBinCount == 0) == 1
            && rows.Count(x => x.RoomId == room8 && x.FruitProfileId == 18 && x.ChangeAmount == 40 && x.OldBinCount == -40 && x.NewBinCount == 0) == 1;
    }

    private static bool RepairAuditIsExact(IReadOnlyList<AuditLog> rows, InventoryIdentityCorrection correction)
    {
        if (rows.Count != 1) return false;
        var row = rows[0];
        var expectedBefore = JsonSerializer.Serialize(new
        {
            MCD10FP18 = 40,
            MCD08FP18 = -40,
            MCD08FP26 = 40,
            Segment356 = "FP18/40/u"
        });
        var expectedAfter = JsonSerializer.Serialize(new
        {
            MCD10FP18 = 0,
            MCD08FP18 = 0,
            MCD08FP26 = 0,
            MCD10FP26 = 40,
            Treatment = "FP26/40/u",
            HistoricalRowsRetained = new long[] { 2094, 2130, 2131, 2211, 2212, 2213, 2214 }
        });
        return row.UserId == correction.CreatedByUserId && row.CreatedAt == correction.CreatedAt
            && row.SourceApplication == "CropQc.Web bounded repair"
            && row.BeforeValuesJson == expectedBefore && row.AfterValuesJson == expectedAfter;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    private static bool Matches(RoomInventoryLedgerSnapshot x, InventoryIdentityKey key) =>
        x.CropYear == key.CropYear && x.GrowerLotId == key.GrowerLotId && x.FruitProfileId == key.FruitProfileId;
    private static Tr508901RepairResult Fail(string state, string message) => new(state, false, false, false, message);
}
