using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CropQc.Web.Services;

public sealed record ReceiptQuantityCheck(long ReceiptId, int WarehouseId, int ReceiptBins, long LedgerBins)
{
    public long Difference => ReceiptBins - LedgerBins;
}

public sealed record ReceiptQuantityReconciliation(int CropYear, IReadOnlyList<ReceiptQuantityCheck> Receipts)
{
    public int ReceiptCount => Receipts.Count;
    public long ReceiptTotal => Receipts.Sum(x => (long)x.ReceiptBins);
    public long LedgerTotal => Receipts.Sum(x => x.LedgerBins);
    public long NetDifference => ReceiptTotal - LedgerTotal;
    public IReadOnlyList<long> MismatchReceiptIds => Receipts.Where(x => x.Difference != 0).Select(x => x.ReceiptId).ToList();
    public int MismatchCount => MismatchReceiptIds.Count;
}

public sealed record InventoryFacilityConservation(
    string Facility, IReadOnlyDictionary<string, long> Categories, long AuthoritativeCurrentBins)
{
    public long AccountedBins => Categories.Values.Sum();
    public long Difference => AccountedBins - AuthoritativeCurrentBins;
}

public sealed record InventoryConservationReport(
    ReceiptQuantityReconciliation Receiving,
    IReadOnlyList<InventoryFacilityConservation> Facilities,
    InventoryFacilityConservation Global,
    IReadOnlyList<string> UnclassifiedTypes,
    IReadOnlyList<long> UnbalancedRoomTransfers,
    IReadOnlyList<Guid> UnbalancedIdentityCorrections,
    long TreatmentIdentityDebit,
    long TreatmentIdentityCredit,
    int InvalidTreatmentIdentityMovements,
    IReadOnlyDictionary<string, long> OtherHistoricalTreatmentCorrections,
    long InterCrewTransitBins,
    long OutsideWarehouseCustodyBins,
    long ReceiptHeldBins = 0)
{
    public IReadOnlyList<InvalidLocationCorrection> InvalidLocationCorrections { get; init; } = [];
    public IReadOnlyList<InvalidReceiptCompensation> InvalidReceiptCompensations { get; init; } = [];
    public long TreatmentIdentityDifference => TreatmentIdentityCredit - TreatmentIdentityDebit;
    public bool IsReady => Receiving.MismatchCount == 0 && Global.Difference == 0
        && Facilities.All(x => x.Difference == 0) && UnclassifiedTypes.Count == 0
        && UnbalancedRoomTransfers.Count == 0 && UnbalancedIdentityCorrections.Count == 0
        && InvalidTreatmentIdentityMovements == 0 && TreatmentIdentityDifference == 0
        && InvalidLocationCorrections.Count == 0 && InvalidReceiptCompensations.Count == 0;
}

public sealed record InvalidLocationCorrection(Guid? CorrectionId, IReadOnlyList<long> AdjustmentIds);
public sealed record InvalidReceiptCompensation(long? ReversalId, IReadOnlyList<long> AdjustmentIds, IReadOnlyList<long> MovementIds);

/// <summary>Read-only quantity accounting. Unknown categories are reported and fail the release gate.</summary>
public sealed partial class InventoryConservationReportService(CropQcDbContext db, IRoomInventoryLedgerQueryService ledger, int cropYear)
{
    // This type-only predicate is also used by historical-only receipt editing.
    // Do not broaden that operation's proof: location types are recognized in the
    // report only alongside the complete parent/pair validation below.
    public static bool IsKnownMovement(string type) => type is
        "ReceiptAdd" or "ReceiptEdit" or "ReceiptAdminOverride" or "StartingInventoryImport"
        or "BinsRun" or "BinsRunReversal" or "Depletion" or "DepletionReversal"
        or "DroppedBins" or "DroppedBinsReversal" or "ProcessorShipment" or "ProcessorShipmentReversal"
        or "OutsideWarehouseTransfer" or "OutsideWarehouseTransferReversal"
        or "InterCrewTransferDispatch" or "InterCrewTransferReceive"
        or "InterCrewTransferReversalDestination" or "InterCrewTransferReversalSource"
        or TruckReceiptReconciliationService.ReturnToSource or TruckReceiptReconciliationService.ReopenDestination
        or "TransferIn" or "TransferOut" or "InventoryIdentityCorrection";

