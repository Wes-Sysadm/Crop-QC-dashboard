using System.Net.Mail;
using System.Data;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IEndOfDayFillAdminService
{
    Task<EndOfDayFillAdminPageViewModel> GetPageAsync(CancellationToken cancellationToken);
    Task<string?> SaveGroupAsync(EndOfDayFillGroupForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SaveRecipientAsync(EndOfDayFillRecipientForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SaveUserAssignmentsAsync(EndOfDayFillUserAssignmentsForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<EndOfDayFillHistoryDetailViewModel?> GetPendingDetailAsync(long sendAttemptId, CancellationToken cancellationToken);
    Task<string?> ResolvePendingSendAsync(EndOfDayFillRecoveryForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class EndOfDayFillAdminService(
    CropQcDbContext dbContext,
    IFacilityContextService facilityContext,
    IBusinessTimeService businessTime,
    IUserAccessService userAccessService,
    IEndOfDayFillWarehouseLabelResolver? warehouseLabelResolver = null) : IEndOfDayFillAdminService
{
    private readonly IEndOfDayFillWarehouseLabelResolver warehouseLabels = warehouseLabelResolver ?? new EndOfDayFillWarehouseLabelResolver();
    public async Task<EndOfDayFillAdminPageViewModel> GetPageAsync(CancellationToken cancellationToken)
    {
        var groups = await dbContext.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Rooms).ThenInclude(x => x.Warehouse)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var recipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.EmailAddress)
            .ToListAsync(cancellationToken);
        var warehouses = await dbContext.Warehouses.AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var staleBefore = businessTime.UtcNow - EndOfDayFillRecoveryPolicy.StaleAfter;
        var reservations = await dbContext.EndOfDayFillSendReservations.AsNoTracking()
            .Include(x => x.SendAttempt)
            .ToListAsync(cancellationToken);
        var staleAttempts = reservations
            .Where(x => x.CreatedAt <= staleBefore && x.SendAttempt.Status == EndOfDayFillSendStatuses.Pending)
            .OrderBy(x => x.CreatedAt)
            .ToList();
        return new EndOfDayFillAdminPageViewModel
        {
            Groups = groups.Select(x => new EndOfDayFillAdminGroupViewModel(
                x.Id,
                x.Name,
                x.WarehouseId,
                warehouseLabels.Resolve(x.WarehouseId, x.Warehouse.Code, x.Warehouse.Name),
                x.Facility,
                x.IsActive,
                x.Rooms.Count,
                x.Rooms.OrderBy(r => r.Warehouse.Code).ThenBy(r => r.SortOrder).ThenBy(r => r.Code)
                    .Select(r => new EndOfDayFillAdminRoomViewModel(r.Id, r.WarehouseId, warehouseLabels.Resolve(r.WarehouseId, r.Warehouse.Code, r.Warehouse.Name), r.Code, r.DisplayName ?? r.Name, r.SubLocation, r.CapacityBins))
                    .ToList())).ToList(),
            Warehouses = warehouses.Select(x => new EndOfDayFillWarehouseOption(
                x.Id,
                warehouseLabels.Resolve(x.Id, x.Code, x.Name),
                x.Code,
                x.Name,
                facilityContext.GetOperatingCompanyFacility(x.Code, x.Name),
                x.IsActive)).ToList(),
            Recipients = recipients.Select(x => new EndOfDayFillAdminRecipientViewModel(x.Id, x.EmailAddress, x.IsActive, x.SortOrder)).ToList(),
            StaleAttempts = staleAttempts.Select(x => PendingView(x.SendAttempt, true)).ToList()
        };
    }

    public async Task<string?> SaveGroupAsync(EndOfDayFillGroupForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Name) || form.Name.Trim().Length > 150) return "A report group name of 150 characters or fewer is required.";
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var changedBy = await FindUserAsync(changedByEmail, cancellationToken);
        if (changedBy is null) return "The administrator account could not be resolved.";
        var warehouse = await dbContext.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.WarehouseId, cancellationToken);
        if (warehouse is null) return "Select a valid warehouse.";
        if (!warehouse.IsActive && form.Id is null) return "An inactive warehouse cannot be assigned to a new report group.";
        var facility = facilityContext.GetOperatingCompanyFacility(warehouse.Code, warehouse.Name);
        if (facility is not ("WP" or "EBS")) return "The selected warehouse does not have a supported WP or EBS operating facility.";
        if (form.IsActive && await dbContext.EndOfDayFillReportGroups.AsNoTracking().AnyAsync(
                x => x.Id != (form.Id ?? 0) && x.WarehouseId == warehouse.Id && x.IsActive,
                cancellationToken))
            return $"{warehouseLabels.Resolve(warehouse.Id, warehouse.Code, warehouse.Name)} already has an active End of Day Fill report.";
        EndOfDayFillReportGroup group;
        string? before = null;
        if (form.Id is null)
        {
            group = new EndOfDayFillReportGroup { Name = form.Name.Trim(), WarehouseId = warehouse.Id, Facility = facility, IsActive = form.IsActive, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            dbContext.EndOfDayFillReportGroups.Add(group);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            group = await dbContext.EndOfDayFillReportGroups.Include(x => x.Rooms).ThenInclude(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Report group was not found.");
            var wrongWarehouse = group.Rooms.FirstOrDefault(x => x.WarehouseId != warehouse.Id);
            if (wrongWarehouse is not null) return $"Report warehouse cannot change while room {wrongWarehouse.Code} is assigned. Update the room in Master Data first.";
            before = JsonSerializer.Serialize(new { group.Name, group.WarehouseId, group.Facility, group.IsActive, RoomIds = group.Rooms.Select(x => x.Id).Order() });
            group.Name = form.Name.Trim();
            group.WarehouseId = warehouse.Id;
            group.Facility = facility;
            group.IsActive = form.IsActive;
            group.UpdatedAt = DateTimeOffset.UtcNow;
        }
        dbContext.AuditLogs.Add(Audit(changedBy.Id, form.Id is null ? "create" : "update", "end-of-day-fill-report-group", group.Id.ToString(), before,
            JsonSerializer.Serialize(new { group.Name, group.WarehouseId, WarehouseLabel = warehouseLabels.Resolve(warehouse.Id, warehouse.Code, warehouse.Name), group.Facility, group.IsActive, RoomIds = group.Rooms.Select(x => x.Id).Order() })));
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

    public async Task<EndOfDayFillHistoryDetailViewModel?> GetPendingDetailAsync(long sendAttemptId, CancellationToken cancellationToken)
    {
        var send = await dbContext.EndOfDayFillReportSends.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sendAttemptId && x.Status == EndOfDayFillSendStatuses.Pending, cancellationToken);
        return send is null ? null : ToHistoryDetail(send);
    }

    public async Task<string?> ResolvePendingSendAsync(EndOfDayFillRecoveryForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        if (await userAccessService.GetAccessLevelAsync(changedByEmail, ApplicationAreas.MasterData, cancellationToken) < PageAccessLevel.Admin)
            return "Master Data Admin access is required to resolve an uncertain send.";
        if (!form.Confirmed) return "Explicitly confirm the recovery decision.";
        var reason = form.Reason.Trim();
        if (reason.Length is < 5 or > 1000) return "A recovery reason between 5 and 1,000 characters is required.";
        var confirmedSent = form.Resolution.Equals("confirmed-sent", StringComparison.OrdinalIgnoreCase);
        var confirmedNotSent = form.Resolution.Equals("confirmed-not-sent", StringComparison.OrdinalIgnoreCase);
        if (!confirmedSent && !confirmedNotSent) return "Choose Confirmed sent or Confirmed not sent.";

        var administrator = await FindUserAsync(changedByEmail, cancellationToken);
        if (administrator is null) return "The administrator account could not be resolved.";

        var pendingGroupId = await dbContext.EndOfDayFillReportSends.AsNoTracking()
            .Where(x => x.Id == form.SendAttemptId && x.Status == EndOfDayFillSendStatuses.Pending)
            .Select(x => (int?)x.ReportGroupId)
            .SingleOrDefaultAsync(cancellationToken);
        if (pendingGroupId is null) return "This pending attempt has already been resolved or does not exist.";

        var isPostgreSql = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            isPostgreSql ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
            cancellationToken);
        if (isPostgreSql)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({EndOfDayFillRecoveryPolicy.AdvisoryLockNamespace}, {pendingGroupId.Value})",
                cancellationToken);
        }
        var reservation = await dbContext.EndOfDayFillSendReservations
            .Include(x => x.SendAttempt)
            .SingleOrDefaultAsync(x => x.SendAttemptId == form.SendAttemptId, cancellationToken);
        if (reservation is null || reservation.SendAttempt.Status != EndOfDayFillSendStatuses.Pending)
            return "This pending attempt has already been resolved or no longer has its fail-closed reservation.";
        if (businessTime.UtcNow - reservation.CreatedAt < EndOfDayFillRecoveryPolicy.StaleAfter)
            return $"This send is still inside the {EndOfDayFillRecoveryPolicy.StaleAfter.TotalMinutes:N0}-minute processing window.";

        var attempt = reservation.SendAttempt;
        if (confirmedSent)
        {
            var successRevisionKey = $"{attempt.ReportGroupId}:{attempt.PacificReportDate:yyyyMMdd}:{attempt.RevisionNumber}";
            if (await dbContext.EndOfDayFillReportSends.AsNoTracking().AnyAsync(
                    x => x.Id != attempt.Id && x.SuccessRevisionKey == successRevisionKey, cancellationToken))
                return "A successful report already occupies this revision. The pending attempt was not changed.";
            attempt.Status = EndOfDayFillSendStatuses.Succeeded;
            attempt.SentAt = attempt.AttemptedAt;
            attempt.GmailMessageId = string.IsNullOrWhiteSpace(form.GmailMessageId) ? null : form.GmailMessageId.Trim()[..Math.Min(form.GmailMessageId.Trim().Length, 500)];
            attempt.SuccessRevisionKey = successRevisionKey;
            attempt.SuccessSnapshotKey = $"{attempt.ReportGroupId}:{attempt.PacificReportDate:yyyyMMdd}:{attempt.RevisionNumber}:{attempt.SnapshotHash}";
            attempt.FailureReason = null;
        }
        else
        {
            attempt.Status = EndOfDayFillSendStatuses.Failed;
            var failureReason = $"Administrator confirmed email was not delivered. {reason}";
            attempt.FailureReason = failureReason[..Math.Min(failureReason.Length, 2000)];
        }

        dbContext.EndOfDayFillSendReservations.Remove(reservation);
        dbContext.AuditLogs.Add(Audit(
            administrator.Id,
            confirmedSent ? "manual-confirmed-sent" : "manual-confirmed-not-sent",
            "end-of-day-fill-report-send",
            attempt.Id.ToString(),
            JsonSerializer.Serialize(new { Status = EndOfDayFillSendStatuses.Pending, reservation.CreatedAt }),
            JsonSerializer.Serialize(new
            {
                attempt.Status,
                attempt.SentAt,
                attempt.SuccessRevisionKey,
                Reason = reason,
                ResolvedByUserId = administrator.Id,
                ResolvedAt = businessTime.UtcNow,
                SentAtEvidence = confirmedSent ? "Original attempted timestamp; administrator verified Gmail Sent folder" : null
            })));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private async Task<User?> FindUserAsync(string email, CancellationToken ct) => await dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToLower() == email.Trim().ToLower(), ct);
    private static EndOfDayFillPendingAttemptViewModel PendingView(EndOfDayFillReportSend send, bool isStale) => new(
        send.Id,
        send.ReportGroupName,
        $"{send.SenderDisplayName} <{send.SenderEmail}>",
        send.AttemptedAt,
        send.Subject,
        string.Join(", ", JsonSerializer.Deserialize<List<string>>(send.RecipientsJson) ?? []),
        send.RevisionNumber,
        send.SnapshotHash,
        isStale);

    private static EndOfDayFillHistoryDetailViewModel ToHistoryDetail(EndOfDayFillReportSend send) => new()
    {
        Id = send.Id,
        GroupName = send.ReportGroupName,
        Facility = send.Facility,
        ReportDate = send.PacificReportDate,
        RevisionNumber = send.RevisionNumber,
        Sender = $"{send.SenderDisplayName} <{send.SenderEmail}>",
        Recipients = string.Join(", ", JsonSerializer.Deserialize<List<string>>(send.RecipientsJson) ?? []),
        Status = send.Status,
        SnapshotJson = send.SnapshotJson,
        Subject = send.Subject,
        HtmlBody = send.HtmlBody,
        TextBody = send.TextBody,
        GmailMessageId = send.GmailMessageId,
        AttemptedAt = send.AttemptedAt,
        SentAt = send.SentAt,
        FailureReason = send.FailureReason
    };
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
