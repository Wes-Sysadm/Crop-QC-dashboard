using System.Net.Mail;
using System.Data;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IEndOfDayFillAdminService
{
    Task<EndOfDayFillAdminPageViewModel> GetPageAsync(CancellationToken cancellationToken);
    Task<string?> SaveGroupAsync(EndOfDayFillGroupForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SaveRecipientAsync(EndOfDayFillRecipientForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SaveUserAssignmentsAsync(EndOfDayFillUserAssignmentsForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class EndOfDayFillAdminService(CropQcDbContext dbContext) : IEndOfDayFillAdminService
{
    public async Task<EndOfDayFillAdminPageViewModel> GetPageAsync(CancellationToken cancellationToken)
    {
        var groups = await dbContext.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Rooms)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var rooms = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse)
            .Where(x => x.IsActive && x.Warehouse.IsActive)
            .OrderBy(x => x.Warehouse.Code).ThenBy(x => x.SortOrder).ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var activeMembership = groups.Where(x => x.IsActive)
            .SelectMany(x => x.Rooms.Select(room => new { room.RoomId, GroupId = x.Id }))
            .GroupBy(x => x.RoomId).ToDictionary(x => x.Key, x => (int?)x.Single().GroupId);
        var recipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.EmailAddress)
            .ToListAsync(cancellationToken);
        return new EndOfDayFillAdminPageViewModel
        {
            Groups = groups.Select(x => new EndOfDayFillAdminGroupViewModel(x.Id, x.Name, x.Facility, x.IsActive, x.Rooms.Select(r => r.RoomId).Order().ToList())).ToList(),
            Rooms = rooms.Select(x => new EndOfDayFillAdminRoomViewModel(x.Id, OperationalFacility(x.Warehouse), x.Code, x.DisplayName ?? x.Name, x.SubLocation, x.CapacityBins, activeMembership.GetValueOrDefault(x.Id))).ToList(),
            Recipients = recipients.Select(x => new EndOfDayFillAdminRecipientViewModel(x.Id, x.EmailAddress, x.IsActive, x.SortOrder)).ToList()
        };
    }

    public async Task<string?> SaveGroupAsync(EndOfDayFillGroupForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var facility = form.Facility.Trim().ToUpperInvariant();
        if (facility is not ("WP" or "EBS")) return "Facility must be WP or EBS.";
        if (string.IsNullOrWhiteSpace(form.Name) || form.Name.Trim().Length > 150) return "A report group name of 150 characters or fewer is required.";
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var selectedRoomIds = form.RoomIds.Distinct().ToList();
        var rooms = await dbContext.Rooms.Include(x => x.Warehouse).Where(x => selectedRoomIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (rooms.Count != selectedRoomIds.Count) return "One or more selected rooms no longer exist.";
        var wrongFacility = rooms.FirstOrDefault(x => !OperationalFacility(x.Warehouse).Equals(facility, StringComparison.OrdinalIgnoreCase));
        if (wrongFacility is not null) return $"Room {wrongFacility.Code} does not belong to {facility}.";

        var duplicate = await dbContext.EndOfDayFillReportGroupRooms.AsNoTracking()
            .Where(x => selectedRoomIds.Contains(x.RoomId) && x.ReportGroup.IsActive && x.ReportGroupId != form.Id)
            .Select(x => new { x.Room.Code, x.ReportGroup.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (form.IsActive && duplicate is not null) return $"Room {duplicate.Code} already belongs to active report group {duplicate.Name}.";

        var changedBy = await FindUserAsync(changedByEmail, cancellationToken);
        if (changedBy is null) return "The administrator account could not be resolved.";
        EndOfDayFillReportGroup group;
        string? before = null;
        if (form.Id is null)
        {
            group = new EndOfDayFillReportGroup { Name = form.Name.Trim(), Facility = facility, IsActive = form.IsActive, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            dbContext.EndOfDayFillReportGroups.Add(group);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            group = await dbContext.EndOfDayFillReportGroups.Include(x => x.Rooms).SingleOrDefaultAsync(x => x.Id == form.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Report group was not found.");
            before = JsonSerializer.Serialize(new { group.Name, group.Facility, group.IsActive, RoomIds = group.Rooms.Select(x => x.RoomId).Order() });
            group.Name = form.Name.Trim();
            group.Facility = facility;
            group.IsActive = form.IsActive;
            group.UpdatedAt = DateTimeOffset.UtcNow;
            dbContext.EndOfDayFillReportGroupRooms.RemoveRange(group.Rooms.Where(x => !selectedRoomIds.Contains(x.RoomId)));
        }
        var existingIds = group.Rooms.Select(x => x.RoomId).ToHashSet();
        foreach (var roomId in selectedRoomIds.Where(x => !existingIds.Contains(x)))
        {
            dbContext.EndOfDayFillReportGroupRooms.Add(new EndOfDayFillReportGroupRoom { ReportGroupId = group.Id, RoomId = roomId, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = changedBy.Id });
        }
        dbContext.AuditLogs.Add(Audit(changedBy.Id, form.Id is null ? "create" : "update", "end-of-day-fill-report-group", group.Id.ToString(), before,
            JsonSerializer.Serialize(new { group.Name, group.Facility, group.IsActive, RoomIds = selectedRoomIds.Order() })));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SaveRecipientAsync(EndOfDayFillRecipientForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var email = form.EmailAddress.Trim().ToLowerInvariant();
        try { if (!new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase)) return "Enter a valid email address."; }
        catch (FormatException) { return "Enter a valid email address."; }
        var changedBy = await FindUserAsync(changedByEmail, cancellationToken);
        if (changedBy is null) return "The administrator account could not be resolved.";
        EndOfDayFillReportRecipient recipient;
        string? before = null;
        if (form.Id is null)
        {
            if (await dbContext.EndOfDayFillReportRecipients.AnyAsync(x => x.NormalizedEmailAddress == email.ToUpper(), cancellationToken)) return "That recipient already exists.";
            recipient = new EndOfDayFillReportRecipient { EmailAddress = email, NormalizedEmailAddress = email.ToUpperInvariant(), IsActive = form.IsActive, SortOrder = form.SortOrder, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, UpdatedByUserId = changedBy.Id };
            dbContext.EndOfDayFillReportRecipients.Add(recipient);
        }
        else
        {
            recipient = await dbContext.EndOfDayFillReportRecipients.SingleOrDefaultAsync(x => x.Id == form.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Recipient was not found.");
            if (await dbContext.EndOfDayFillReportRecipients.AnyAsync(x => x.Id != recipient.Id && x.NormalizedEmailAddress == email.ToUpper(), cancellationToken)) return "That recipient already exists.";
            before = JsonSerializer.Serialize(new { recipient.EmailAddress, recipient.IsActive, recipient.SortOrder });
            recipient.EmailAddress = email;
            recipient.NormalizedEmailAddress = email.ToUpperInvariant();
            recipient.IsActive = form.IsActive;
            recipient.SortOrder = form.SortOrder;
            recipient.UpdatedAt = DateTimeOffset.UtcNow;
            recipient.UpdatedByUserId = changedBy.Id;
        }
        dbContext.AuditLogs.Add(Audit(changedBy.Id, form.Id is null ? "create" : "update", "end-of-day-fill-report-recipient", form.Id?.ToString() ?? "new", before,
            JsonSerializer.Serialize(new { recipient.EmailAddress, recipient.IsActive, recipient.SortOrder })));
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SaveUserAssignmentsAsync(EndOfDayFillUserAssignmentsForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var changedBy = await FindUserAsync(changedByEmail, cancellationToken);
        if (changedBy is null) return "The administrator account could not be resolved.";
        var user = await dbContext.Users.Include(x => x.UserAssignments).SingleOrDefaultAsync(x => x.Id == form.UserId, cancellationToken);
        if (user is null) return "User not found.";
        var groupIds = form.GroupIds.Distinct().ToList();
        if (await dbContext.EndOfDayFillReportGroups.CountAsync(x => groupIds.Contains(x.Id), cancellationToken) != groupIds.Count) return "One or more report groups were not found.";
        var before = user.UserAssignments.Select(x => x.ReportGroupId).Order().ToList();
        dbContext.EndOfDayFillUserGroupAssignments.RemoveRange(user.UserAssignments.Where(x => !groupIds.Contains(x.ReportGroupId)));
        foreach (var groupId in groupIds.Where(x => !before.Contains(x)))
            dbContext.EndOfDayFillUserGroupAssignments.Add(new EndOfDayFillUserGroupAssignment { UserId = user.Id, ReportGroupId = groupId, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = changedBy.Id });
        dbContext.AuditLogs.Add(Audit(changedBy.Id, "update-assignments", "end-of-day-fill-user-assignment", user.Id.ToString(), JsonSerializer.Serialize(before), JsonSerializer.Serialize(groupIds.Order())));
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<User?> FindUserAsync(string email, CancellationToken ct) => await dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToLower() == email.Trim().ToLower(), ct);
    private static string OperationalFacility(Warehouse warehouse)
    {
        var identity = $"{warehouse.Code} {warehouse.Name}";
        if (identity.Contains("EBS", StringComparison.OrdinalIgnoreCase) || identity.Contains("Earl Brown", StringComparison.OrdinalIgnoreCase)) return "EBS";
        if (identity.Contains("WP", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("Windy Point", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("MCD", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("McDougall", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("DH", StringComparison.OrdinalIgnoreCase)) return "WP";
        return "Other";
    }
    private static AuditLog Audit(int userId, string action, string entity, string key, string? before, string? after) => new()
    {
        UserId = userId,
        Action = action,
        EntityName = entity,
        EntityKey = key,
        BeforeValuesJson = before,
        AfterValuesJson = after,
        SourceApplication = "CropQc.Web",
        CreatedAt = DateTimeOffset.UtcNow
    };
}
