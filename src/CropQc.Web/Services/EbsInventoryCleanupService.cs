using System.Data;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IEbsInventoryCleanupService
{
    Task<EbsInventoryCleanupPageViewModel> GetReviewAsync(int page, int pageSize, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ApproveAsync(ApproveEbsInventoryCleanupForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class EbsInventoryCleanupService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledgerQuery,
    IInventoryDeductionInvariantService inventoryInvariant,
    IUserAccessService userAccessService) : IEbsInventoryCleanupService
{
    public const string AdjustmentType = "HistoricalBinsRunCleanup";
    public const string SourceApplication = "CropQc.Web";
    public const string RequiredReason = "Historical inventory cleanup — fruit was packed before current room-ledger tracking was complete.";
    private const int MaximumPageSize = 100;

    public async Task<EbsInventoryCleanupPageViewModel> GetReviewAsync(
        int page,
        int pageSize,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAdminAsync(user, cancellationToken);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var ebsWarehouseId = await EbsWarehouseIdAsync(cancellationToken);
        if (ebsWarehouseId is null)
        {
            return new EbsInventoryCleanupPageViewModel { Page = page, PageSize = pageSize };
        }

        var snapshots = await ledgerQuery.GetSnapshotsAsync(ebsWarehouseId, null, cancellationToken);
        var stale = snapshots
            .Where(x => x.CurrentBins > 0 && !IsProtectedGalaEvans7(x))
            .OrderBy(x => x.Room)
            .ThenBy(x => x.Lot, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new EbsInventoryCleanupPageViewModel
        {
            Page = page,
            PageSize = pageSize,
            TotalRows = stale.Count,
            Rows = stale
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToRow)
                .ToList()
        };
    }

    public async Task<string?> ApproveAsync(
        ApproveEbsInventoryCleanupForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAdminAsync(user, cancellationToken);
        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "A historical cleanup explanation is required.";
        }
        if (string.IsNullOrWhiteSpace(form.OperationKey))
        {
            return "A cleanup request identifier is required.";
        }

        var ebsWarehouseId = await EbsWarehouseIdAsync(cancellationToken);
        if (ebsWarehouseId is null)
        {
            return "The EBS facility is not configured.";
        }

        if (await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .AnyAsync(x => x.InventoryOperationKey == form.OperationKey, cancellationToken))
        {
            return null;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var snapshot = (await ledgerQuery.GetSnapshotsAsync(ebsWarehouseId, null, cancellationToken))
            .SingleOrDefault(x => x.LatestAdjustmentId == form.InventorySnapshotId);
        if (snapshot is null || snapshot.CurrentBins <= 0)
        {
            return "This EBS room-lot balance no longer exists.";
        }
        if (IsProtectedGalaEvans7(snapshot))
        {
            return "Gala inventory in Evans 7 is protected and cannot be included in historical cleanup.";
        }
        if (snapshot.CurrentBins != form.ExpectedCurrentBins)
        {
            return $"The room-lot balance changed from {form.ExpectedCurrentBins} to {snapshot.CurrentBins} bins. Reload and review it again.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var adjustment = new RoomInventoryAdjustment
        {
            CropYear = snapshot.CropYear,
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            GrowerLotId = snapshot.GrowerLotId,
            FruitProfileId = snapshot.FruitProfileId,
            GrowerName = snapshot.Grower,
            LotNumber = snapshot.Lot,
            PoolStart = snapshot.PoolStart,
            VarietyCode = snapshot.Variety,
            OldBinCount = snapshot.CurrentBins,
            ChangeAmount = -snapshot.CurrentBins,
            NewBinCount = 0,
            AdjustmentType = AdjustmentType,
            Source = "Historical Bins Run Cleanup",
            InventoryStatus = snapshot.InventoryStatus,
            Reason = form.Reason.Trim(),
            Notes = RequiredReason,
            AdjustmentAt = form.RunAt,
            CreatedByUserId = userId,
            CreatedAt = now,
            InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
            InventoryOperationKey = form.OperationKey.Trim()
        };
        var entry = new BinsRunEntry
        {
            InventoryAdjustment = adjustment,
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            CropYear = snapshot.CropYear,
            GrowerLotId = snapshot.GrowerLotId,
            FruitProfileId = snapshot.FruitProfileId,
            GrowerName = snapshot.Grower,
            LotNumber = snapshot.Lot,
            PoolStart = snapshot.PoolStart,
            VarietyCode = snapshot.Variety,
            InventoryStatus = snapshot.InventoryStatus,
            PreviousAvailableBins = snapshot.CurrentBins,
            BinsRun = snapshot.CurrentBins,
            NewAvailableBins = 0,
            Notes = form.Reason.Trim(),
            RunAt = form.RunAt,
            CreatedByUserId = userId,
            CreatedAt = now,
            TransactionType = ActualRunTransactionTypes.Legacy
        };
        dbContext.BinsRunEntries.Add(entry);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "ApproveHistoricalInventoryCleanup",
            EntityName = nameof(BinsRunEntry),
            EntityKey = form.OperationKey.Trim(),
            UserId = userId,
            BeforeValuesJson = JsonSerializer.Serialize(new
            {
                snapshot.Facility,
                snapshot.Room,
                snapshot.CropYear,
                snapshot.Lot,
                snapshot.Variety,
                snapshot.ProductionType,
                snapshot.CurrentBins
            }),
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                RemainingBins = 0,
                Reason = form.Reason.Trim(),
                RunAt = form.RunAt
            }),
            SourceApplication = SourceApplication,
            CreatedAt = now
        });
        await inventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return null;
    }

    private async Task RequireAdminAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(
            user,
            ApplicationAreas.HistoricalInventoryCleanup,
            PageAccessLevel.Admin,
            cancellationToken))
        {
            throw new UnauthorizedAccessException("Historical Inventory Cleanup Admin access is required.");
        }
    }

    private async Task<int?> EbsWarehouseIdAsync(CancellationToken cancellationToken) =>
        await dbContext.Warehouses.AsNoTracking()
            .Where(x => x.IsActive && x.Code == "EBS")
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<int?> CurrentUserIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
    }

    public static bool IsProtectedGalaEvans7(RoomInventoryLedgerSnapshot row) =>
        row.Room.Contains("Evans 7", StringComparison.OrdinalIgnoreCase)
        && (row.Variety.Contains("Gala", StringComparison.OrdinalIgnoreCase)
            || row.VarietyName.Contains("Gala", StringComparison.OrdinalIgnoreCase));

    private static EbsInventoryCleanupRowViewModel ToRow(RoomInventoryLedgerSnapshot x) =>
        new()
        {
            InventorySnapshotId = x.LatestAdjustmentId,
            WarehouseId = x.WarehouseId,
            RoomId = x.RoomId,
            Room = x.Room,
            CropYear = x.CropYear,
            FruitProfileId = x.FruitProfileId,
            Grower = x.Grower,
            Lot = x.Lot,
            Variety = x.VarietyName,
            ProductionType = x.ProductionType,
            CurrentBins = x.CurrentBins,
            PositiveLedgerOrigin = x.PositiveBins,
            BinsRunDeductions = x.LegacyBinsRunDepletionBins + x.ActualRunDepletionBins,
            TransferActivity = x.TransferInBins + x.TransferOutBins,
            TrueUps = x.TrueUpBins,
            LastActivityAt = x.LastTransactionAt,
            EvidenceSource = $"Permanent room ledger ({x.TransactionCount} transactions)",
            WarningReason = "Active EBS balance outside protected Gala inventory in Evans 7; confirm physical disposition before cleanup."
        };
}
