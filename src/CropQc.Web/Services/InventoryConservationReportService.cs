using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

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
    long OutsideWarehouseCustodyBins)
{
    public long TreatmentIdentityDifference => TreatmentIdentityCredit - TreatmentIdentityDebit;
    public bool IsReady => Receiving.MismatchCount == 0 && Global.Difference == 0
        && Facilities.All(x => x.Difference == 0) && UnclassifiedTypes.Count == 0
        && UnbalancedRoomTransfers.Count == 0 && UnbalancedIdentityCorrections.Count == 0
        && InvalidTreatmentIdentityMovements == 0 && TreatmentIdentityDifference == 0;
}

/// <summary>Read-only quantity accounting. Unknown categories are reported and fail the release gate.</summary>
public sealed class InventoryConservationReportService(CropQcDbContext db, IRoomInventoryLedgerQueryService ledger, int cropYear)
{
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
        return new(receiving, facilities, global,
            effective.Where(x => !IsKnownMovement(x.AdjustmentType)).Select(x => x.AdjustmentType).Distinct().Order().ToList(),
            pairs.Where(x => x.RoomTransferId != null).GroupBy(x => x.RoomTransferId!.Value)
                .Where(x => x.Sum(y => (long)y.ChangeAmount) != 0).Select(x => x.Key).ToList(),
            pairs.Where(x => x.InventoryIdentityCorrectionId != null).GroupBy(x => x.InventoryIdentityCorrectionId!.Value)
                .Where(x => x.Sum(y => (long)y.ChangeAmount) != 0).Select(x => x.Key).ToList(),
            treatment.Sum(x => x.SourceSegmentId is null ? 0L : x.BinCount),
            treatment.Sum(x => x.DestinationSegmentId is null ? 0L : x.BinCount),
            treatment.Count(x => x.SourceSegmentId == null || x.DestinationSegmentId == null || x.BinCount <= 0),
            otherTreatment.GroupBy(x => x.MovementType).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.BinCount)),
            await db.InterCrewTransfers.Where(x => x.Status == InterCrewTransferStatuses.InTransit)
                .SumAsync(x => (long?)x.BinsLoaded, cancellationToken) ?? 0,
            await db.OutsideWarehouseTransfers.Where(x => !x.IsReversed)
                .SumAsync(x => (long?)x.BinCount, cancellationToken) ?? 0);
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
                or TruckReceiptReconciliationService.ReopenDestination => "Inter-crew custody (net room movement)",
            "TransferIn" or "TransferOut" => "Internal transfers",
            "InventoryIdentityCorrection" => "Identity/location corrections",
            _ => "UNCLASSIFIED: " + x.AdjustmentType
        };
}
