using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class EndOfDayFillWarehouseConfigurationSyncConstants
{
    public const string CommandName = "--sync-end-of-day-fill-warehouses";
}

public sealed record EndOfDayFillWarehouseConfigurationSyncRequest(
    bool Apply,
    bool ConfirmProduction,
    bool ConfirmDisposableRestore,
    string RequestedBy,
    string Reason,
    string? ExpectedTargetFingerprint,
    string? ExpectedProtectedFingerprint);

public sealed record EndOfDayFillWarehouseConfigurationGroupPlan(
    string Label,
    int WarehouseId,
    string StoredWarehouseCode,
    string Name,
    string Facility,
    IReadOnlyList<int> RoomIds,
    IReadOnlyList<string> RoomCodes,
    IReadOnlyList<string> AssignedUsers);

public sealed record EndOfDayFillWarehouseConfigurationExistingGroup(
    int Id,
    string Name,
    int WarehouseId,
    string Facility,
    bool IsActive,
    IReadOnlyList<int> RoomIds,
    IReadOnlyList<string> RoomCodes,
    IReadOnlyList<string> AssignedUsers,
    int HistoricalSendCount);

public sealed record EndOfDayFillWarehouseConfigurationPreflight(
    string State,
    string TargetFingerprint,
    string ProtectedFingerprint,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<EndOfDayFillWarehouseConfigurationExistingGroup> ExistingGroups,
    IReadOnlyList<EndOfDayFillWarehouseConfigurationGroupPlan> DesiredGroups,
    int RecipientCount,
    int HistoricalSendCount);

public sealed record EndOfDayFillWarehouseConfigurationSyncResult(
    bool Success,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    EndOfDayFillWarehouseConfigurationPreflight Preflight,
    EndOfDayFillWarehouseConfigurationPreflight? FinalState = null);

public interface IEndOfDayFillWarehouseConfigurationSyncService
{
    Task<EndOfDayFillWarehouseConfigurationSyncResult> RunAsync(
        EndOfDayFillWarehouseConfigurationSyncRequest request,
        CancellationToken cancellationToken);
}

