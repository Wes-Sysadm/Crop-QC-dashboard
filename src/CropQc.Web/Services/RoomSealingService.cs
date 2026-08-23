using System.Data;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRoomSealingService
{
    Task<RoomSealConfirmationViewModel?> GetConfirmationAsync(int roomId, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> ChangeStateAsync(RoomSealForm form, bool seal, ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class RoomSealingService(
    CropQcDbContext dbContext,
    IBusinessTimeService? businessTime = null) : IRoomSealingService
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);
    private IBusinessTimeService BusinessTime { get; } = businessTime ?? new PacificBusinessTimeService(new SystemClock());

    public async Task<RoomSealConfirmationViewModel?> GetConfirmationAsync(
        int roomId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(principal);
        var room = await dbContext.Rooms.AsNoTracking()
            .Where(x => x.Id == roomId)
            .Include(x => x.Warehouse)
            .Include(x => x.SealedByUser)
            .SingleOrDefaultAsync(cancellationToken);
        if (room is null) return null;

        var now = BusinessTime.UtcNow;
        var scheduled = RoomSealState.IsScheduled(room, now);
        var sealedNow = RoomSealState.IsEffectivelySealed(room, now);
        var formTime = scheduled ? room.SealedAt!.Value : now;
        var pacific = BusinessTime.ToPacific(formTime);
        return new RoomSealConfirmationViewModel
        {
            RoomId = room.Id,
            Warehouse = room.Warehouse.Code,
            Room = room.CropQcRoomName ?? room.DisplayName ?? room.Code,
            HasActiveSeal = room.IsSealed,
            IsSealScheduled = scheduled,
            IsSealed = sealedNow,
            SealedAt = room.SealedAt,
            SealRecordedAt = room.SealRecordedAt,
            SealedBy = room.SealedByUser?.DisplayName,
            Form = new RoomSealForm
            {
                RoomId = room.Id,
                ExpectedIsSealed = room.IsSealed,
                ExpectedEffectiveAt = room.SealedAt,
                EffectiveDate = DateOnly.FromDateTime(pacific.DateTime),
                EffectiveTime = TimeOnly.FromDateTime(pacific.DateTime)
            }
        };
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

        DateTimeOffset? requestedEffectiveAt = null;
        if (form.EffectiveDate is not null && form.EffectiveTime is not null)
        {
            try
            {
                requestedEffectiveAt = BusinessTime.PacificLocalToUtc(
                    form.EffectiveDate.Value.ToDateTime(form.EffectiveTime.Value, DateTimeKind.Unspecified));
            }
            catch (ArgumentException exception)
            {
                return exception.Message;
            }
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var rooms = await RoomMovementSealGuard.LockRoomsAsync(dbContext, [form.RoomId], cancellationToken);
        var room = rooms.SingleOrDefault();
        if (room is null) return "Room was not found.";
        if (room.IsSealed != form.ExpectedIsSealed
            || (room.IsSealed && form.ExpectedEffectiveAt != room.SealedAt))
        {
            return "Room sealing state changed while this confirmation was open. Refresh and review the current state.";
        }

        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var actor = string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
        if (actor is null) return "The current active user could not be resolved.";

        var now = BusinessTime.UtcNow;
        var wasScheduled = RoomSealState.IsScheduled(room, now);
        var wasEffectivelySealed = RoomSealState.IsEffectivelySealed(room, now);
        var before = new { room.IsSealed, room.SealedAt, room.SealRecordedAt, room.SealedByUserId };
        string action;
        string auditAction;
        DateTimeOffset eventEffectiveAt;
        DateTimeOffset? previousEffectiveAt = null;

        if (seal)
        {
            if (requestedEffectiveAt is null) return "Seal Date and Seal Time are required.";
            if (!room.IsSealed)
            {
                room.IsSealed = true;
                room.SealedAt = requestedEffectiveAt;
                room.SealRecordedAt = now;
                room.SealedByUserId = actor.Id;
                action = requestedEffectiveAt > now ? RoomSealActions.SealScheduled : RoomSealActions.Seal;
                auditAction = requestedEffectiveAt > now ? "RoomSealScheduled" : "RoomSealed";
                eventEffectiveAt = requestedEffectiveAt.Value;
            }
            else
            {
                if (room.SealedAt == requestedEffectiveAt) return null;
                if (!wasScheduled)
                {
                    return "This Room is already actively sealed. Unseal it before recording a different seal.";
                }
                previousEffectiveAt = room.SealedAt;
                room.SealedAt = requestedEffectiveAt;
                room.SealRecordedAt = now;
                room.SealedByUserId = actor.Id;
                action = RoomSealActions.ScheduleChanged;
                auditAction = "RoomSealScheduleChanged";
                eventEffectiveAt = requestedEffectiveAt.Value;
            }
        }
        else if (wasScheduled)
        {
            eventEffectiveAt = room.SealedAt!.Value;
            previousEffectiveAt = room.SealedAt;
            room.IsSealed = false;
            room.SealedAt = null;
            room.SealRecordedAt = null;
            room.SealedByUserId = null;
            action = RoomSealActions.ScheduleCanceled;
            auditAction = "RoomSealScheduleCanceled";
        }
        else if (wasEffectivelySealed)
        {
            if (requestedEffectiveAt is null) return "Unseal Date and Unseal Time are required.";
            if (requestedEffectiveAt > now) return "A future Unseal time is not supported. Enter the current or a past Pacific time.";
            if (room.SealedAt is not null && requestedEffectiveAt < room.SealedAt)
                return "Unseal time cannot be earlier than the effective seal time.";
            eventEffectiveAt = requestedEffectiveAt.Value;
            room.IsSealed = false;
            room.SealedAt = null;
            room.SealRecordedAt = null;
            room.SealedByUserId = null;
            action = RoomSealActions.Unseal;
            auditAction = "RoomUnsealed";
        }
        else
        {
            return null;
        }

        var displayRoom = room.CropQcRoomName ?? room.DisplayName ?? room.Code;
        dbContext.RoomSealEvents.Add(new RoomSealEvent
        {
            RoomId = room.Id,
            Action = action,
            EffectiveAt = eventEffectiveAt,
            PreviousEffectiveAt = previousEffectiveAt,
            ChangedAt = now,
            ChangedByUserId = actor.Id,
            WarehouseCodeSnapshot = room.Warehouse.Code,
            RoomCodeSnapshot = displayRoom,
            Note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim()
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = actor.Id,
            Action = auditAction,
            EntityName = nameof(Room),
            EntityKey = room.Id.ToString(),
            BeforeValuesJson = JsonSerializer.Serialize(before, AuditJson),
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                room.IsSealed,
                room.SealedAt,
                room.SealRecordedAt,
                room.SealedByUserId,
                EventAction = action,
                EffectiveAt = eventEffectiveAt,
                PreviousEffectiveAt = previousEffectiveAt,
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

public static class RoomSealState
{
    public static bool IsScheduled(Room room, DateTimeOffset utcNow) =>
        room.IsSealed && room.SealedAt is not null && room.SealedAt > utcNow;

    public static bool IsEffectivelySealed(Room room, DateTimeOffset utcNow) =>
        room.IsSealed && (room.SealedAt is null || room.SealedAt <= utcNow);
}

public static class RoomMovementSealGuard
{
    public static Task<string?> ValidateAsync(
        CropQcDbContext dbContext,
        IReadOnlyCollection<int> sourceRoomIds,
        IReadOnlyCollection<int> destinationRoomIds,
        CancellationToken cancellationToken) =>
        ValidateAsync(
            dbContext,
            sourceRoomIds,
            destinationRoomIds,
            new PacificBusinessTimeService(new SystemClock()),
            cancellationToken);

    public static async Task<string?> ValidateAsync(
        CropQcDbContext dbContext,
        IReadOnlyCollection<int> sourceRoomIds,
        IReadOnlyCollection<int> destinationRoomIds,
        IBusinessTimeService businessTime,
        CancellationToken cancellationToken)
    {
        var allIds = sourceRoomIds.Concat(destinationRoomIds).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (allIds.Count == 0) return null;
        var rooms = await LockRoomsAsync(dbContext, allIds, cancellationToken);
        var sourceIds = sourceRoomIds.ToHashSet();
        var destinationIds = destinationRoomIds.ToHashSet();
        foreach (var room in rooms.Where(x => RoomSealState.IsEffectivelySealed(x, businessTime.UtcNow)).OrderBy(x => x.Id))
        {
            var label = $"{room.Warehouse.Code} {room.CropQcRoomName ?? room.DisplayName ?? room.Code}";
            var since = room.SealedAt is null ? "" : $" since {businessTime.FormatPacific(room.SealedAt, "g")}";
            if (sourceIds.Contains(room.Id) && destinationIds.Contains(room.Id))
                return $"Room {label} has been sealed{since} and cannot be used for inventory movement. A Manager must unseal the Room before fruit can be moved.";
            if (sourceIds.Contains(room.Id))
                return $"Room {label} has been sealed{since}. A Manager must unseal the Room before fruit can be moved out.";
            return $"Room {label} has been sealed{since}. A Manager must unseal the Room before fruit can be moved in.";
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
