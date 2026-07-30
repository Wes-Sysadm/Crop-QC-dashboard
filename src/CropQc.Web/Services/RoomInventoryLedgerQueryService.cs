using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRoomInventoryLedgerQueryService
{
    Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        CancellationToken cancellationToken);
}

public sealed class RoomInventoryLedgerQueryService(CropQcDbContext dbContext) : IRoomInventoryLedgerQueryService
{
    public const int MaximumRoomLotRows = 2000;

    public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
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
                    && newerBaselineRow.LotNumber == x.LotNumber
                    && newerBaselineRow.VarietyCode == x.VarietyCode
                    && newerBaselineRow.CreatedAt > x.CreatedAt));

        var normalizedQuery = query.Select(x => new
        {
            x.Id,
            x.WarehouseId,
            x.RoomId,
            CropYear = x.CropYear ?? (x.Receipt == null ? null : (int?)x.Receipt.CropYear),
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
        var grouped = await normalizedQuery
            .Where(x => x.LotNumber != "" && x.VarietyCode != "")
            .GroupBy(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.LotNumber, x.VarietyCode, x.FruitProfileId })
            .Select(x => new
            {
                x.Key.WarehouseId,
                x.Key.RoomId,
                x.Key.CropYear,
                x.Key.LotNumber,
                VarietyCode = x.Key.VarietyCode!,
                x.Key.FruitProfileId,
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
        if (grouped.Count > MaximumRoomLotRows)
        {
            throw new InvalidOperationException($"Room inventory selection exceeds the safe limit of {MaximumRoomLotRows} room-lot rows. Filter by facility or room.");
        }

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
                latest.GrowerLotId,
                x.FruitProfileId,
                latest.GrowerName,
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
                x.LatestAdjustmentId);
        }).ToList();
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
    long LatestAdjustmentId);