public sealed class EndOfDayFillWarehouseConfigurationSyncService(
    CropQcDbContext dbContext,
    IBusinessTimeService businessTime) : IEndOfDayFillWarehouseConfigurationSyncService
{
    private static readonly DesiredWarehouse[] Desired =
    [
        new("WP", 4, "WP", "WP", "WP End of Day Fill"),
        new("MCD", 3, "McDougall", "WP", "MCD End of Day Fill"),
        new("DH", 2, "DH", "WP", "DH End of Day Fill"),
        new("EBS", 1, "EBS", "EBS", "EBS End of Day Fill")
    ];

    public async Task<EndOfDayFillWarehouseConfigurationSyncResult> RunAsync(
        EndOfDayFillWarehouseConfigurationSyncRequest request,
        CancellationToken cancellationToken)
    {
        var preflight = await BuildPreflightAsync(cancellationToken);
        if (preflight.State == "Conflict")
            return new(false, false, false, "Configuration conflicts must be reviewed before applying.", preflight);
        if (preflight.State == "AlreadyApplied")
            return new(true, false, true, "The four warehouse report groups are already configured exactly; no writes were made.", preflight, preflight);
        if (!request.Apply)
            return new(true, false, false, "Dry run is Ready. Re-run with explicit confirmation and both fresh fingerprints to apply.", preflight);
        if (!request.ConfirmProduction && !request.ConfirmDisposableRestore)
            return new(false, false, false, "Apply requires explicit production or disposable-restore confirmation.", preflight);
        if (request.ConfirmProduction && request.ConfirmDisposableRestore)
            return new(false, false, false, "Choose exactly one apply environment confirmation.", preflight);
        if (request.Reason.Trim().Length < 10)
            return new(false, false, false, "Apply requires a reason of at least 10 characters.", preflight);
        if (!FixedEquals(request.ExpectedTargetFingerprint, preflight.TargetFingerprint)
            || !FixedEquals(request.ExpectedProtectedFingerprint, preflight.ProtectedFingerprint))
            return new(false, false, false, "Fresh target and protected fingerprints are required and must match the dry run.", preflight);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var locked = await BuildPreflightAsync(cancellationToken);
        if (locked.State != "Ready"
            || !FixedEquals(locked.TargetFingerprint, preflight.TargetFingerprint)
            || !FixedEquals(locked.ProtectedFingerprint, preflight.ProtectedFingerprint))
            return new(false, false, false, "Configuration changed after dry run; the transaction was not applied.", locked);

        var administrator = await dbContext.Users.SingleOrDefaultAsync(
            x => x.IsActive && x.Email.ToLower() == request.RequestedBy.Trim().ToLower(),
            cancellationToken);
        if (administrator is null)
            return new(false, false, false, "The requested-by administrator could not be resolved.", locked);

        var groups = await dbContext.EndOfDayFillReportGroups
            .Include(x => x.Rooms)
            .Include(x => x.UserAssignments)
            .ToListAsync(cancellationToken);
        var wp = groups.Single(x => x.Id == 1);
        var ebs = groups.Single(x => x.Id == 2);
        var wpUserIds = wp.UserAssignments.Select(x => x.UserId).Order().ToArray();
        var ebsUserIds = ebs.UserAssignments.Select(x => x.UserId).Order().ToArray();
        var desiredNames = Desired.Select(x => x.GroupName).ToHashSet(StringComparer.Ordinal);
        var targetGroupIds = groups.Where(x => x.Id is 1 or 2 || desiredNames.Contains(x.Name)).Select(x => x.Id).ToArray();
        var includedRooms = await dbContext.Rooms
            .Where(x => x.EndOfDayFillReportGroupId != null
                && targetGroupIds.Contains(x.EndOfDayFillReportGroupId.Value))
            .ToListAsync(cancellationToken);

        foreach (var desired in Desired)
        {
            var group = desired.Label switch
            {
                "WP" => wp,
                "EBS" => ebs,
                _ => groups.SingleOrDefault(x => x.Name == desired.GroupName)
            };
            if (group is null)
            {
                group = new EndOfDayFillReportGroup
                {
                    Name = desired.GroupName,
                    WarehouseId = desired.WarehouseId,
                    Facility = desired.Facility,
                    IsActive = true,
                    CreatedAt = businessTime.UtcNow,
                    UpdatedAt = businessTime.UtcNow
                };
                dbContext.EndOfDayFillReportGroups.Add(group);
                await dbContext.SaveChangesAsync(cancellationToken);
                groups.Add(group);
            }
            group.Name = desired.GroupName;
            group.WarehouseId = desired.WarehouseId;
            group.Facility = desired.Facility;
            group.IsActive = true;
            group.UpdatedAt = businessTime.UtcNow;

            foreach (var room in includedRooms.Where(x => x.WarehouseId == desired.WarehouseId))
                room.EndOfDayFillReportGroupId = group.Id;

            var desiredUserIds = desired.Label == "EBS" ? ebsUserIds : wpUserIds;
            dbContext.EndOfDayFillUserGroupAssignments.RemoveRange(
                group.UserAssignments.Where(x => !desiredUserIds.Contains(x.UserId)));
            foreach (var userId in desiredUserIds.Where(id => group.UserAssignments.All(x => x.UserId != id)))
                dbContext.EndOfDayFillUserGroupAssignments.Add(new EndOfDayFillUserGroupAssignment
                {
                    UserId = userId,
                    ReportGroupId = group.Id,
                    CreatedAt = businessTime.UtcNow,
                    CreatedByUserId = administrator.Id
                });
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = administrator.Id,
            Action = "apply-warehouse-scope",
            EntityName = "end-of-day-fill-warehouse-configuration",
            EntityKey = "WP|MCD|DH|EBS",
            BeforeValuesJson = JsonSerializer.Serialize(new
            {
                preflight.TargetFingerprint,
                preflight.ProtectedFingerprint,
                preflight.DesiredGroups,
                Environment = request.ConfirmProduction ? "Production" : "DisposableRestore"
            }),
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                Reason = request.Reason.Trim(),
                RequestedBy = administrator.Email,
                WarehouseMasterRenames = 0,
                HistoricalSendWrites = 0
            }),
            SourceApplication = "CropQc.Web",
            CreatedAt = businessTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var protectedAfter = await ProtectedFingerprintAsync(cancellationToken);
        if (!FixedEquals(preflight.ProtectedFingerprint, protectedAfter))
            throw new InvalidOperationException("Protected historical sends or global recipients changed during configuration apply.");
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var finalState = await BuildPreflightAsync(cancellationToken);
        if (finalState.State != "AlreadyApplied")
            return new(false, true, false, "Apply committed but exact final verification failed.", preflight, finalState);
        return new(true, true, false, "Four warehouse report groups were configured and verified.", preflight, finalState);
    }

    private async Task<EndOfDayFillWarehouseConfigurationPreflight> BuildPreflightAsync(CancellationToken cancellationToken)
    {
        var conflicts = new List<string>();
        var warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var desired in Desired)
        {
            var warehouse = warehouses.SingleOrDefault(x => x.Id == desired.WarehouseId);
            if (warehouse is null || !warehouse.Code.Equals(desired.StoredCode, StringComparison.Ordinal))
                conflicts.Add($"Warehouse {desired.WarehouseId} must have exact stored code {desired.StoredCode}.");
        }

        var groups = await dbContext.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Rooms)
            .Include(x => x.UserAssignments).ThenInclude(x => x.User)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var wp = groups.SingleOrDefault(x => x.Id == 1 && x.Name == "WP End of Day Fill" && x.Facility == "WP");
        var ebs = groups.SingleOrDefault(x => x.Id == 2 && x.Name == "EBS End of Day Fill" && x.Facility == "EBS");
        if (wp is null) conflicts.Add("Existing group 1 must be the reviewed WP End of Day Fill group.");
        if (ebs is null) conflicts.Add("Existing group 2 must be the reviewed EBS End of Day Fill group.");
        var desiredNames = Desired.Select(x => x.GroupName).ToHashSet(StringComparer.Ordinal);
        foreach (var group in groups.Where(x => x.IsActive && !desiredNames.Contains(x.Name)))
            conflicts.Add($"Unexpected active report group {group.Id} / {group.Name} requires review.");
        foreach (var desired in Desired)
        {
            var matches = groups.Count(x => x.Name == desired.GroupName);
            if (matches > 1) conflicts.Add($"Duplicate {desired.GroupName} definitions require review.");
        }

        var targetGroupIds = groups.Where(x => desiredNames.Contains(x.Name) || x.Id is 1 or 2).Select(x => x.Id).ToHashSet();
        var includedRooms = await dbContext.Rooms.AsNoTracking()
            .Where(x => x.EndOfDayFillReportGroupId != null && targetGroupIds.Contains(x.EndOfDayFillReportGroupId.Value))
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.SortOrder).ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);
        foreach (var room in includedRooms.Where(x => Desired.All(d => d.WarehouseId != x.WarehouseId)))
            conflicts.Add($"Included room {room.Id} / {room.Code} belongs to an unreviewed warehouse {room.WarehouseId}.");

        var wpUsers = wp?.UserAssignments.Select(x => x.User.Email).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var ebsUsers = ebs?.UserAssignments.Select(x => x.User.Email).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var plans = Desired.Select(desired =>
        {
            var rooms = includedRooms.Where(x => x.WarehouseId == desired.WarehouseId).ToList();
            var users = desired.Label == "EBS" ? ebsUsers : wpUsers;
            return new EndOfDayFillWarehouseConfigurationGroupPlan(
                desired.Label,
                desired.WarehouseId,
                desired.StoredCode,
                desired.GroupName,
                desired.Facility,
                rooms.Select(x => x.Id).ToArray(),
                rooms.Select(x => x.Code).ToArray(),
                users);
        }).ToList();
        if (plans.Any(x => x.RoomIds.Count == 0)) conflicts.Add("Every reviewed warehouse must retain at least one explicitly included room.");

        var exact = conflicts.Count == 0 && Desired.All(desired =>
        {
            var group = groups.SingleOrDefault(x => x.Name == desired.GroupName);
            if (group is null || !group.IsActive || group.WarehouseId != desired.WarehouseId || group.Facility != desired.Facility) return false;
            var plan = plans.Single(x => x.Label == desired.Label);
            if (!group.Rooms.Select(x => x.Id).Order().SequenceEqual(plan.RoomIds.Order())) return false;
            return group.UserAssignments.Select(x => x.User.Email).Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(plan.AssignedUsers.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }) && groups.Count(x => x.IsActive) == 4;

        var recipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking().CountAsync(cancellationToken);
        var sends = await dbContext.EndOfDayFillReportSends.AsNoTracking().CountAsync(cancellationToken);
        var sendsByGroup = await dbContext.EndOfDayFillReportSends.AsNoTracking()
            .GroupBy(x => x.ReportGroupId)
            .Select(x => new { ReportGroupId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ReportGroupId, x => x.Count, cancellationToken);
        var existingGroups = groups.Select(x => new EndOfDayFillWarehouseConfigurationExistingGroup(
            x.Id,
            x.Name,
            x.WarehouseId,
            x.Facility,
            x.IsActive,
            x.Rooms.Select(r => r.Id).Order().ToArray(),
            x.Rooms.OrderBy(r => r.Code).Select(r => r.Code).ToArray(),
            x.UserAssignments.Select(a => a.User.Email).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            sendsByGroup.GetValueOrDefault(x.Id))).ToList();
        return new(
            conflicts.Count > 0 ? "Conflict" : exact ? "AlreadyApplied" : "Ready",
            Fingerprint(new
            {
                Warehouses = warehouses.Select(x => new { x.Id, x.Code, x.Name, x.IsActive }),
                Groups = groups.Select(x => new { x.Id, x.Name, x.WarehouseId, x.Facility, x.IsActive, Rooms = x.Rooms.Select(r => r.Id).Order(), Users = x.UserAssignments.Select(a => a.UserId).Order() }),
                Plans = plans
            }),
            await ProtectedFingerprintAsync(cancellationToken),
            conflicts,
            existingGroups,
            plans,
            recipients,
            sends);
    }

    private async Task<string> ProtectedFingerprintAsync(CancellationToken cancellationToken)
    {
        var recipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.EmailAddress, x.NormalizedEmailAddress, x.IsActive, x.SortOrder, x.CreatedAt, x.UpdatedAt, x.UpdatedByUserId })
            .ToListAsync(cancellationToken);
        var sends = await dbContext.EndOfDayFillReportSends.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.ReportGroupId,
                x.ReportGroupName,
                x.Facility,
                x.PacificReportDate,
                x.RevisionNumber,
                x.SenderUserId,
                x.SenderEmail,
                x.SenderDisplayName,
                x.RecipientsJson,
                x.PhysicalCountConfirmed,
                x.SnapshotHash,
                x.SnapshotJson,
                x.SuccessRevisionKey,
                x.SuccessSnapshotKey,
                x.Subject,
                x.HtmlBody,
                x.TextBody,
                x.Status,
                x.FailureReason,
                x.GmailMessageId,
                x.CreatedAt,
                x.AttemptedAt,
                x.SentAt
            }).ToListAsync(cancellationToken);
        return Fingerprint(new { Recipients = recipients, Sends = sends });
    }

    private static string Fingerprint<T>(T value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static bool FixedEquals(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || left.Length != right.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left.ToLowerInvariant()), Encoding.ASCII.GetBytes(right));
    }

    private sealed record DesiredWarehouse(string Label, int WarehouseId, string StoredCode, string Facility, string GroupName);
}
