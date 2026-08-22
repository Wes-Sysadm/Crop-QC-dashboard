using System.Data;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRoomSealingService
{
    Task<RoomSealConfirmationViewModel?> GetConfirmationAsync(int roomId, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> ChangeStateAsync(RoomSealForm form, bool seal, ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class RoomSealingService(CropQcDbContext dbContext) : IRoomSealingService
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<RoomSealConfirmationViewModel?> GetConfirmationAsync(
        int roomId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(principal);
        return await dbContext.Rooms.AsNoTracking()
            .Where(x => x.Id == roomId)
            .Select(x => new RoomSealConfirmationViewModel
            {
                RoomId = x.Id,
                Warehouse = x.Warehouse.Code,
                Room = x.CropQcRoomName ?? x.DisplayName ?? x.Code,
                IsSealed = x.IsSealed,
                SealedAt = x.SealedAt,
                SealedBy = x.SealedByUser == null ? null : x.SealedByUser.DisplayName,
                Form = new RoomSealForm { RoomId = x.Id, ExpectedIsSealed = x.IsSealed }
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> ChangeStateAsync(
        RoomSealForm form,
        bool seal,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(principal);
        if (form.RoomId <= 0) return "Room was not found.";
        if (form.Note?.Trim().Length > 500) return "Note must be 500 characters or fewer.";

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var rooms = await RoomMovementSealGuard.LockRoomsAsync(dbContext, [form.RoomId], cancellationToken);
        var room = rooms.SingleOrDefault();
        if (room is null) return "Room was not found.";
        if (room.IsSealed != form.ExpectedIsSealed)
        {
            return "Room sealing state changed while this confirmation was open. Refresh and review the current state.";
        }
        if (room.IsSealed == seal)
        {
            return null;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var actor = string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
        if (actor is null) return "The current active user could not be resolved.";

        var now = DateTimeOffset.UtcNow;
        var before = new { room.IsSealed, room.SealedAt, room.SealedByUserId };
        room.IsSealed = seal;
        room.SealedAt = seal ? now : null;
        room.SealedByUserId = seal ? actor.Id : null;
        var displayRoom = room.CropQcRoomName ?? room.DisplayName ?? room.Code;
        dbContext.RoomSealEvents.Add(new RoomSealEvent
        {
            RoomId = room.Id,
            Action = seal ? RoomSealActions.Seal : RoomSealActions.Unseal,
            ChangedAt = now,
            ChangedByUserId = actor.Id,
            WarehouseCodeSnapshot = room.Warehouse.Code,
            RoomCodeSnapshot = displayRoom,
            Note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim()
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = actor.Id,
            Action = seal ? "RoomSealed" : "RoomUnsealed",
            EntityName = nameof(Room),
            EntityKey = room.Id.ToString(),
            BeforeValuesJson = JsonSerializer.Serialize(before, AuditJson),
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                room.IsSealed,
                room.SealedAt,
                room.SealedByUserId,
                Warehouse = room.Warehouse.Code,
                Room = displayRoom,
                Note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim()
            }, AuditJson),
            SourceApplication = "Web",
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public static bool CanManage(ClaimsPrincipal principal) =>
        principal.IsInRole(BuiltInRoleNames.Manager) || principal.IsInRole(BuiltInRoleNames.Admin);

    private static void EnsureManagerOrAdmin(ClaimsPrincipal principal)
    {
        if (!CanManage(principal)) throw new UnauthorizedAccessException("Manager or Admin role is required to seal or unseal a room.");
    }
}

public static class RoomMovementSealGuard
{
    public static async Task<string?> ValidateAsync(
        CropQcDbContext dbContext,
        IReadOnlyCollection<int> sourceRoomIds,
        IReadOnlyCollection<int> destinationRoomIds,
        CancellationToken cancellationToken)
    {
        var allIds = sourceRoomIds.Concat(destinationRoomIds).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (allIds.Count == 0) return null;
        var rooms = await LockRoomsAsync(dbContext, allIds, cancellationToken);
        var sourceIds = sourceRoomIds.ToHashSet();
        var destinationIds = destinationRoomIds.ToHashSet();
        foreach (var room in rooms.Where(x => x.IsSealed).OrderBy(x => x.Id))
        {
            var label = $"{room.Warehouse.Code} {room.CropQcRoomName ?? room.DisplayName ?? room.Code}";
            if (sourceIds.Contains(room.Id) && destinationIds.Contains(room.Id))
                return $"Room {label} is sealed and cannot be used for inventory movement.";
            if (sourceIds.Contains(room.Id))
                return $"Room {label} is sealed. Unseal it before moving inventory out.";
            return $"Room {label} is sealed. Unseal it before moving inventory in.";
        }
        return null;
    }

    internal static async Task<List<Room>> LockRoomsAsync(
        CropQcDbContext dbContext,
        IReadOnlyCollection<int> roomIds,
        CancellationToken cancellationToken)
    {
        var ids = roomIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (ids.Count == 0) return [];
        var provider = dbContext.Database.ProviderName ?? "";
        var rooms = new List<Room>(ids.Count);
        foreach (var id in ids)
        {
            Room? room;
            if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                room = await dbContext.Rooms
                    .FromSqlInterpolated($"SELECT * FROM \"Rooms\" WHERE \"Id\" = {id} FOR UPDATE")
                    .Include(x => x.Warehouse)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                room = await dbContext.Rooms
                    .FromSqlInterpolated($"SELECT * FROM [Rooms] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
                    .Include(x => x.Warehouse)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            else
            {
                room = await dbContext.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            }
            if (room is not null) rooms.Add(room);
        }
        return rooms;
    }
}
