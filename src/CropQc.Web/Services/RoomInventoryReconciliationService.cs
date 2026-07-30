using CropQc.Data;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Web.Services;

public interface IRoomInventoryReconciliationService
{
    Task<RoomInventoryReconciliationPageViewModel> GetPageAsync(
        RoomInventoryReconciliationFilter filter,
        CancellationToken cancellationToken);
}

public sealed class RoomInventoryReconciliationService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledgerQuery,
    IInventoryDeductionInvariantService? inventoryDeductionInvariantService = null) : IRoomInventoryReconciliationService
{
    private const int MaximumReceiptRows = 5000;
    private IInventoryDeductionInvariantService InventoryInvariant { get; } =
        inventoryDeductionInvariantService
        ?? new InventoryDeductionInvariantService(dbContext, NullLogger<InventoryDeductionInvariantService>.Instance);

    public async Task<RoomInventoryReconciliationPageViewModel> GetPageAsync(
        RoomInventoryReconciliationFilter filter,
        CancellationToken cancellationToken)
    {
        var roomIds = filter.RoomId is null ? null : new[] { filter.RoomId.Value };
        var snapshots = await ledgerQuery.GetSnapshotsAsync(filter.WarehouseId, roomIds, cancellationToken);

        var receiptQuery = dbContext.Receipts.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsTestData)
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId);
        var receipts = await receiptQuery
            .OrderBy(x => x.Id)
            .Take(MaximumReceiptRows + 1)
            .Select(x => new ReceiptLedgerEvidence(
                x.Id,
                x.WarehouseId,
                x.Warehouse.Code,
                x.RoomId,
                x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                x.CropYear,
                x.FruitProfileId,
                x.GrowerName,
                !string.IsNullOrWhiteSpace(x.GrowerNumber) ? x.GrowerNumber! : x.LotCode,
                x.FruitProfile.VarietyCode,
                x.FruitProfile.ProductionType,
                x.BinCount,
                x.RoomInventoryAdjustments.Any()))
            .ToListAsync(cancellationToken);
        if (receipts.Count > MaximumReceiptRows)
        {
            throw new InvalidOperationException($"Inventory reconciliation exceeds the safe limit of {MaximumReceiptRows} receipt rows. Filter by facility or room.");
        }

        var receiptEvidence = receipts
            .GroupBy(x => Key(x.RoomId, x.CropYear, x.Lot, x.Variety, x.FruitProfileId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => new
                {
                    InboundBins = x.Sum(y => y.BinCount),
                    UnledgeredBins = x.Where(y => !y.HasLedgerRow).Sum(y => y.BinCount),
                    First = x.First()
                },
                StringComparer.OrdinalIgnoreCase);
        var rows = snapshots.Select(snapshot =>
        {
            var key = Key(snapshot.RoomId, snapshot.CropYear, snapshot.Lot, snapshot.Variety, snapshot.FruitProfileId);
            var evidence = receiptEvidence.GetValueOrDefault(key);
            receiptEvidence.Remove(key);
            var warnings = Warnings(snapshot, evidence?.UnledgeredBins ?? 0);
            return new RoomInventoryReconciliationRowViewModel
            {
                WarehouseId = snapshot.WarehouseId,
                Facility = snapshot.Facility,
                RoomId = snapshot.RoomId,
                Room = snapshot.Room,
                CropYear = snapshot.CropYear,
                Grower = snapshot.Grower,
                Lot = snapshot.Lot,
                StoredVariety = snapshot.StoredVarietyCode,
                CanonicalVariety = snapshot.Variety,
                ProductionType = snapshot.ProductionType,
                InboundReceiptBins = evidence?.InboundBins ?? 0,
                UnledgeredInboundBins = evidence?.UnledgeredBins ?? 0,
                PositiveLedgerBins = snapshot.PositiveBins,
                NegativeLedgerBins = snapshot.NegativeBins,
                LegacyBinsRunDepletionBins = snapshot.LegacyBinsRunDepletionBins,
                ActualRunDepletionBins = snapshot.ActualRunDepletionBins,
                ActualRunReversalBins = snapshot.ActualRunReversalBins,
                TransferInBins = snapshot.TransferInBins,
                TransferOutBins = snapshot.TransferOutBins,
                TrueUpBins = snapshot.TrueUpBins,
                OtherAdjustmentBins = snapshot.OtherAdjustmentBins,
                LedgerBalance = snapshot.CurrentBins,
                TransactionCount = snapshot.TransactionCount,
                FirstTransactionAt = snapshot.FirstTransactionAt,
                LastTransactionAt = snapshot.LastTransactionAt,
                Warnings = warnings
            };
        }).ToList();

        foreach (var missing in receiptEvidence.Values)
        {
            rows.Add(new RoomInventoryReconciliationRowViewModel
            {
                WarehouseId = missing.First.WarehouseId,
                Facility = missing.First.Facility,
                RoomId = missing.First.RoomId,
                Room = missing.First.Room,
                CropYear = missing.First.CropYear,
                Grower = missing.First.Grower,
                Lot = missing.First.Lot,
                CanonicalVariety = missing.First.Variety,
                ProductionType = missing.First.ProductionType,
                InboundReceiptBins = missing.InboundBins,
                UnledgeredInboundBins = missing.UnledgeredBins,
                Warnings = ["Receipt bins have no room-ledger origin. Review physical room placement before proposing an opening balance."]
            });
        }

        if (!string.IsNullOrWhiteSpace(filter.Lot))
        {
            rows = rows.Where(x => x.Lot.Contains(filter.Lot.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(filter.Variety))
        {
            rows = rows.Where(x =>
                    x.CanonicalVariety.Contains(filter.Variety.Trim(), StringComparison.OrdinalIgnoreCase)
                    || x.StoredVariety.Contains(filter.Variety.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (filter.WarningsOnly)
        {
            rows = rows.Where(x => x.Warnings.Count > 0).ToList();
        }

        var readiness = await InventoryInvariant.VerifyReadinessAsync(cancellationToken);
        var negativeAdjustments = await GetNegativeAdjustmentsAsync(filter, readiness, cancellationToken);
        var globalWarnings = await GetGlobalWarningsAsync(filter, readiness, cancellationToken);
        return new RoomInventoryReconciliationPageViewModel
        {
            Filter = filter,
            Warehouses = await dbContext.Warehouses.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new RoomInventoryReconciliationOption(x.Id, x.Code))
                .ToListAsync(cancellationToken),
            Rooms = await dbContext.Rooms.AsNoTracking()
                .Where(x => x.IsActive && (filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId))
                .OrderBy(x => x.Warehouse.Code)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Code)
                .Select(x => new RoomInventoryReconciliationOption(
                    x.Id,
                    $"{x.Warehouse.Code} / {x.CropQcRoomName ?? x.DisplayName ?? x.Code}"))
                .ToListAsync(cancellationToken),
            Rows = rows
                .OrderBy(x => x.Facility)
                .ThenBy(x => x.Room)
                .ThenBy(x => x.Lot)
                .ThenBy(x => x.ProductionType)
                .ThenBy(x => x.CanonicalVariety)
                .ToList(),
            NegativeAdjustments = negativeAdjustments,
            GlobalWarnings = globalWarnings
        };
    }

    private async Task<IReadOnlyList<string>> GetGlobalWarningsAsync(
        RoomInventoryReconciliationFilter filter,
        InventoryDeductionReadinessResult readiness,
        CancellationToken cancellationToken)
    {
        var adjustments = dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId);
        var orphanedRunAdjustments = await adjustments.CountAsync(
            x => x.ActualRunId != null
                && !dbContext.BinsRunEntries.Any(entry => entry.InventoryAdjustmentId == x.Id),
            cancellationToken);
        var unparentedRunAdjustments = await adjustments.CountAsync(
            x => x.ActualRunId == null
                && x.Source != null
                && EF.Functions.Like(x.Source, "Actual Run%"),
            cancellationToken);
        var mismatchedRunEntries = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId)
            .CountAsync(x => x.ActualRunId != null
                && ((x.TransactionType == CropQc.Data.Entities.ActualRunTransactionTypes.Depletion
                        && x.InventoryAdjustment.ChangeAmount != -x.BinsRun)
                    || (x.TransactionType == CropQc.Data.Entities.ActualRunTransactionTypes.Reversal
                        && x.InventoryAdjustment.ChangeAmount != x.BinsRun)),
                cancellationToken);
        var duplicateOperationKeys = await dbContext.ActualRunRevisions.AsNoTracking()
            .GroupBy(x => x.OperationKey)
            .CountAsync(x => x.Count() > 1, cancellationToken);

        var warnings = new List<string>();
        if (orphanedRunAdjustments > 0) warnings.Add($"{orphanedRunAdjustments} Actual Run adjustment(s) have no linked Bins Run entry.");
        if (unparentedRunAdjustments > 0) warnings.Add($"{unparentedRunAdjustments} adjustment(s) claim an Actual Run source without an Actual Run parent.");
        if (mismatchedRunEntries > 0) warnings.Add($"{mismatchedRunEntries} Actual Run Bins Run entry/adjustment amount pair(s) do not match.");
        if (duplicateOperationKeys > 0) warnings.Add($"{duplicateOperationKeys} duplicate Actual Run operation key(s) require review.");
        var blocking = readiness.Issues.Count(x => x.BlocksDeployment);
        var historical = readiness.Issues.Count(
            x => x.InvariantVersion < InventoryDeductionInvariantService.CurrentVersion);
        if (blocking > 0) warnings.Add($"{blocking} deduction invariant failure(s) block deployment readiness.");
        if (historical > 0) warnings.Add($"{historical} historical deduction failure(s) remain read-only and require operational review.");
        return warnings;
    }

    private async Task<IReadOnlyList<RoomInventoryNegativeAdjustmentViewModel>> GetNegativeAdjustmentsAsync(
        RoomInventoryReconciliationFilter filter,
        InventoryDeductionReadinessResult readiness,
        CancellationToken cancellationToken)
    {
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.Receipt)
                .ThenInclude(x => x!.FruitProfile)
            .Include(x => x.FruitProfile)
            .Include(x => x.CreatedByUser)
            .Include(x => x.RoomTransfer)
            .Where(x => x.ChangeAmount < 0)
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId)
            .Where(x => string.IsNullOrWhiteSpace(filter.Lot) || x.LotNumber.Contains(filter.Lot))
            .Where(x => string.IsNullOrWhiteSpace(filter.Variety)
                || (x.VarietyCode != null && x.VarietyCode.Contains(filter.Variety))
                || (x.FruitProfile != null && x.FruitProfile.VarietyCode.Contains(filter.Variety)))
            .OrderByDescending(x => x.AdjustmentAt)
            .ThenByDescending(x => x.Id)
            .Take(MaximumReceiptRows)
            .ToListAsync(cancellationToken);
        var ids = adjustments.Select(x => x.Id).ToList();
        var entries = ids.Count == 0
            ? []
            : await dbContext.BinsRunEntries.AsNoTracking()
                .Where(x => ids.Contains(x.InventoryAdjustmentId))
                .ToListAsync(cancellationToken);
        var entryLookup = entries.GroupBy(x => x.InventoryAdjustmentId).ToDictionary(x => x.Key, x => x.ToList());
        var issueLookup = readiness.Issues
            .GroupBy(x => x.AdjustmentId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var baselineRows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType)
            .Select(x => new
            {
                x.RoomId,
                x.CropYear,
                x.LotNumber,
                x.FruitProfileId,
                x.InventoryStatus,
                x.AdjustmentAt
            })
            .ToListAsync(cancellationToken);
        var baselineCutoffs = baselineRows
            .GroupBy(x => BaselineKey(
                x.RoomId,
                x.CropYear,
                x.LotNumber,
                x.FruitProfileId,
                x.InventoryStatus))
            .ToDictionary(x => x.Key, x => x.Max(y => y.AdjustmentAt));

        return adjustments.Select(x =>
        {
            var parents = entryLookup.GetValueOrDefault(x.Id) ?? [];
            var warnings = (issueLookup.GetValueOrDefault(x.Id) ?? [])
                .Select(y => $"{y.Code}: {y.Message}")
                .ToList();
            var parentType = parents.Count > 0 && x.RoomTransfer is not null
                ? "Multiple"
                : parents.Count > 0
                    ? "Bins Run"
                    : x.RoomTransfer is not null
                        ? "Transfer"
                        : x.RoomTransferId is not null
                            ? "Missing Transfer"
                            : "None";
            var profile = x.FruitProfile ?? x.Receipt?.FruitProfile;
            var baselineKey = BaselineKey(
                x.RoomId,
                x.CropYear ?? x.Receipt?.CropYear,
                x.LotNumber,
                x.FruitProfileId ?? x.Receipt?.FruitProfileId,
                x.InventoryStatus ?? profile?.ProductionType);
            var currentlyAffects = !baselineCutoffs.TryGetValue(baselineKey, out var cutoff)
                || x.AdjustmentAt >= cutoff;
            return new RoomInventoryNegativeAdjustmentViewModel
            {
                AdjustmentId = x.Id,
                Facility = x.Warehouse.Code,
                Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                CropYear = x.CropYear ?? x.Receipt?.CropYear,
                Grower = x.GrowerName,
                Lot = x.LotNumber,
                Variety = profile?.VarietyCode ?? x.VarietyCode ?? "",
                ProductionType = profile?.ProductionType ?? "",
                Quantity = -x.ChangeAmount,
                AdjustmentType = x.AdjustmentType,
                ParentType = parentType,
                BinsRunId = parents.Count == 1 ? parents[0].Id : null,
                TransferId = x.RoomTransferId,
                ActualRunId = x.ActualRunId,
                CreatedBy = x.CreatedByUser?.DisplayName ?? "Unknown",
                AdjustmentAt = x.AdjustmentAt,
                ParentMatches = warnings.Count == 0 && parentType is "Bins Run" or "Transfer",
                CurrentlyAffectsInventory = currentlyAffects,
                InvariantVersion = x.InventoryInvariantVersion,
                RecordedSource = x.Source ?? "",
                Warnings = warnings
            };
        }).ToList();
    }

    private static IReadOnlyList<string> Warnings(RoomInventoryLedgerSnapshot snapshot, int unledgeredInboundBins)
    {
        var warnings = new List<string>();
        if (unledgeredInboundBins > 0)
        {
            warnings.Add($"{unledgeredInboundBins} receipt bin(s) have no room-ledger origin.");
        }
        if (snapshot.CurrentBins < 0)
        {
            warnings.Add($"Ledger balance is negative by {-snapshot.CurrentBins} bin(s).");
        }
        if (string.IsNullOrWhiteSpace(snapshot.StoredVarietyCode))
        {
            warnings.Add($"Stored variety is blank; canonical fruit profile resolves it as {snapshot.Variety}.");
        }
        else if (!snapshot.StoredVarietyCode.Equals(snapshot.Variety, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Stored variety {snapshot.StoredVarietyCode} resolves to canonical {snapshot.Variety}.");
        }
        return warnings;
    }

    private static string Key(int roomId, int? cropYear, string lot, string variety, int? fruitProfileId) =>
        $"{roomId}|{cropYear?.ToString() ?? "-"}|{lot.Trim().ToUpperInvariant()}|{variety.Trim().ToUpperInvariant()}|{fruitProfileId?.ToString() ?? "-"}";

    private static string BaselineKey(
        int roomId,
        int? cropYear,
        string lot,
        int? fruitProfileId,
        string? inventoryStatus) =>
        $"{roomId}|{cropYear?.ToString() ?? "-"}|{lot.Trim().ToUpperInvariant()}|{fruitProfileId?.ToString() ?? "-"}|{(inventoryStatus ?? "").Trim().ToUpperInvariant()}";

    private sealed record ReceiptLedgerEvidence(
        long Id,
        int WarehouseId,
        string Facility,
        int RoomId,
        string Room,
        int CropYear,
        int FruitProfileId,
        string Grower,
        string Lot,
        string Variety,
        string ProductionType,
        int BinCount,
        bool HasLedgerRow);
}
