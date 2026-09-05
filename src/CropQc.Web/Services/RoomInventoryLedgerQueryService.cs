using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRoomInventoryLedgerQueryService
{
    Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        int? fruitProfileId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsOfAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        GetSnapshotsAsync(warehouseId, roomIds, cancellationToken);
}

public sealed class RoomInventoryLedgerQueryService(CropQcDbContext dbContext) : IRoomInventoryLedgerQueryService
{
    public const int MaximumRoomLotRows = 2000;

    public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        CancellationToken cancellationToken) =>
        await GetSnapshotsCoreAsync(warehouseId, roomIds, null, null, cancellationToken);

    public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        int? fruitProfileId,
        CancellationToken cancellationToken)
        => await GetSnapshotsCoreAsync(warehouseId, roomIds, fruitProfileId, null, cancellationToken);

    public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsOfAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        await GetSnapshotsCoreAsync(warehouseId, roomIds, null, asOf, cancellationToken);

    private async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsCoreAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        int? fruitProfileId,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var query = dbContext.RoomInventoryAdjustments.AsNoTracking();
        if (warehouseId is not null)
        {
            query = query.Where(x => x.WarehouseId == warehouseId.Value);
        }

        if (roomIds is { Count: > 0 })
        {
            query = query.Where(x => roomIds.Contains(x.RoomId));
        }

        if (asOf is not null)
        {
            query = query.Where(x => x.AdjustmentAt <= asOf.Value);
        }

        query = query
            .Where(x => !dbContext.RoomInventoryAdjustments.Any(baseline =>
                baseline.RoomId == x.RoomId
                && (asOf == null || baseline.AdjustmentAt <= asOf.Value)
                && baseline.ReceiptId == null
                && baseline.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType
                && (x.ReceiptId != null
                    ? baseline.AdjustmentAt >= x.Receipt!.ReceivedAt
                    : baseline.AdjustmentAt > x.AdjustmentAt)))
            .Where(x =>
                x.ReceiptId != null
                || x.AdjustmentType != RoomInventoryImportService.StartingInventoryAdjustmentType
                || !dbContext.RoomInventoryAdjustments.Any(newerBaselineRow =>
                    newerBaselineRow.RoomId == x.RoomId
                    && (asOf == null || newerBaselineRow.AdjustmentAt <= asOf.Value)
                    && newerBaselineRow.ReceiptId == null
                    && newerBaselineRow.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType
                    && newerBaselineRow.AdjustmentAt == x.AdjustmentAt
                    && newerBaselineRow.CropYear == x.CropYear
                    && newerBaselineRow.GrowerLotId == x.GrowerLotId
                    && newerBaselineRow.FruitProfileId == x.FruitProfileId
                    && newerBaselineRow.LotNumber == x.LotNumber
                    && newerBaselineRow.VarietyCode == x.VarietyCode
                    && newerBaselineRow.CreatedAt > x.CreatedAt));

        var normalizedQuery = query.Select(x => new
        {
            x.Id,
            x.WarehouseId,
            x.RoomId,
            GrowerLotId = x.GrowerLotId
                ?? dbContext.BinsRunEntries
                    .Where(entry => entry.InventoryAdjustmentId == x.Id)
                    .Select(entry => entry.GrowerLotId
                        ?? (entry.SourceInventoryAdjustment == null ? null : entry.SourceInventoryAdjustment.GrowerLotId)
                        ?? (entry.SourceInventoryAdjustment == null || entry.SourceInventoryAdjustment.Receipt == null
                            || entry.SourceInventoryAdjustment.Receipt.GrowerLotId == null
                            || (entry.SourceInventoryAdjustment.LotNumber != entry.SourceInventoryAdjustment.Receipt.GrowerNumber
                                && entry.SourceInventoryAdjustment.LotNumber != entry.SourceInventoryAdjustment.Receipt.LotCode)
                            ? null
                            : entry.SourceInventoryAdjustment.Receipt.GrowerLotId))
                    .FirstOrDefault()
                ?? (x.Receipt == null || x.Receipt.GrowerLotId == null
                    || (x.LotNumber != x.Receipt.GrowerNumber && x.LotNumber != x.Receipt.LotCode)
                    ? null
                    : x.Receipt.GrowerLotId),
            CropYear = x.CropYear
                ?? (x.Receipt == null ? null : (int?)x.Receipt.CropYear)
                ?? dbContext.BinsRunEntries
                    .Where(entry => entry.InventoryAdjustmentId == x.Id)
                    .Select(entry => entry.CropYear
                        ?? (entry.Receipt == null ? null : (int?)entry.Receipt.CropYear)
                        ?? (entry.SourceInventoryAdjustment == null ? null : entry.SourceInventoryAdjustment.CropYear)
                        ?? (entry.SourceInventoryAdjustment == null || entry.SourceInventoryAdjustment.Receipt == null
                            ? null
                            : (int?)entry.SourceInventoryAdjustment.Receipt.CropYear))
                    .FirstOrDefault(),
            LotNumber = x.LotNumber != ""
                ? x.LotNumber
                : x.Receipt == null
                    ? ""
                    : x.Receipt.GrowerNumber ?? x.Receipt.LotCode,
            FruitProfileId = x.FruitProfileId ?? (x.Receipt == null ? null : (int?)x.Receipt.FruitProfileId),
            // Historical adjustment identity is immutable. Prefer identity persisted on
            // the adjustment (including its persisted Grower Lot) and only consult the
            // current Receipt when it cannot contradict that evidence.
            GrowerNumber = x.GrowerLot != null && x.LotNumber != "" && x.LotNumber == x.GrowerLot.LotNumber
                    ? x.GrowerLot.LotNumber
                    : x.Receipt != null && x.Receipt.GrowerNumber != null && x.Receipt.GrowerNumber != ""
                        && (x.LotNumber == "" || x.LotNumber == x.Receipt.LotCode)
                    ? x.Receipt.GrowerNumber
                    : x.ReceiptId != null && x.LotNumber != ""
                    ? x.LotNumber
                    : x.RoomTransfer != null && x.RoomTransfer.LotNumber != ""
                    ? x.RoomTransfer.LotNumber
                    : dbContext.BinsRunEntries
                    .Where(entry => entry.InventoryAdjustmentId == x.Id)
                    .Select(entry => entry.GrowerNumberSnapshot
                        ?? (entry.SourceInventoryAdjustment == null
                            ? null
                            : entry.SourceInventoryAdjustment.GrowerLot != null
                                ? entry.SourceInventoryAdjustment.ReceiptId != null
                                    ? entry.SourceInventoryAdjustment.LotNumber
                                    : null
                                : entry.SourceInventoryAdjustment.LotNumber))
                    .FirstOrDefault()
                    ?? (x.Receipt == null ? null : x.Receipt.GrowerNumber ?? x.Receipt.LotCode)
                    ?? (dbContext.Receipts
                            .Where(receipt => receipt.WarehouseId == x.WarehouseId
                                && receipt.RoomId == x.RoomId
                                && receipt.CropYear == x.CropYear
                                && (x.FruitProfileId == null || receipt.FruitProfileId == x.FruitProfileId)
                                && (receipt.GrowerNumber == x.LotNumber || receipt.LotCode == x.LotNumber)
                                && receipt.GrowerNumber != null
                                && receipt.GrowerNumber != "")
                            .Select(receipt => receipt.GrowerNumber)
                            .Distinct()
                            .Count() == 1
                        ? dbContext.Receipts
                            .Where(receipt => receipt.WarehouseId == x.WarehouseId
                                && receipt.RoomId == x.RoomId
                                && receipt.CropYear == x.CropYear
                                && (x.FruitProfileId == null || receipt.FruitProfileId == x.FruitProfileId)
                                && (receipt.GrowerNumber == x.LotNumber || receipt.LotCode == x.LotNumber)
                                && receipt.GrowerNumber != null
                                && receipt.GrowerNumber != "")
                            .Select(receipt => receipt.GrowerNumber)
                            .FirstOrDefault()
                        : null),
            VarietyCode = x.VarietyCode != null && x.VarietyCode != ""
                ? x.VarietyCode
                : x.FruitProfile != null
                    ? x.FruitProfile.VarietyCode
                    : x.Receipt != null
                    ? x.Receipt.FruitProfile.VarietyCode
                    : "",
            ChangeAmount = x.ReceiptId == null
                && x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType
                    ? x.NewBinCount
                    : x.ChangeAmount,
            x.AdjustmentType,
            x.ActualRunId,
            x.AdjustmentAt
        });
        if (fruitProfileId is not null)
        {
            normalizedQuery = normalizedQuery.Where(x => x.FruitProfileId == fruitProfileId.Value);
        }

        var persistedGroups = await normalizedQuery
            .Where(x => x.LotNumber != "" && x.VarietyCode != "")
            .GroupBy(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.LotNumber, x.VarietyCode, x.FruitProfileId })
            .Select(x => new GroupedLedgerRow
            {
                WarehouseId = x.Key.WarehouseId,
                RoomId = x.Key.RoomId,
                CropYear = x.Key.CropYear,
                GrowerLotId = x.Key.GrowerLotId,
                LotNumber = x.Key.LotNumber,
                VarietyCode = x.Key.VarietyCode!,
                FruitProfileId = x.Key.FruitProfileId,
                GrowerNumberCount = x.Where(y => y.GrowerNumber != null && y.GrowerNumber != "")
                    .Select(y => y.GrowerNumber)
                    .Distinct()
                    .Count(),
                GrowerNumber = x.Where(y => y.GrowerNumber != null && y.GrowerNumber != "")
                    .Select(y => y.GrowerNumber)
                    .FirstOrDefault(),
                CurrentBins = x.Sum(y => y.ChangeAmount),
                PositiveBins = x.Sum(y => y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                NegativeBins = x.Sum(y => y.ChangeAmount < 0 ? y.ChangeAmount : 0),
                ActualRunDepletionBins = x.Sum(y => y.ActualRunId != null && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                ActualRunReversalBins = x.Sum(y => y.ActualRunId != null && y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                LegacyBinsRunDepletionBins = x.Sum(y => y.ActualRunId == null && y.AdjustmentType == BinsRunService.AdjustmentType && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                TransferInBins = x.Sum(y => y.AdjustmentType == "TransferIn" && y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                TransferOutBins = x.Sum(y => y.AdjustmentType == "TransferOut" && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                TrueUpBins = x.Sum(y => y.AdjustmentType == "ManualTrueUp" ? y.ChangeAmount : 0),
                DroppedBins = x.Sum(y => y.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBins && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                DroppedBinsRestored = x.Sum(y => y.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBinsReversal && y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                TransactionCount = x.Count(),
                FirstTransactionAt = x.Min(y => y.AdjustmentAt),
                LastTransactionAt = x.Max(y => y.AdjustmentAt),
                LatestAdjustmentId = x.Max(y => y.Id)
            })
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.RoomId)
            .ThenBy(x => x.LotNumber)
            .ThenBy(x => x.VarietyCode)
            .Take(MaximumRoomLotRows + 1)
            .ToListAsync(cancellationToken);
        if (persistedGroups.Count > MaximumRoomLotRows)
        {
            throw new InvalidOperationException($"Room inventory selection exceeds the safe limit of {MaximumRoomLotRows} room-lot rows. Filter by facility or room.");
        }

        var canonicalYears = persistedGroups
            .GroupBy(CanonicalIdentity)
            .ToDictionary(
                x => x.Key,
                x => x.Where(y => y.CropYear is not null).Select(y => y.CropYear!.Value).Distinct().ToList());
        var grouped = persistedGroups
            .GroupBy(x => new
            {
                Identity = CanonicalIdentity(x),
                CropYear = x.CropYear ?? (canonicalYears[CanonicalIdentity(x)].Count == 1
                    ? canonicalYears[CanonicalIdentity(x)][0]
                    : (int?)null)
            })
            .Select(x =>
            {
                var growerNumbers = x.Where(y => y.GrowerNumberCount == 1 && !string.IsNullOrWhiteSpace(y.GrowerNumber))
                    .Select(y => y.GrowerNumber!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var hasAmbiguousSource = x.Any(y => y.GrowerNumberCount > 1);
                return new GroupedLedgerRow
                {
                    WarehouseId = x.First().WarehouseId,
                    RoomId = x.First().RoomId,
                    CropYear = x.Key.CropYear,
                    GrowerLotId = x.First().GrowerLotId,
                    LotNumber = x.First().LotNumber,
                    VarietyCode = x.First().VarietyCode,
                    FruitProfileId = x.First().FruitProfileId,
                    GrowerNumberCount = hasAmbiguousSource ? 2 : growerNumbers.Count,
                    GrowerNumber = !hasAmbiguousSource && growerNumbers.Count == 1 ? growerNumbers[0] : null,
                    CurrentBins = x.Sum(y => y.CurrentBins),
                    PositiveBins = x.Sum(y => y.PositiveBins),
                    NegativeBins = x.Sum(y => y.NegativeBins),
                    ActualRunDepletionBins = x.Sum(y => y.ActualRunDepletionBins),
                    ActualRunReversalBins = x.Sum(y => y.ActualRunReversalBins),
                    LegacyBinsRunDepletionBins = x.Sum(y => y.LegacyBinsRunDepletionBins),
                    TransferInBins = x.Sum(y => y.TransferInBins),
                    TransferOutBins = x.Sum(y => y.TransferOutBins),
                    TrueUpBins = x.Sum(y => y.TrueUpBins),
                    DroppedBins = x.Sum(y => y.DroppedBins),
                    DroppedBinsRestored = x.Sum(y => y.DroppedBinsRestored),
                    TransactionCount = x.Sum(y => y.TransactionCount),
                    FirstTransactionAt = x.Min(y => y.FirstTransactionAt),
                    LastTransactionAt = x.Max(y => y.LastTransactionAt),
                    LatestAdjustmentId = x.Max(y => y.LatestAdjustmentId)
                };
            })
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.RoomId)
            .ThenBy(x => x.LotNumber)
            .ThenBy(x => x.VarietyCode)
            .ToList();

        var latestIds = grouped.Select(x => x.LatestAdjustmentId).ToList();
        var metadata = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => latestIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                Facility = x.Warehouse.Code,
                Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                LocationGroup = x.SourceSubLocation ?? x.Room.SubLocation ?? "",
                x.GrowerLotId,
                FruitProfileId = x.FruitProfileId ?? (x.Receipt == null ? null : (int?)x.Receipt.FruitProfileId),
                x.GrowerName,
                x.PoolStart,
                StoredVarietyCode = x.VarietyCode ?? "",
                VarietyName = x.FruitProfile != null
                    ? x.FruitProfile.Name
                    : x.Receipt != null
                        ? x.Receipt.FruitProfile.Name
                        : x.VarietyCode ?? "",
                FruitType = x.FruitProfile != null
                    ? x.FruitProfile.FruitType
                    : x.Receipt == null
                        ? ""
                        : x.Receipt.FruitProfile.FruitType,
                ProductionType = x.FruitProfile != null
                    ? x.FruitProfile.ProductionType
                    : x.Receipt == null
                        ? ""
                        : x.Receipt.FruitProfile.ProductionType,
                IsOrganic = x.FruitProfile != null
                    ? (bool?)x.FruitProfile.IsOrganic
                    : x.Receipt == null
                        ? null
                        : x.Receipt.FruitProfile.IsOrganic,
                SourceReference = x.Receipt != null
                    ? "Receipt " + x.Receipt.CompuTechReceiptId
                    : x.Source ?? x.Reason ?? x.AdjustmentType,
                x.InventoryStatus
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var snapshots = grouped.Select(x =>
        {
            var latest = metadata[x.LatestAdjustmentId];
            var otherAdjustmentBins = x.CurrentBins
                + x.ActualRunDepletionBins
                - x.ActualRunReversalBins
                + x.LegacyBinsRunDepletionBins
                - x.TransferInBins
                + x.TransferOutBins
                - x.TrueUpBins
                + x.DroppedBins
                - x.DroppedBinsRestored;
            return new RoomInventoryLedgerSnapshot(
                x.WarehouseId,
                latest.Facility,
                x.RoomId,
                latest.Room,
                latest.LocationGroup,
                x.CropYear,
                x.GrowerLotId ?? latest.GrowerLotId,
                x.FruitProfileId,
                latest.GrowerName,
                x.GrowerNumberCount == 1 ? x.GrowerNumber : null,
                x.LotNumber,
                latest.PoolStart,
                latest.StoredVarietyCode,
                x.VarietyCode,
                latest.VarietyName,
                latest.FruitType,
                latest.ProductionType,
                latest.IsOrganic,
                latest.InventoryStatus ?? "",
                x.PositiveBins,
                x.NegativeBins,
                x.ActualRunDepletionBins,
                x.ActualRunReversalBins,
                x.LegacyBinsRunDepletionBins,
                x.TransferInBins,
                x.TransferOutBins,
                x.TrueUpBins,
                otherAdjustmentBins,
                x.CurrentBins,
                x.TransactionCount,
                x.FirstTransactionAt,
                x.LastTransactionAt,
                x.LatestAdjustmentId,
                latest.SourceReference,
                x.DroppedBins,
                x.DroppedBinsRestored);
        }).ToList();

        // Reconcile only rows that already share the same persisted Grower Lot
        // identity. A legacy null-GrowerLot position must never be folded into a
        // canonical position merely because their display fields happen to match.
        var lineageQuery = dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.SourceInventoryAdjustmentId != null);
        if (warehouseId is not null)
        {
            lineageQuery = lineageQuery.Where(x => x.WarehouseId == warehouseId.Value);
        }
        if (roomIds is { Count: > 0 })
        {
            lineageQuery = lineageQuery.Where(x => roomIds.Contains(x.RoomId));
        }
        if (asOf is not null)
        {
            lineageQuery = lineageQuery.Where(x => x.RunAt <= asOf.Value);
        }
        var lineageEdges = await lineageQuery
            .Select(x => new { Source = x.SourceInventoryAdjustmentId!.Value, Destination = x.InventoryAdjustmentId })
            .ToListAsync(cancellationToken);
        return ReconcileCompatibleSourceSnapshots(snapshots, BuildLineageComponents(
            lineageEdges.Select(x => (x.Source, x.Destination))));
    }

    private static IReadOnlyList<RoomInventoryLedgerSnapshot> ReconcileCompatibleSourceSnapshots(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
        IReadOnlyDictionary<long, long> lineageComponents)
    {
        var result = new List<RoomInventoryLedgerSnapshot>();
        foreach (var group in snapshots.GroupBy(snapshot =>
        {
            // A Bins Run entry is immutable source/destination custody evidence.
            // Prefer it over conflicting legacy display labels so a fully depleted
            // receipt cannot be rendered as a new positive inventory position.
            return lineageComponents.TryGetValue(snapshot.LatestAdjustmentId, out var component)
                ? $"lineage|{component}"
                : $"identity|{CanonicalRoomLotKey(snapshot)}";
        }, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            if (rows.Count == 1 || !CanReconcile(rows, lineageComponents))
            {
                result.AddRange(rows);
                continue;
            }

            var latest = rows
                .OrderByDescending(x => x.LastTransactionAt)
                .ThenByDescending(x => x.LatestAdjustmentId)
                .First();
            var growerLotId = rows
                .Where(x => x.GrowerLotId is not null)
                .Select(x => x.GrowerLotId!.Value)
                .Distinct()
                .SingleOrDefault();
            var cropYear = rows
                .Where(x => x.CropYear is not null)
                .Select(x => x.CropYear!.Value)
                .Distinct()
                .SingleOrDefault();
            var growerNumber = rows
                .Select(x => x.GrowerNumber?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SingleOrDefault();
            var sourceReferences = rows
                .Select(x => x.SourceReference.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Add(latest with
            {
                CropYear = cropYear == 0 ? null : cropYear,
                GrowerLotId = growerLotId == 0 ? null : growerLotId,
                GrowerNumber = growerNumber,
                PositiveBins = rows.Sum(x => x.PositiveBins),
                NegativeBins = rows.Sum(x => x.NegativeBins),
                ActualRunDepletionBins = rows.Sum(x => x.ActualRunDepletionBins),
                ActualRunReversalBins = rows.Sum(x => x.ActualRunReversalBins),
                LegacyBinsRunDepletionBins = rows.Sum(x => x.LegacyBinsRunDepletionBins),
                TransferInBins = rows.Sum(x => x.TransferInBins),
                TransferOutBins = rows.Sum(x => x.TransferOutBins),
                TrueUpBins = rows.Sum(x => x.TrueUpBins),
                OtherAdjustmentBins = rows.Sum(x => x.OtherAdjustmentBins),
                CurrentBins = rows.Sum(x => x.CurrentBins),
                TransactionCount = rows.Sum(x => x.TransactionCount),
                FirstTransactionAt = rows.Min(x => x.FirstTransactionAt),
                LastTransactionAt = rows.Max(x => x.LastTransactionAt),
                LatestAdjustmentId = rows.Max(x => x.LatestAdjustmentId),
                SourceReference = sourceReferences.Count switch
                {
                    0 => "Multiple inventory sources",
                    1 => sourceReferences[0],
                    _ => $"{sourceReferences.Count} inventory sources; latest {latest.SourceReference}"
                },
                DroppedBins = rows.Sum(x => x.DroppedBins),
                DroppedBinsRestored = rows.Sum(x => x.DroppedBinsRestored)
            });
        }

        return result
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.RoomId)
            .ThenBy(x => x.Lot)
            .ThenBy(x => x.Variety)
            .ToList();
    }

    private static bool CanReconcile(
        IReadOnlyList<RoomInventoryLedgerSnapshot> rows,
        IReadOnlyDictionary<long, long> lineageComponents)
    {
        var sameBinsRunLineage = rows
            .Select(x => lineageComponents.GetValueOrDefault(x.LatestAdjustmentId, x.LatestAdjustmentId))
            .Distinct()
            .Count() == 1;
        // A direct Bins Run source/destination chain is stronger evidence than
        // a stale historical display label. Keep its quantity together even
        // when older rows disagree about the canonical Grower Lot/name; this
        // prevents a fully depleted receipt from reappearing as current stock.
        return rows.Select(x => x.WarehouseId).Distinct().Count() == 1
            && rows.Where(x => x.CropYear is not null).Select(x => x.CropYear).Distinct().Count() <= 1
            && (rows.Select(x => x.GrowerLotId).Distinct().Count() == 1 || sameBinsRunLineage)
            && (sameBinsRunLineage || DistinctNonEmpty(rows.Select(x => x.GrowerNumber)) <= 1)
            && DistinctNonEmpty(rows.Select(x => x.InventoryStatus)) <= 1
            && DistinctNonEmpty(rows.Select(x => x.ProductionType)) <= 1
            && rows.Where(x => x.IsOrganic is not null).Select(x => x.IsOrganic).Distinct().Count() <= 1;
    }

    private static IReadOnlyDictionary<long, long> BuildLineageComponents(
        IEnumerable<(long Source, long Destination)> edges)
    {
        var parent = new Dictionary<long, long>();
        long Find(long id)
        {
            if (!parent.TryGetValue(id, out var value))
            {
                parent[id] = id;
                return id;
            }
            if (value == id) return id;
            return parent[id] = Find(value);
        }

        foreach (var (source, destination) in edges)
        {
            var sourceRoot = Find(source);
            var destinationRoot = Find(destination);
            if (sourceRoot != destinationRoot) parent[destinationRoot] = sourceRoot;
        }
        foreach (var id in parent.Keys.ToList()) parent[id] = Find(id);
        return parent;
    }

    private static int DistinctNonEmpty(IEnumerable<string?> values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static string CanonicalRoomLotKey(RoomInventoryLedgerSnapshot snapshot) =>
        string.Join('|',
            snapshot.WarehouseId,
            snapshot.RoomId,
            snapshot.Lot.Trim().ToUpperInvariant(),
            snapshot.Variety.Trim().ToUpperInvariant(),
            snapshot.FruitProfileId?.ToString() ?? "-");

    private static CanonicalLedgerIdentity CanonicalIdentity(GroupedLedgerRow row) =>
        new(
            row.WarehouseId,
            row.RoomId,
            row.GrowerLotId,
            row.LotNumber.Trim().ToUpperInvariant(),
            row.VarietyCode.Trim().ToUpperInvariant(),
            row.FruitProfileId);

    private sealed record CanonicalLedgerIdentity(
        int WarehouseId,
        int RoomId,
        int? GrowerLotId,
        string LotNumber,
        string VarietyCode,
        int? FruitProfileId);

    private sealed class GroupedLedgerRow
    {
        public int WarehouseId { get; init; }
        public int RoomId { get; init; }
        public int? CropYear { get; init; }
        public int? GrowerLotId { get; init; }
        public string LotNumber { get; init; } = "";
        public string VarietyCode { get; init; } = "";
        public int? FruitProfileId { get; init; }
        public int GrowerNumberCount { get; init; }
        public string? GrowerNumber { get; init; }
        public int CurrentBins { get; init; }
        public int PositiveBins { get; init; }
        public int NegativeBins { get; init; }
        public int ActualRunDepletionBins { get; init; }
        public int ActualRunReversalBins { get; init; }
        public int LegacyBinsRunDepletionBins { get; init; }
        public int TransferInBins { get; init; }
        public int TransferOutBins { get; init; }
        public int TrueUpBins { get; init; }
        public int DroppedBins { get; init; }
        public int DroppedBinsRestored { get; init; }
        public int TransactionCount { get; init; }
        public DateTimeOffset FirstTransactionAt { get; init; }
        public DateTimeOffset LastTransactionAt { get; init; }
        public long LatestAdjustmentId { get; init; }
    }
}

public sealed record RoomInventoryLedgerSnapshot(
    int WarehouseId,
    string Facility,
    int RoomId,
    string Room,
    string LocationGroup,
    int? CropYear,
    int? GrowerLotId,
    int? FruitProfileId,
    string Grower,
    string? GrowerNumber,
    string Lot,
    string? PoolStart,
    string StoredVarietyCode,
    string Variety,
    string VarietyName,
    string FruitType,
    string ProductionType,
    bool? IsOrganic,
    string InventoryStatus,
    int PositiveBins,
    int NegativeBins,
    int ActualRunDepletionBins,
    int ActualRunReversalBins,
    int LegacyBinsRunDepletionBins,
    int TransferInBins,
    int TransferOutBins,
    int TrueUpBins,
    int OtherAdjustmentBins,
    int CurrentBins,
    int TransactionCount,
    DateTimeOffset FirstTransactionAt,
    DateTimeOffset LastTransactionAt,
    long LatestAdjustmentId,
    string SourceReference = "",
    int DroppedBins = 0,
    int DroppedBinsRestored = 0);
