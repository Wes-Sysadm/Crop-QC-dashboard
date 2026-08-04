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
}

public sealed class RoomInventoryLedgerQueryService(CropQcDbContext dbContext) : IRoomInventoryLedgerQueryService
{
    public const int MaximumRoomLotRows = 2000;

    public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        CancellationToken cancellationToken) =>
        await GetSnapshotsAsync(warehouseId, roomIds, null, cancellationToken);

    public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        int? fruitProfileId,
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

        query = query
            .Where(x => !dbContext.RoomInventoryAdjustments.Any(baseline =>
                baseline.RoomId == x.RoomId
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
            x.GrowerLotId,
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
            VarietyCode = x.FruitProfile != null
                ? x.FruitProfile.VarietyCode
                : x.Receipt != null
                    ? x.Receipt.FruitProfile.VarietyCode
                    : x.VarietyCode ?? "",
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
                CurrentBins = x.Sum(y => y.ChangeAmount),
                PositiveBins = x.Sum(y => y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                NegativeBins = x.Sum(y => y.ChangeAmount < 0 ? y.ChangeAmount : 0),
                ActualRunDepletionBins = x.Sum(y => y.ActualRunId != null && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                ActualRunReversalBins = x.Sum(y => y.ActualRunId != null && y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                LegacyBinsRunDepletionBins = x.Sum(y => y.ActualRunId == null && y.AdjustmentType == BinsRunService.AdjustmentType && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                TransferInBins = x.Sum(y => y.AdjustmentType == "TransferIn" && y.ChangeAmount > 0 ? y.ChangeAmount : 0),
                TransferOutBins = x.Sum(y => y.AdjustmentType == "TransferOut" && y.ChangeAmount < 0 ? -y.ChangeAmount : 0),
                TrueUpBins = x.Sum(y => y.AdjustmentType == "ManualTrueUp" ? y.ChangeAmount : 0),
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
            .Select(x => new GroupedLedgerRow
            {
                WarehouseId = x.First().WarehouseId,
                RoomId = x.First().RoomId,
                CropYear = x.Key.CropYear,
                GrowerLotId = x.First().GrowerLotId,
                LotNumber = x.First().LotNumber,
                VarietyCode = x.First().VarietyCode,
                FruitProfileId = x.First().FruitProfileId,
                CurrentBins = x.Sum(y => y.CurrentBins),
                PositiveBins = x.Sum(y => y.PositiveBins),
                NegativeBins = x.Sum(y => y.NegativeBins),
                ActualRunDepletionBins = x.Sum(y => y.ActualRunDepletionBins),
                ActualRunReversalBins = x.Sum(y => y.ActualRunReversalBins),
                LegacyBinsRunDepletionBins = x.Sum(y => y.LegacyBinsRunDepletionBins),
                TransferInBins = x.Sum(y => y.TransferInBins),
                TransferOutBins = x.Sum(y => y.TransferOutBins),
                TrueUpBins = x.Sum(y => y.TrueUpBins),
                TransactionCount = x.Sum(y => y.TransactionCount),
                FirstTransactionAt = x.Min(y => y.FirstTransactionAt),
                LastTransactionAt = x.Max(y => y.LastTransactionAt),
                LatestAdjustmentId = x.Max(y => y.LatestAdjustmentId)
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
                ReceiptGrowerNumber = x.Receipt == null ? null : x.Receipt.GrowerNumber,
                GrowerLotNumber = x.GrowerLot == null ? null : x.GrowerLot.LotNumber,
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

        return grouped.Select(x =>
        {
            var latest = metadata[x.LatestAdjustmentId];
            var otherAdjustmentBins = x.CurrentBins
                + x.ActualRunDepletionBins
                - x.ActualRunReversalBins
                + x.LegacyBinsRunDepletionBins
                - x.TransferInBins
                + x.TransferOutBins
                - x.TrueUpBins;
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
                ResolveGrowerNumber(latest.ReceiptGrowerNumber, latest.GrowerLotNumber),
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
                latest.SourceReference);
        }).ToList();
    }

    private static string? ResolveGrowerNumber(string? receiptGrowerNumber, string? growerLotNumber)
    {
        var values = new[] { receiptGrowerNumber, growerLotNumber }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 1 ? values[0] : null;
    }

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
        public int CurrentBins { get; init; }
        public int PositiveBins { get; init; }
        public int NegativeBins { get; init; }
        public int ActualRunDepletionBins { get; init; }
        public int ActualRunReversalBins { get; init; }
        public int LegacyBinsRunDepletionBins { get; init; }
        public int TransferInBins { get; init; }
        public int TransferOutBins { get; init; }
        public int TrueUpBins { get; init; }
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
    string SourceReference = "");