    public async Task<ReceiptQuantityReconciliation> ReconcileReceiptsAsync(CancellationToken cancellationToken)
    {
        // Crop identity, not a calendar cutoff, defines the complete receiving gate.
        var receipts = await db.Receipts.AsNoTracking()
            .Where(x => !x.IsTransferReceipt && !x.IsDeleted && !x.IsTestData && x.ReceiptType == "Truck receipt"
                && x.CropYear == cropYear)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WarehouseId, x.BinCount }).ToListAsync(cancellationToken);
        var ids = receipts.Select(x => x.Id).ToList();
        var rows = await db.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId != null && ids.Contains(x.ReceiptId.Value)
                && x.InventoryIdentityCorrectionId == null
                && (x.AdjustmentType == "ReceiptAdd" || x.AdjustmentType == "ReceiptEdit"
                    || (x.AdjustmentType == "ReceiptAdminOverride"
                        && x.ReceiptInventoryOverride!.ActionType == ReceiptInventoryOverrideActionTypes.QuantityCorrection)))
            .Select(x => new { ReceiptId = x.ReceiptId!.Value, x.ChangeAmount }).ToListAsync(cancellationToken);
        var totals = rows.GroupBy(x => x.ReceiptId).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.ChangeAmount));
        return new(cropYear, receipts.Select(x =>
            new ReceiptQuantityCheck(x.Id, x.WarehouseId, x.BinCount, totals.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<InventoryConservationReport> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var receiving = await ReconcileReceiptsAsync(cancellationToken);
        var allRows = await db.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Receipt).Include(x => x.ReceiptInventoryOverride).ToListAsync(cancellationToken);
        var effective = EffectiveAdjustments(allRows);
        var snapshots = await ledger.GetSnapshotsAsync(null, null, cancellationToken);
        var warehouses = await db.Warehouses.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var facilities = warehouses.Select(w => new InventoryFacilityConservation(w.Value,
            effective.Where(x => x.WarehouseId == w.Key).GroupBy(Category)
                .ToDictionary(x => x.Key, x => x.Sum(EffectiveBins)),
            snapshots.Where(x => x.WarehouseId == w.Key).Sum(x => (long)x.CurrentBins))).ToList();
        var global = new InventoryFacilityConservation("GLOBAL",
            facilities.SelectMany(x => x.Categories).GroupBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Value)),
            facilities.Sum(x => x.AuthoritativeCurrentBins));
        // Check complete immutable operations, before baseline filtering: an opening
        // baseline may supersede just one facility's side of a historical transfer.
        var pairs = await db.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.RoomTransferId != null || x.InventoryIdentityCorrectionId != null)
            .Select(x => new { x.RoomTransferId, x.InventoryIdentityCorrectionId, x.ChangeAmount })
            .ToListAsync(cancellationToken);
        var treatment = await db.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId != null
                && x.MovementType == TreatmentLineageMovementTypes.IdentityReclassification)
            .Select(x => new { x.SourceSegmentId, x.DestinationSegmentId, x.BinCount }).ToListAsync(cancellationToken);
        var otherTreatment = await db.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId != null
                && x.MovementType != TreatmentLineageMovementTypes.IdentityReclassification)
            .Select(x => new { x.MovementType, x.BinCount }).ToListAsync(cancellationToken);
        var invalidLocations = await ValidateLocationCorrectionsAsync(allRows, cancellationToken);
        var invalidCompensations = await ValidateReceiptCompensationsAsync(allRows, cancellationToken);
        return new(receiving, facilities, global,
            effective.Where(x => !IsKnownMovement(x.AdjustmentType) && !IsLocationMovement(x.AdjustmentType)
                && x.AdjustmentType != "ReceiptPlacementReversal").Select(x => x.AdjustmentType).Distinct().Order().ToList(),
            pairs.Where(x => x.RoomTransferId != null).GroupBy(x => x.RoomTransferId!.Value)
                .Where(x => x.Sum(y => (long)y.ChangeAmount) != 0).Select(x => x.Key).ToList(),
            pairs.Where(x => x.InventoryIdentityCorrectionId != null).GroupBy(x => x.InventoryIdentityCorrectionId!.Value)
                .Where(x => x.Sum(y => (long)y.ChangeAmount) != 0).Select(x => x.Key).ToList(),
            treatment.Sum(x => x.SourceSegmentId is null ? 0L : x.BinCount),
            treatment.Sum(x => x.DestinationSegmentId is null ? 0L : x.BinCount),
            treatment.Count(x => x.SourceSegmentId == null || x.DestinationSegmentId == null || x.BinCount <= 0),
            otherTreatment.GroupBy(x => x.MovementType).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.BinCount)),
            await db.InterCrewTransfers.Where(x => x.Status == InterCrewTransferStatuses.InTransit)
                .SumAsync(x => (long?)(x.BinsLoaded - (x.BinsReceived ?? 0)), cancellationToken) ?? 0,
            await db.OutsideWarehouseTransfers.Where(x => !x.IsReversed)
                .SumAsync(x => (long?)x.BinCount, cancellationToken) ?? 0,
            (await db.ReceiptCustodyAcknowledgments.SumAsync(x => (long?)x.Quantity, cancellationToken) ?? 0)
                - (await db.ReceiptCustodyReversals.Where(x => x.PlacementId == null).SumAsync(x => (long?)x.Quantity, cancellationToken) ?? 0)
                + (await db.ReceiptCustodyReversals.Where(x => x.PlacementId != null).SumAsync(x => (long?)x.Quantity, cancellationToken) ?? 0)
                - (await db.ReceiptCustodyPlacements.SumAsync(x => (long?)x.Quantity, cancellationToken) ?? 0))
        { InvalidLocationCorrections = invalidLocations, InvalidReceiptCompensations = invalidCompensations };
    }

    private static bool IsLocationMovement(string type) => type is "CorrectOriginalRoomOut" or "CorrectOriginalRoomIn";

    private async Task<IReadOnlyList<InvalidLocationCorrection>> ValidateLocationCorrectionsAsync(
        IReadOnlyList<RoomInventoryAdjustment> rows, CancellationToken ct)
    {
        // Validate complete operations before opening-baseline filtering. Never let
        // supersession hide a missing side, or unrelated balanced rows cancel it.
        var audits = await db.AuditLogs.AsNoTracking().Where(x => x.Action == "CanonicalReceiptLocationCorrected").ToListAsync(ct);
        var ids = rows.Where(x => IsLocationMovement(x.AdjustmentType) && x.ReceiptInventoryOverrideId != null)
            .Select(x => x.ReceiptInventoryOverrideId!.Value).ToHashSet();
        var locationParents = await db.ReceiptInventoryOverrides.AsNoTracking()
            .Where(x => x.ActionType == ReceiptInventoryOverrideActionTypes.LocationCorrection && x.ExpectedAdjustmentCount == 2)
            .ToListAsync(ct);
        // Legacy ReceiptAdminOverride pairs retain their existing deduction validator.
        // A canonical parent must not disappear from diagnostics when both legs or
        // its audit are missing.
        foreach (var parent in locationParents)
            if (!rows.Any(x => x.ReceiptInventoryOverrideId == parent.Id && x.AdjustmentType == "ReceiptAdminOverride")) ids.Add(parent.Id);
        foreach (var audit in audits)
            if (Guid.TryParse(audit.EntityKey, out var id)) ids.Add(id);
        var parents = await db.ReceiptInventoryOverrides.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var rooms = await db.Rooms.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.WarehouseId, ct);
        var invalid = rows.Where(x => IsLocationMovement(x.AdjustmentType) && x.ReceiptInventoryOverrideId == null)
            .Select(x => new InvalidLocationCorrection(null, new[] { x.Id })).ToList();
        foreach (var id in ids.Order())
        {
            var pair = rows.Where(x => x.ReceiptInventoryOverrideId == id).OrderBy(x => x.Id).ToArray();
            var evidence = audits.Where(x => x.EntityKey == id.ToString()).ToArray();
            if (!parents.TryGetValue(id, out var parent) || !ValidLocationPair(parent, pair, evidence, rooms))
                invalid.Add(new(id, pair.Select(x => x.Id).ToArray()));
        }
        return invalid;
    }

    private static bool ValidLocationPair(ReceiptInventoryOverride parent, RoomInventoryAdjustment[] rows,
        AuditLog[] audits, IReadOnlyDictionary<int, int> rooms)
    {
        if (!parent.IsComplete || parent.ActionType != ReceiptInventoryOverrideActionTypes.LocationCorrection
            || parent.ExpectedAdjustmentCount != 2 || rows.Length != 2 || parent.InventoryDelta != 0
            || parent.OldReceiptBinCount != parent.NewReceiptBinCount || parent.CurrentInventoryBefore != parent.CurrentInventoryAfter
            || string.IsNullOrWhiteSpace(parent.OperationKey) || string.IsNullOrWhiteSpace(parent.Reason)
            || audits.Length != 1) return false;
        if (rows.Count(x => x.AdjustmentType == "CorrectOriginalRoomOut") != 1
            || rows.Count(x => x.AdjustmentType == "CorrectOriginalRoomIn") != 1) return false;
        var outgoing = rows.Single(x => x.AdjustmentType == "CorrectOriginalRoomOut");
        var incoming = rows.Single(x => x.AdjustmentType == "CorrectOriginalRoomIn");
        if (outgoing == null || incoming == null || outgoing.ChangeAmount >= 0
            || (long)outgoing.ChangeAmount + incoming.ChangeAmount != 0 || outgoing.RoomId == incoming.RoomId) return false;
        if (rows.Any(x => x.ReceiptId != parent.ReceiptId || x.CreatedByUserId != parent.AdministratorUserId
            || x.InventoryInvariantVersion != 3 || x.CreatedAt != parent.CreatedAt || x.Reason != parent.Reason
            || x.OldBinCount == null || (long)x.NewBinCount != (long)x.OldBinCount + x.ChangeAmount
            || !rooms.TryGetValue(x.RoomId, out var warehouse) || warehouse != x.WarehouseId)
            || outgoing.AdjustmentAt != incoming.AdjustmentAt
            || outgoing.InventoryOperationKey != parent.OperationKey + ":location:out"
            || incoming.InventoryOperationKey != parent.OperationKey + ":location:in"
            || outgoing.CropYear != incoming.CropYear || outgoing.GrowerLotId != incoming.GrowerLotId
            || outgoing.FruitProfileId != incoming.FruitProfileId || outgoing.LotNumber != incoming.LotNumber
            || outgoing.VarietyCode != incoming.VarietyCode || outgoing.InventoryStatus != incoming.InventoryStatus) return false;
        try
        {
            using var before = JsonDocument.Parse(parent.BeforeReceiptSnapshotJson);
            using var after = JsonDocument.Parse(parent.AfterReceiptSnapshotJson);
            using var affected = JsonDocument.Parse(parent.AffectedInventorySnapshotJson);
            var b = before.RootElement; var a = after.RootElement; var s = affected.RootElement;
            var audit = audits[0];
            using var auditAfter = JsonDocument.Parse(audit.AfterValuesJson ?? "null");
            var history = auditAfter.RootElement;
            return b.GetProperty("id").GetInt64() == parent.ReceiptId && a.GetProperty("id").GetInt64() == parent.ReceiptId
                && b.GetProperty("binCount").GetInt32() == parent.OldReceiptBinCount
                && a.GetProperty("binCount").GetInt32() == parent.NewReceiptBinCount
                && s.GetProperty("sourceRoomId").GetInt32() == outgoing.RoomId
                && s.GetProperty("sourceWarehouseId").GetInt32() == outgoing.WarehouseId
                && s.GetProperty("relocatedBins").GetInt32() == incoming.ChangeAmount
                && s.GetProperty("destination").GetProperty("roomId").GetInt32() == incoming.RoomId
                && s.GetProperty("destination").GetProperty("warehouseId").GetInt32() == incoming.WarehouseId
                && a.GetProperty("roomId").GetInt32() == incoming.RoomId && a.GetProperty("warehouseId").GetInt32() == incoming.WarehouseId
                && audit.UserId == parent.AdministratorUserId && audit.CreatedAt == parent.CreatedAt
                && audit.EntityName == "CanonicalInventory" && audit.SourceApplication == "CanonicalInventory/v1"
                && JsonSerializer.Deserialize<string>(audit.BeforeValuesJson ?? "null") == parent.BeforeReceiptSnapshotJson
                && history.GetProperty("afterReceiptSnapshotJson").GetString() == parent.AfterReceiptSnapshotJson
                && history.GetProperty("sourceRoomId").GetInt32() == outgoing.RoomId
                && history.GetProperty("sourceWarehouseId").GetInt32() == outgoing.WarehouseId
                && history.GetProperty("destination").GetProperty("roomId").GetInt32() == incoming.RoomId
                && history.GetProperty("destination").GetProperty("warehouseId").GetInt32() == incoming.WarehouseId
                && history.GetProperty("relocatedBins").GetInt32() == incoming.ChangeAmount;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return false; }
    }

    private static long EffectiveBins(RoomInventoryAdjustment x) =>
        x.ReceiptId == null && x.AdjustmentType == "StartingInventoryImport" ? x.NewBinCount : x.ChangeAmount;

    // Same opening-baseline boundary as RoomInventoryLedgerQueryService; retain
    // raw category rows independently so missing inventory identities surface as
    // a difference against the authoritative grouped query.
    public static IReadOnlyList<RoomInventoryAdjustment> EffectiveAdjustments(IReadOnlyList<RoomInventoryAdjustment> rows)
    {
        var baselines = rows.Where(x => x.ReceiptId == null && x.AdjustmentType == "StartingInventoryImport")
            .ToLookup(x => x.RoomId);
        return rows.Where(x => !baselines[x.RoomId].Any(b =>
                x.ReceiptId != null ? b.AdjustmentAt >= x.Receipt!.ReceivedAt : b.AdjustmentAt > x.AdjustmentAt))
            .Where(x => x.ReceiptId != null || x.AdjustmentType != "StartingInventoryImport"
                || !baselines[x.RoomId].Any(b => b.AdjustmentAt == x.AdjustmentAt && b.CropYear == x.CropYear
                    && b.GrowerLotId == x.GrowerLotId && b.FruitProfileId == x.FruitProfileId
                    && b.LotNumber == x.LotNumber && b.VarietyCode == x.VarietyCode && b.CreatedAt > x.CreatedAt)).ToList();
    }

    private static string Category(RoomInventoryAdjustment x) =>
        x.InventoryIdentityCorrectionId != null
        || x.ReceiptInventoryOverride?.ActionType is ReceiptInventoryOverrideActionTypes.InventoryReclassification
            or ReceiptInventoryOverrideActionTypes.LocationCorrection ? "Identity/location corrections" :
        x.AdjustmentType switch
        {
            "ReceiptAdd" or "ReceiptEdit" or "ReceiptAdminOverride" => "Receiving / quantity corrections / voids",
            "StartingInventoryImport" => "Opening inventory",
            "BinsRun" or "BinsRunReversal" => "Runs (net of reversals)",
            "Depletion" or "DepletionReversal" or "DroppedBins" or "DroppedBinsReversal" => "Depletions / losses (net)",
            "ProcessorShipment" or "ProcessorShipmentReversal" => "Processor exits (net)",
            "OutsideWarehouseTransfer" or "OutsideWarehouseTransferReversal" => "Outside warehouse custody (net room removal)",
            "InterCrewTransferDispatch" or "InterCrewTransferReceive" or "InterCrewTransferReversalDestination"
                or "InterCrewTransferReversalSource" or TruckReceiptReconciliationService.ReturnToSource
                or TruckReceiptReconciliationService.ReopenDestination or "ReceiptPlacementReversal" => "Inter-crew custody (net room movement)",
            "TransferIn" or "TransferOut" => "Internal transfers",
            "InventoryIdentityCorrection" => "Identity/location corrections",
            _ => "UNCLASSIFIED: " + x.AdjustmentType
        };
}
