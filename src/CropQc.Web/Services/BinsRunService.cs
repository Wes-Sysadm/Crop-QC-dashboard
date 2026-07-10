using System.Data;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IBinsRunService
{
    Task<BinsRunPageViewModel> GetPageAsync(BinsRunFilterForm filter, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> CreateAsync(BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateAsync(long id, BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(ReverseBinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class BinsRunService(CropQcDbContext dbContext, IUserAccessService userAccessService) : IBinsRunService
{
    public const string AdjustmentType = "BinsRun";
    public const string ReversalAdjustmentType = "BinsRunReversal";
    public const string SourceApplication = "CropQc.Web";

    public async Task<BinsRunPageViewModel> GetPageAsync(BinsRunFilterForm filter, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var canRecord = await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken);
        var canAdmin = await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Admin, cancellationToken);
        var options = await GetAvailableInventoryAsync(filter.WarehouseId, filter.RoomId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(filter.Grower))
        {
            options = options.Where(x => x.Grower.Contains(filter.Grower, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Variety))
        {
            options = options.Where(x => x.Variety.Contains(filter.Variety, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Lot))
        {
            options = options.Where(x => x.Lot.Contains(filter.Lot, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var historyQuery = dbContext.BinsRunEntries.AsNoTracking()
            .Include(x => x.Room)
            .Include(x => x.Warehouse)
            .Include(x => x.CreatedByUser)
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId);
        if (filter.FromDate is DateTime fromDate)
        {
            historyQuery = historyQuery.Where(x => x.RunAt >= new DateTimeOffset(fromDate.Date));
        }

        if (filter.ToDate is DateTime toDate)
        {
            historyQuery = historyQuery.Where(x => x.RunAt < new DateTimeOffset(toDate.Date.AddDays(1)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Grower))
        {
            historyQuery = historyQuery.Where(x => x.GrowerName.Contains(filter.Grower));
        }

        if (!string.IsNullOrWhiteSpace(filter.Variety))
        {
            historyQuery = historyQuery.Where(x => (x.VarietyCode ?? "").Contains(filter.Variety));
        }

        if (!string.IsNullOrWhiteSpace(filter.Lot))
        {
            historyQuery = historyQuery.Where(x => x.LotNumber.Contains(filter.Lot));
        }

        var rooms = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .Where(x => x.IsActive && (filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId))
            .OrderBy(x => x.Warehouse.Code)
            .ThenBy(x => x.SubLocation)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.CropQcRoomName ?? x.DisplayName ?? x.Code)
            .ToListAsync(cancellationToken);

        return new BinsRunPageViewModel
        {
            Filter = filter,
            Form = new BinsRunForm
            {
                WarehouseId = filter.WarehouseId,
                RoomId = filter.RoomId,
                InventoryKey = options.FirstOrDefault()?.InventoryKey ?? "",
                ExpectedAvailableBins = options.FirstOrDefault()?.CurrentBins ?? 0,
                RunAt = DateTimeOffset.Now
            },
            Warehouses = await dbContext.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(cancellationToken),
            Rooms = rooms,
            AvailableInventory = options,
            History = await historyQuery
                .OrderByDescending(x => x.RunAt)
                .ThenByDescending(x => x.Id)
                .Take(100)
                .Select(x => new BinsRunHistoryItemViewModel
                {
                    Id = x.Id,
                    InventoryKey = x.ReceiptId != null
                        ? "R:" + x.ReceiptId.Value
                        : $"A:{x.InventoryAdjustmentId}",
                    WarehouseId = x.WarehouseId,
                    RoomId = x.RoomId,
                    Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                    Inventory = x.GrowerName + " - " + x.VarietyCode + " - " + x.LotNumber,
                    PreviousAvailableBins = x.PreviousAvailableBins,
                    BinsRun = x.BinsRun,
                    NewAvailableBins = x.NewAvailableBins,
                    RunAt = x.RunAt,
                    CreatedBy = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                    IsReversed = x.IsReversed,
                    ReverseReason = x.ReverseReason,
                    Notes = x.Notes
                })
                .ToListAsync(cancellationToken),
            CanRecord = canRecord,
            CanAdmin = canAdmin,
            SelectedAvailableBins = options.FirstOrDefault()?.CurrentBins
        };
    }

    public async Task<string?> CreateAsync(BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken))
        {
            return "Bins Run Edit access is required to record bins run.";
        }

        return await SaveNewBalanceAsync(null, form, user, "Create", cancellationToken);
    }

    public async Task<string?> UpdateAsync(long id, BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken))
        {
            return "Bins Run Edit access is required to edit bins run.";
        }

        if (id <= 0)
        {
            return "Bins Run entry is required.";
        }

        return await SaveNewBalanceAsync(id, form, user, "Update", cancellationToken);
    }

    public async Task<string?> ReverseAsync(ReverseBinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Admin, cancellationToken))
        {
            return "Bins Run Admin access is required to reverse bins run.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "Reason is required to reverse bins run.";
        }

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var entry = await dbContext.BinsRunEntries
            .Include(x => x.InventoryAdjustment)
            .SingleOrDefaultAsync(x => x.Id == form.Id, cancellationToken);
        if (entry is null)
        {
            return "Bins Run entry was not found.";
        }

        if (entry.IsReversed)
        {
            return "Bins Run entry is already reversed.";
        }

        var snapshot = await GetCurrentInventoryByEntryAsync(entry, cancellationToken);
        if (snapshot is null)
        {
            return "Selected inventory is no longer available in this room.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var previous = snapshot.CurrentBins;
        var restored = previous + entry.BinsRun;
        var adjustment = CreateAdjustment(snapshot, entry.BinsRun, previous, restored, ReversalAdjustmentType, userId, entry.RunAt, $"Reversal of Bins Run #{entry.Id}: {form.Reason.Trim()}");
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync(cancellationToken);

        entry.IsReversed = true;
        entry.ReversedAt = DateTimeOffset.UtcNow;
        entry.ReversedByUserId = userId;
        entry.ReverseReason = form.Reason.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("Reverse", entry, userId, new { previousAvailableBins = previous }, new { restoredAvailableBins = restored, form.Reason }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return null;
    }

    private async Task<string?> SaveNewBalanceAsync(long? entryId, BinsRunForm form, ClaimsPrincipal user, string auditAction, CancellationToken cancellationToken)
    {
        if (form.BinsRun <= 0)
        {
            return "Bins run must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(form.InventoryKey))
        {
            return "Select available inventory.";
        }

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        BinsRunEntry? existing = null;
        if (entryId is long id)
        {
            existing = await dbContext.BinsRunEntries.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (existing is null) return "Bins Run entry was not found.";
            if (existing.IsReversed) return "Reversed Bins Run entries cannot be edited.";
        }

        var snapshot = await GetCurrentInventoryByKeyAsync(form.InventoryKey, cancellationToken);
        if (snapshot is null)
        {
            return "Selected inventory is no longer available in this room.";
        }

        if (form.RoomId is not null && snapshot.RoomId != form.RoomId)
        {
            return "Selected inventory does not belong to the selected room.";
        }

        if (form.WarehouseId is not null && snapshot.WarehouseId != form.WarehouseId)
        {
            return "Selected inventory does not belong to the selected facility.";
        }

        var effectiveAvailable = snapshot.CurrentBins + (existing is null ? 0 : existing.BinsRun);
        if (existing is null && form.ExpectedAvailableBins > 0 && snapshot.CurrentBins != form.ExpectedAvailableBins)
        {
            return $"Available quantity changed before save. {snapshot.CurrentBins} bins are available now.";
        }

        if (form.BinsRun > effectiveAvailable)
        {
            return $"Cannot run {form.BinsRun} bins because only {effectiveAvailable} bins are currently available.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var newAvailable = effectiveAvailable - form.BinsRun;
        var changeAmount = newAvailable - snapshot.CurrentBins;
        var adjustment = CreateAdjustment(snapshot, changeAmount, snapshot.CurrentBins, newAvailable, AdjustmentType, userId, form.RunAt, form.Notes);
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync(cancellationToken);

        object? before = null;
        var entry = existing;
        if (entry is null)
        {
            entry = new BinsRunEntry
            {
                ReceiptId = snapshot.ReceiptId,
                SourceInventoryAdjustmentId = snapshot.InventoryAdjustmentId,
                InventoryAdjustmentId = adjustment.Id,
                WarehouseId = snapshot.WarehouseId,
                RoomId = snapshot.RoomId,
                GrowerLotId = snapshot.GrowerLotId,
                FruitProfileId = snapshot.FruitProfileId,
                GrowerName = snapshot.Grower,
                LotNumber = snapshot.Lot,
                PoolStart = snapshot.PoolStart,
                VarietyCode = snapshot.Variety,
                InventoryStatus = snapshot.InventoryStatus,
                PreviousAvailableBins = snapshot.CurrentBins,
                BinsRun = form.BinsRun,
                NewAvailableBins = newAvailable,
                Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
                RunAt = form.RunAt,
                CreatedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.BinsRunEntries.Add(entry);
        }
        else
        {
            before = EntrySnapshot(entry);
            entry.InventoryAdjustmentId = adjustment.Id;
            entry.WarehouseId = snapshot.WarehouseId;
            entry.RoomId = snapshot.RoomId;
            entry.GrowerLotId = snapshot.GrowerLotId;
            entry.FruitProfileId = snapshot.FruitProfileId;
            entry.GrowerName = snapshot.Grower;
            entry.LotNumber = snapshot.Lot;
            entry.PoolStart = snapshot.PoolStart;
            entry.VarietyCode = snapshot.Variety;
            entry.InventoryStatus = snapshot.InventoryStatus;
            entry.PreviousAvailableBins = effectiveAvailable;
            entry.BinsRun = form.BinsRun;
            entry.NewAvailableBins = newAvailable;
            entry.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
            entry.RunAt = form.RunAt;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(auditAction, entry, userId, before, EntrySnapshot(entry), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<BinsRunInventoryOptionViewModel>> GetAvailableInventoryAsync(int? warehouseId, int? roomId, CancellationToken cancellationToken)
    {
        var snapshots = await GetCurrentInventorySnapshotsAsync(warehouseId, roomId, cancellationToken);
        return snapshots
            .Where(x => x.CurrentBins > 0)
            .OrderBy(x => x.Facility)
            .ThenBy(x => x.Room)
            .ThenBy(x => x.Grower)
            .ThenBy(x => x.Variety)
            .ThenBy(x => x.Lot)
            .Select(x => new BinsRunInventoryOptionViewModel(
                x.InventoryKey,
                x.WarehouseId,
                x.RoomId,
                $"{x.Grower} - {x.Variety} - {x.Lot} - {x.CurrentBins} bins available",
                x.Grower,
                x.Lot,
                x.Variety,
                $"{x.Facility} / {x.Room}",
                x.CurrentBins))
            .ToList();
    }

    private async Task<InventorySnapshot?> GetCurrentInventoryByKeyAsync(string inventoryKey, CancellationToken cancellationToken)
    {
        var parts = inventoryKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        if (parts[0].Equals("R", StringComparison.OrdinalIgnoreCase) && long.TryParse(parts[1], out var receiptId))
        {
            return (await GetCurrentInventorySnapshotsAsync(null, null, cancellationToken)).SingleOrDefault(x => x.ReceiptId == receiptId);
        }

        if (parts[0].Equals("A", StringComparison.OrdinalIgnoreCase) && long.TryParse(parts[1], out var adjustmentId))
        {
            var snapshots = await GetCurrentInventorySnapshotsAsync(null, null, cancellationToken);
            var byAdjustmentId = snapshots.SingleOrDefault(x => x.InventoryAdjustmentId == adjustmentId);
            if (byAdjustmentId is not null)
            {
                return byAdjustmentId;
            }

            if (parts.Length >= 3)
            {
                var lotKey = parts[2];
                return snapshots.SingleOrDefault(x => x.ReceiptId is null
                    && string.Equals(CurrentStorageLotKey(x.RoomId, x.Lot, x.Variety), lotKey, StringComparison.OrdinalIgnoreCase));
            }
        }

        return null;
    }

    private async Task<InventorySnapshot?> GetCurrentInventoryByEntryAsync(BinsRunEntry entry, CancellationToken cancellationToken)
    {
        var snapshots = await GetCurrentInventorySnapshotsAsync(entry.WarehouseId, entry.RoomId, cancellationToken);
        return snapshots.SingleOrDefault(x =>
            (entry.ReceiptId is not null && x.ReceiptId == entry.ReceiptId)
            || (entry.ReceiptId is null
                && x.ReceiptId is null
                && string.Equals(CurrentStorageLotKey(x.RoomId, x.Lot, x.Variety), CurrentStorageLotKey(entry.RoomId, entry.LotNumber, entry.VarietyCode ?? ""), StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<IReadOnlyList<InventorySnapshot>> GetCurrentInventorySnapshotsAsync(int? warehouseId, int? roomId, CancellationToken cancellationToken)
    {
        var receiptsQuery = dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Where(x => !x.IsDeleted)
            .Where(x => x.ReceiptType == "Truck receipt");
        if (warehouseId is not null) receiptsQuery = receiptsQuery.Where(x => x.WarehouseId == warehouseId);
        if (roomId is not null) receiptsQuery = receiptsQuery.Where(x => x.RoomId == roomId);

        var receipts = await receiptsQuery.ToListAsync(cancellationToken);
        var correctionCutoffs = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == null && x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType)
            .Where(x => roomId == null || x.RoomId == roomId)
            .GroupBy(x => x.RoomId)
            .Select(x => new { RoomId = x.Key, Cutoff = x.Max(y => y.AdjustmentAt) })
            .ToDictionaryAsync(x => x.RoomId, x => x.Cutoff, cancellationToken);
        receipts = receipts
            .Where(x => !HasStorageExcludedIdentifierPrefix(x.CompuTechReceiptId, "LS"))
            .Where(x => !HasStorageExcludedIdentifierPrefix(x.CompuTechReceiptId, "DS"))
            .Where(x => !correctionCutoffs.TryGetValue(x.RoomId, out var cutoff) || x.ReceivedAt > cutoff)
            .ToList();
        var receiptIds = receipts.Select(x => x.Id).ToList();
        var depletionByReceipt = await dbContext.RoomDepletions.AsNoTracking()
            .Where(x => receiptIds.Contains(x.ReceiptId) && !x.IsVoided)
            .GroupBy(x => x.ReceiptId)
            .Select(x => new { ReceiptId = x.Key, Bins = x.Sum(y => y.BinCountDepleted) })
            .ToDictionaryAsync(x => x.ReceiptId, x => x.Bins, cancellationToken);
        var receiptAdjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
            .ToListAsync(cancellationToken);
        var latestAdjustmentByReceipt = receiptAdjustments
            .GroupBy(x => x.ReceiptId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AdjustmentAt).ThenByDescending(y => y.Id).First());

        var receiptSnapshots = receipts.Select(receipt =>
        {
            var latest = latestAdjustmentByReceipt.GetValueOrDefault(receipt.Id);
            var currentBins = latest is null ? Math.Max(0, receipt.BinCount - depletionByReceipt.GetValueOrDefault(receipt.Id)) : Math.Max(0, latest.NewBinCount);
            return new InventorySnapshot(
                $"R:{receipt.Id}",
                receipt.Id,
                latest?.Id,
                receipt.WarehouseId,
                receipt.RoomId,
                receipt.Warehouse.Code,
                receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
                receipt.GrowerLotId,
                receipt.FruitProfileId,
                receipt.GrowerName,
                !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber! : receipt.LotCode,
                receipt.PoolStart,
                receipt.FruitProfile.VarietyCode,
                "",
                currentBins);
        });

        var adjustmentQuery = dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Where(x => x.ReceiptId == null || x.AdjustmentType == "TransferIn");
        if (warehouseId is not null) adjustmentQuery = adjustmentQuery.Where(x => x.WarehouseId == warehouseId);
        if (roomId is not null) adjustmentQuery = adjustmentQuery.Where(x => x.RoomId == roomId);

        var adjustmentSnapshots = ApplyLatestCurrentBalanceRows(await adjustmentQuery.ToListAsync(cancellationToken))
            .Select(x => new InventorySnapshot(
                $"A:{x.Id}:{CurrentStorageLotKey(x.RoomId, x.LotNumber, x.VarietyCode ?? "")}",
                null,
                x.Id,
                x.WarehouseId,
                x.RoomId,
                x.Warehouse.Code,
                x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                x.GrowerLotId,
                x.FruitProfileId,
                x.GrowerName,
                x.LotNumber,
                x.PoolStart,
                x.VarietyCode ?? "",
                x.InventoryStatus ?? "",
                Math.Max(0, x.NewBinCount)));

        return receiptSnapshots.Concat(adjustmentSnapshots).ToList();
    }

    private static bool HasStorageExcludedIdentifierPrefix(string receiptId, string prefix) =>
        receiptId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (receiptId.Length == prefix.Length || !char.IsLetterOrDigit(receiptId[prefix.Length]));

    private static IEnumerable<RoomInventoryAdjustment> ApplyLatestCurrentBalanceRows(IEnumerable<RoomInventoryAdjustment> adjustments) =>
        adjustments
            .Where(x => !string.IsNullOrWhiteSpace(x.LotNumber))
            .GroupBy(x => CurrentStorageLotKey(x.RoomId, x.LotNumber, x.VarietyCode ?? ""), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var latestEffectiveDate = group.Max(x => x.AdjustmentAt);
                var latestRows = group.Where(x => x.AdjustmentAt == latestEffectiveDate).ToList();
                var latestCreatedAt = latestRows.Max(x => x.CreatedAt);
                return latestRows.Where(x => x.CreatedAt == latestCreatedAt);
            });

    private static RoomInventoryAdjustment CreateAdjustment(InventorySnapshot snapshot, int changeAmount, int previous, int next, string adjustmentType, int? userId, DateTimeOffset adjustmentAt, string? notes) =>
        new()
        {
            ReceiptId = snapshot.ReceiptId,
            CropYear = null,
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            GrowerLotId = snapshot.GrowerLotId,
            FruitProfileId = snapshot.FruitProfileId,
            GrowerName = snapshot.Grower,
            LotNumber = snapshot.Lot,
            PoolStart = snapshot.PoolStart,
            VarietyCode = snapshot.Variety,
            OldBinCount = previous,
            ChangeAmount = changeAmount,
            NewBinCount = next,
            AdjustmentType = adjustmentType,
            Source = "Bins Run",
            InventoryStatus = string.IsNullOrWhiteSpace(snapshot.InventoryStatus) ? null : snapshot.InventoryStatus,
            Reason = adjustmentType,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            AdjustmentAt = adjustmentAt,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        return provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private async Task<int?> CurrentUserIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking().Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task AddAuditAsync(string action, BinsRunEntry entry, int? userId, object? before, object? after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(BinsRunEntry),
            EntityKey = entry.Id.ToString(),
            UserId = userId,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after),
            SourceApplication = SourceApplication,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Task.CompletedTask;
    }

    private static object EntrySnapshot(BinsRunEntry entry) => new
    {
        entry.Id,
        entry.RoomId,
        entry.ReceiptId,
        entry.SourceInventoryAdjustmentId,
        entry.InventoryAdjustmentId,
        entry.GrowerName,
        entry.LotNumber,
        entry.VarietyCode,
        entry.PreviousAvailableBins,
        entry.BinsRun,
        entry.NewAvailableBins,
        entry.RunAt,
        entry.IsReversed,
        entry.ReverseReason
    };

    private static string CurrentStorageLotKey(int roomId, string lot, string variety) =>
        RoomInventoryImportService.CurrentStorageLotKey(roomId, lot, variety);

    private sealed record InventorySnapshot(
        string InventoryKey,
        long? ReceiptId,
        long? InventoryAdjustmentId,
        int WarehouseId,
        int RoomId,
        string Facility,
        string Room,
        int? GrowerLotId,
        int? FruitProfileId,
        string Grower,
        string Lot,
        string? PoolStart,
        string Variety,
        string InventoryStatus,
        int CurrentBins);
}
