using System.Security.Claims;
using CropQc.Data;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IEbsInventoryCleanupService
{
    Task<EbsInventoryCleanupPageViewModel> GetReviewAsync(
        int page,
        int pageSize,
        ClaimsPrincipal user,
        CancellationToken cancellationToken);
}

public sealed class EbsInventoryCleanupService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledgerQuery,
    IUserAccessService userAccessService) : IEbsInventoryCleanupService
{
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

        var protectedRoom = await GetEvans7RoomAsync(ebsWarehouseId.Value, cancellationToken);
        var snapshots = await ledgerQuery.GetSnapshotsAsync(ebsWarehouseId, null, cancellationToken);
        var candidates = snapshots
            .Where(x => x.CurrentBins != 0 && x.RoomId != protectedRoom.Id)
            .OrderBy(x => x.Room)
            .ThenBy(x => x.Lot, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new EbsInventoryCleanupPageViewModel
        {
            Page = page,
            PageSize = pageSize,
            TotalRows = candidates.Count,
            ProtectedRoomId = protectedRoom.Id,
            ProtectedRoom = RoomDisplayName(protectedRoom),
            CandidateCurrentBins = candidates.Sum(x => x.CurrentBins),
            Rows = candidates
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToRow)
                .ToList()
        };
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

    private async Task<ProtectedRoomIdentity> GetEvans7RoomAsync(
        int ebsWarehouseId,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Rooms.AsNoTracking()
            .Where(x => x.WarehouseId == ebsWarehouseId)
            .Select(x => new ProtectedRoomIdentity(
                x.Id,
                x.Code,
                x.Name,
                x.CropQcRoomName,
                x.CompuTechRoomCode,
                x.DisplayName))
            .ToListAsync(cancellationToken);
        var matches = candidates.Where(IsEvans7Room).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "The EBS Evans 7 room identity could not be resolved. Cleanup remains disabled."),
            _ => throw new InvalidOperationException(
                "More than one EBS room matches Evans 7. Cleanup remains disabled until room identity is corrected.")
        };
    }

    public static bool IsEvans7Room(ProtectedRoomIdentity room) =>
        new[]
        {
            room.Code,
            room.Name,
            room.CropQcRoomName,
            room.CompuTechRoomCode,
            room.DisplayName
        }.Select(NormalizeRoomIdentity)
            .Any(x => x is "EVANS7" or "EVANSSTREET7" or "EVANCA07" or "EVANCA7");

    private static string NormalizeRoomIdentity(string? value) =>
        new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string RoomDisplayName(ProtectedRoomIdentity room) =>
        room.CropQcRoomName
        ?? room.DisplayName
        ?? room.Name
        ?? room.Code
        ?? $"Room {room.Id}";

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
            WarningReason = "EBS test inventory outside the protected Evans 7 room. Removal requires the reviewed operational script and explicit production authorization."
        };

    public sealed record ProtectedRoomIdentity(
        int Id,
        string? Code,
        string? Name,
        string? CropQcRoomName,
        string? CompuTechRoomCode,
        string? DisplayName);
}
