using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IHarvestWatchService
{
    Task<HarvestWatchRoomViewModel> GetRoomDataAsync(int roomId, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<HarvestWatchOperationResult> DeployAsync(HarvestWatchDeployForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> RetireAsync(int roomId, long deploymentId, HarvestWatchRetireForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<HarvestWatchInboundResult> ProcessInboundReplyAsync(HarvestWatchInboundReply reply, CancellationToken cancellationToken);
    Task ProcessPendingOutboundEmailsAsync(IReadOnlyCollection<long>? deploymentIds, CancellationToken cancellationToken);
}

public sealed record HarvestWatchOperationResult(bool Success, string? Error, IReadOnlyList<long> DeploymentIds)
{
    public static HarvestWatchOperationResult Failed(string error) => new(false, error, []);
    public static HarvestWatchOperationResult Succeeded(IReadOnlyList<long> ids) => new(true, null, ids);
}

public sealed record HarvestWatchInboundReply(string MessageId, string SenderEmail, string Subject, string Body, DateTimeOffset ReceivedAt);
public sealed record HarvestWatchInboundResult(string Outcome, long? DeploymentId = null);

public sealed class HarvestWatchOutboundEmailWork
{
    public required string Type { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? MessageId { get; set; }
}

public interface IHarvestWatchEmailDispatcher
{
    Task<QcEmailSendResult> SendVerificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken);
    Task<QcEmailSendResult> SendErrorNotificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken);
}

public sealed class HarvestWatchEmailDispatcher(CropQcDbContext dbContext, IQcEmailSender emailSender) : IHarvestWatchEmailDispatcher
{
    public async Task<QcEmailSendResult> SendVerificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken)
    {
        var room = $"{deployment.WarehouseCodeSnapshot} {deployment.RoomCodeSnapshot}";
        var deployed = FormatPacific(deployment.DeployedAt);
        var marker = $"[HW:{deployment.Id}:{deployment.CorrelationToken}]";
        var subject = $"HarvestWatch Deployment - {room} - {deployment.HarvestWatchCode} {marker}";
        var text = $"""
            A HarvestWatch device has been deployed.

            Facility: {deployment.WarehouseCodeSnapshot}
            Room: {deployment.RoomCodeSnapshot}
            HarvestWatch Code: {deployment.HarvestWatchCode}
            Variety: {deployment.VarietySnapshot}
            Deployed: {deployed} Pacific
            Deployed By: {deployment.DeployedByUser.DisplayName} <{deployment.DeployerEmailSnapshot}>

            Reply to this email with one of:
            Working
            Error - Failed to Read
            Error - Low Reading

            {marker}
            """;
        var mailbox = await SystemMailboxAsync(cancellationToken);
        if (mailbox is null) return QcEmailSendResult.Failed("The dedicated HarvestWatch mailbox is unavailable.", reconnectRequired: true);
        return await emailSender.SendAsync(mailbox, new QcEmailMessage(
            mailbox.Email,
            HarvestWatchConstants.VerificationRecipient,
            mailbox.Email,
            subject,
            text,
            $"<pre>{System.Net.WebUtility.HtmlEncode(text)}</pre>",
            []), cancellationToken);
    }

    public async Task<QcEmailSendResult> SendErrorNotificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken)
    {
        var room = $"{deployment.WarehouseCodeSnapshot} {deployment.RoomCodeSnapshot}";
        var status = HarvestWatchDisplay.Status(deployment.Status);
        var text = $"""
            The HarvestWatch device deployed in {room} needs attention before the room is considered ready.

            HarvestWatch Code: {deployment.HarvestWatchCode}
            Status: {status}
            Variety: {deployment.VarietySnapshot}
            Deployed: {FormatPacific(deployment.DeployedAt)} Pacific
            Verified by: {deployment.VerifiedByEmail ?? HarvestWatchConstants.VerificationRecipient}

            Please check the HarvestWatch device and correct the issue.
            """;
        var mailbox = await SystemMailboxAsync(cancellationToken);
        if (mailbox is null) return QcEmailSendResult.Failed("The dedicated HarvestWatch mailbox is unavailable.", reconnectRequired: true);
        return await emailSender.SendAsync(mailbox, new QcEmailMessage(
            mailbox.Email,
            deployment.DeployerEmailSnapshot,
            mailbox.Email,
            $"HarvestWatch Error - {room} - {deployment.HarvestWatchCode}",
            text,
            $"<pre>{System.Net.WebUtility.HtmlEncode(text)}</pre>",
            []), cancellationToken);
    }

    private Task<User?> SystemMailboxAsync(CancellationToken cancellationToken) => dbContext.Users.SingleOrDefaultAsync(x => x.Email == HarvestWatchConstants.VerificationRecipient && x.IsActive, cancellationToken);

    private static string FormatPacific(DateTimeOffset value) => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(value, "Pacific Standard Time").ToString("MMMM d, yyyy 'at' h:mm tt");
}

public static class HarvestWatchConstants
{
    public const string VerificationRecipient = "wes@fruitandland.com";
    public const string MailboxAuthenticationScheme = "HarvestWatchMailboxGoogle";
    public static readonly IReadOnlySet<string> EligibleFacilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DH", "EBS" };
}

public static class HarvestWatchDisplay
{
    public static string Status(string status) => status switch
    {
        HarvestWatchStatuses.PendingVerification => "Pending Verification",
        HarvestWatchStatuses.ErrorFailedToRead => "Error - Failed to Read",
        HarvestWatchStatuses.ErrorLowReading => "Error - Low Reading",
        _ => status
    };
}

public static partial class HarvestWatchReplyParser
{
    private static readonly Regex FailedToRead = FailedToReadRegex();
    private static readonly Regex LowReading = LowReadingRegex();
    private static readonly Regex Working = WorkingRegex();
    private static readonly Regex Marker = MarkerRegex();

    public static string? ParseStatus(string? text)
    {
        var value = text ?? string.Empty;
        var failed = FailedToRead.IsMatch(value);
        var low = LowReading.IsMatch(value);
        var working = Working.IsMatch(value);
        return failed || low || working
            ? failed && !low && !working ? HarvestWatchStatuses.ErrorFailedToRead
            : low && !failed && !working ? HarvestWatchStatuses.ErrorLowReading
            : working && !failed && !low ? HarvestWatchStatuses.Working
            : null
            : null;
    }

    public static string ExtractNewReplyContent(string? text)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var result = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(">", StringComparison.Ordinal)
                || ReplySeparatorRegex().IsMatch(trimmed)
                || OriginalMessageSeparatorRegex().IsMatch(trimmed)) break;
            result.Add(line);
        }
        return string.Join("\n", result).Trim();
    }

    public static (long Id, string Token)? ParseCorrelation(string? subject, string? body)
    {
        var match = Marker.Match((subject ?? string.Empty) + "\n" + (body ?? string.Empty));
        return match.Success && long.TryParse(match.Groups["id"].Value, out var id)
            ? (id, match.Groups["token"].Value)
            : null;
    }

    [GeneratedRegex(@"(?im)^\s*(?:error\s*[-:]?\s*)?failed\s+to\s+read\s*[.!]?\s*$")]
    private static partial Regex FailedToReadRegex();
    [GeneratedRegex(@"(?im)^\s*(?:error\s*[-:]?\s*)?low\s+reading\s*[.!]?\s*$")]
    private static partial Regex LowReadingRegex();
    [GeneratedRegex(@"(?im)^\s*working\s*[.!]?\s*$")]
    private static partial Regex WorkingRegex();
    [GeneratedRegex(@"\[HW:(?<id>\d+):(?<token>[A-Za-z0-9_-]{16,64})\]", RegexOptions.IgnoreCase)]
    private static partial Regex MarkerRegex();
    [GeneratedRegex(@"^On .+ wrote:$", RegexOptions.IgnoreCase)]
    private static partial Regex ReplySeparatorRegex();
    [GeneratedRegex(@"^-{2,}\s*(Original Message|Forwarded message)\s*-{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex OriginalMessageSeparatorRegex();
}

public sealed class HarvestWatchService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IHarvestWatchEmailDispatcher emailDispatcher,
    IBusinessTimeService businessTime,
    ILogger<HarvestWatchService> logger) : IHarvestWatchService
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<HarvestWatchRoomViewModel> GetRoomDataAsync(int roomId, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var room = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == roomId, cancellationToken);
        if (room is null) return new HarvestWatchRoomViewModel { IneligibleReason = "Room not found." };
        var eligible = IsEligible(room);
        var deployments = await dbContext.HarvestWatchDeployments.AsNoTracking()
            .Where(x => x.RoomId == roomId && x.IsActive)
            .OrderByDescending(x => x.DeployedAt)
            .Select(x => new HarvestWatchDeploymentViewModel
            {
                Id = x.Id,
                Code = x.HarvestWatchCode,
                Status = x.Status,
                DeployedAt = x.DeployedAt,
                DeployedBy = x.DeployedByUser.DisplayName,
                VerifiedAt = x.VerifiedAt,
                VerificationEmailWarning = x.VerificationEmailSentAt == null ? x.VerificationEmailError : null
            }).ToListAsync(cancellationToken);
        return new HarvestWatchRoomViewModel
        {
            IsEligible = eligible,
            CanManage = eligible && RoomSealingService.CanManage(principal),
            IneligibleReason = eligible ? null : EligibilityMessage(room),
            Deployments = deployments,
            ActiveDeploymentCount = deployments.Count,
            Form = new HarvestWatchDeployForm { RoomId = roomId }
        };
    }

    public async Task<HarvestWatchOperationResult> DeployAsync(HarvestWatchDeployForm form, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(principal);
        var codes = (form.Codes ?? []).Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).ToList();
        if (codes.Count == 0) return HarvestWatchOperationResult.Failed("Enter at least one five-digit HarvestWatch code.");
        if (codes.Any(x => x.Length != 5 || !x.All(char.IsAsciiDigit))) return HarvestWatchOperationResult.Failed("Each HarvestWatch code must be exactly five digits.");
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count) return HarvestWatchOperationResult.Failed("Each HarvestWatch code can be entered only once.");

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var room = await dbContext.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.RoomId, cancellationToken);
        if (room is null) return HarvestWatchOperationResult.Failed("Room was not found.");
        if (!IsEligible(room)) return HarvestWatchOperationResult.Failed(EligibilityMessage(room));
        var activeCount = await dbContext.HarvestWatchDeployments.CountAsync(x => x.RoomId == room.Id && x.IsActive, cancellationToken);
        if (activeCount + codes.Count >= 4 && !form.ConfirmMoreThanThree)
            return HarvestWatchOperationResult.Failed("More than 3 HarvestWatch devices are being deployed in this room. Confirm that this is intentional.");
        var duplicate = await dbContext.HarvestWatchDeployments.Include(x => x.Room).Where(x => x.IsActive && codes.Contains(x.HarvestWatchCode)).Select(x => new { x.HarvestWatchCode, Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code }).FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null) return HarvestWatchOperationResult.Failed($"HarvestWatch code {duplicate.HarvestWatchCode} is already active in {duplicate.Room}.");

        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var actor = string.IsNullOrWhiteSpace(email) ? null : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
        if (actor is null) return HarvestWatchOperationResult.Failed("The current active user could not be resolved.");
        var now = businessTime.UtcNow;
        var varietySnapshot = await GetVarietySnapshotAsync(room, cancellationToken);
        var displayRoom = room.CropQcRoomName ?? room.DisplayName ?? room.Code;
        var deployments = codes.Select(code => new HarvestWatchDeployment
        {
            RoomId = room.Id,
            WarehouseId = room.WarehouseId,
            HarvestWatchCode = code,
            Status = HarvestWatchStatuses.PendingVerification,
            DeployedAt = now,
            DeployedByUserId = actor.Id,
            DeployerEmailSnapshot = actor.Email,
            WarehouseCodeSnapshot = room.Warehouse.Code,
            RoomCodeSnapshot = displayRoom,
            VarietySnapshot = varietySnapshot,
            CorrelationToken = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
        dbContext.HarvestWatchDeployments.AddRange(deployments);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var deployment in deployments)
        {
            dbContext.HarvestWatchStatusHistories.Add(new HarvestWatchStatusHistory
            {
                HarvestWatchDeploymentId = deployment.Id,
                NewStatus = deployment.Status,
                Source = "Deployment",
                ChangedAt = now,
                ChangedByEmail = actor.Email
            });
            QueueOutboundEmail(deployment, "Verification", now);
            dbContext.AuditLogs.Add(Audit(actor.Id, "HarvestWatchDeployed", deployment.Id, null, new { deployment.HarvestWatchCode, deployment.RoomCodeSnapshot, deployment.VarietySnapshot, deployment.Status }, now));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        await ProcessPendingOutboundEmailsAsync(deployments.Select(x => x.Id).ToList(), cancellationToken);
        return HarvestWatchOperationResult.Succeeded(deployments.Select(x => x.Id).ToList());
    }

    public async Task<string?> RetireAsync(int roomId, long deploymentId, HarvestWatchRetireForm form, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(principal);
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var actor = string.IsNullOrWhiteSpace(email) ? null : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
        if (actor is null) return "The current active user could not be resolved.";
        var deployment = await dbContext.HarvestWatchDeployments.SingleOrDefaultAsync(x => x.Id == deploymentId && x.RoomId == roomId && x.IsActive, cancellationToken);
        if (deployment is null) return "Active HarvestWatch deployment was not found.";
        var now = businessTime.UtcNow;
        var prior = deployment.Status;
        deployment.IsActive = false; deployment.Status = HarvestWatchStatuses.Removed; deployment.RemovedAt = now; deployment.RemovedByUserId = actor.Id; deployment.UpdatedAt = now;
        dbContext.HarvestWatchStatusHistories.Add(new HarvestWatchStatusHistory { HarvestWatchDeploymentId = deployment.Id, PreviousStatus = prior, NewStatus = deployment.Status, Source = "Removal", ChangedAt = now, ChangedByEmail = actor.Email, Note = form.Note?.Trim() });
        dbContext.AuditLogs.Add(Audit(actor.Id, "HarvestWatchRemoved", deployment.Id, new { Status = prior }, new { deployment.HarvestWatchCode, deployment.Status, Note = form.Note?.Trim() }, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<HarvestWatchInboundResult> ProcessInboundReplyAsync(HarvestWatchInboundReply reply, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reply.MessageId)) return new HarvestWatchInboundResult("IgnoredMissingMessageId");
        if (await dbContext.HarvestWatchInboundMessages.AnyAsync(x => x.GmailMessageId == reply.MessageId, cancellationToken)) return new HarvestWatchInboundResult("Duplicate");
        var sender = reply.SenderEmail.Trim().ToLowerInvariant();
        var correlation = HarvestWatchReplyParser.ParseCorrelation(reply.Subject, reply.Body);
        HarvestWatchDeployment? deployment = null;
        var outcome = "Ignored";
        if (!string.Equals(sender, HarvestWatchConstants.VerificationRecipient, StringComparison.OrdinalIgnoreCase)) outcome = "RejectedSender";
        else if (correlation is null) outcome = "IgnoredUncorrelated";
        else deployment = await dbContext.HarvestWatchDeployments.Include(x => x.DeployedByUser).SingleOrDefaultAsync(x => x.Id == correlation.Value.Id && x.CorrelationToken == correlation.Value.Token, cancellationToken);
        if (deployment is null && outcome == "Ignored") outcome = "IgnoredUnknownDeployment";
        if (deployment is not null && !deployment.IsActive) outcome = "IgnoredInactiveDeployment";
        var parsed = deployment is null || !deployment.IsActive ? null : HarvestWatchReplyParser.ParseStatus(HarvestWatchReplyParser.ExtractNewReplyContent(reply.Body));
        if (deployment is not null && deployment.IsActive && parsed is null) outcome = "IgnoredAmbiguousOrUnknownStatus";
        var now = businessTime.UtcNow;
        dbContext.HarvestWatchInboundMessages.Add(new HarvestWatchInboundMessage { GmailMessageId = reply.MessageId, HarvestWatchDeploymentId = deployment?.Id, SenderEmail = sender, Subject = Truncate(reply.Subject, 1000), BodyExcerpt = Truncate(reply.Body, 4000), ReceivedAt = reply.ReceivedAt, Outcome = outcome, ProcessedAt = now });
        if (deployment is not null && !deployment.IsActive)
            dbContext.AuditLogs.Add(Audit(deployment.DeployedByUserId, "HarvestWatchVerificationIgnored", deployment.Id, new { deployment.Status, deployment.IsActive }, new { Sender = sender, reply.MessageId, Outcome = outcome }, now));
        var enteringError = false;
        if (deployment is not null && parsed is not null && deployment.Status != parsed)
        {
            var old = deployment.Status;
            enteringError = HarvestWatchStatuses.IsError(parsed) && !HarvestWatchStatuses.IsError(old);
            deployment.Status = parsed; deployment.VerifiedAt = reply.ReceivedAt; deployment.VerifiedByEmail = sender; deployment.LastReplyMessageId = reply.MessageId; deployment.UpdatedAt = now;
            dbContext.HarvestWatchStatusHistories.Add(new HarvestWatchStatusHistory { HarvestWatchDeploymentId = deployment.Id, PreviousStatus = old, NewStatus = parsed, Source = "EmailReply", ChangedAt = now, InboundMessageId = reply.MessageId, ChangedByEmail = sender });
            dbContext.AuditLogs.Add(Audit(deployment.DeployedByUserId, "HarvestWatchVerificationReceived", deployment.Id, new { Status = old }, new { Status = parsed, Sender = sender, reply.MessageId }, now));
            if (enteringError) QueueOutboundEmail(deployment, "ErrorNotification", now);
            outcome = "StatusUpdated";
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (deployment is not null && enteringError) await ProcessPendingOutboundEmailsAsync([deployment.Id], cancellationToken);
        return new HarvestWatchInboundResult(outcome, deployment?.Id);
    }

    public async Task ProcessPendingOutboundEmailsAsync(IReadOnlyCollection<long>? deploymentIds, CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        var query = dbContext.HarvestWatchDeployments.Include(x => x.DeployedByUser).AsQueryable();
        if (deploymentIds is { Count: > 0 }) query = query.Where(x => deploymentIds.Contains(x.Id));
        var deployments = await query.OrderBy(x => x.Id).Take(100).ToListAsync(cancellationToken);
        if (deployments.Count == 0) return;
        var workRows = await dbContext.HarvestWatchStatusHistories
            .Where(x => deployments.Select(deployment => deployment.Id).Contains(x.HarvestWatchDeploymentId) && x.Source == "OutboundEmail")
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var pending = workRows.Select(row => new { Row = row, Work = DeserializeWork(row.Note) })
            .Where(x => x.Work is not null && IsDue(x.Work!, now)).Take(25).ToList();
        foreach (var item in pending)
        {
            var deployment = deployments.Single(x => x.Id == item.Row.HarvestWatchDeploymentId);
            if (item.Work!.Type == "ErrorNotification" && !HarvestWatchStatuses.IsError(deployment.Status))
            {
                item.Work.Status = "Superseded";
                item.Work.NextRetryAt = null;
                item.Work.LastError = "Superseded by a later non-error verification reply.";
                item.Row.Note = SerializeWork(item.Work);
                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }
            await DeliverAsync(deployment, item.Row, item.Work, cancellationToken);
        }
    }

    private async Task DeliverAsync(HarvestWatchDeployment deployment, HarvestWatchStatusHistory workRow, HarvestWatchOutboundEmailWork work, CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        var verification = work.Type == "Verification";
        var result = verification
            ? await emailDispatcher.SendVerificationAsync(deployment, cancellationToken)
            : await emailDispatcher.SendErrorNotificationAsync(deployment, cancellationToken);
        work.AttemptCount++;
        work.LastAttemptAt = now;
        work.Status = DeliveryStatus(result);
        work.LastError = result.Success ? null : Truncate(result.Error, 1000);
        work.NextRetryAt = result.Success || result.ReconnectRequired ? null : NextRetry(now, work.AttemptCount);
        if (result.Success) { work.SentAt = now; work.MessageId = result.MessageId; }
        if (verification)
        {
            deployment.VerificationEmailError = result.Success ? null : Truncate(result.Error, 1000);
            if (result.Success) { deployment.VerificationEmailSentAt = now; deployment.VerificationEmailMessageId = result.MessageId; dbContext.AuditLogs.Add(Audit(deployment.DeployedByUserId, "HarvestWatchVerificationEmailSent", deployment.Id, null, new { result.MessageId }, now)); }
        }
        else
        {
            if (result.Success) { deployment.ErrorNotificationSentAt = now; deployment.ErrorNotificationMessageId = result.MessageId; dbContext.AuditLogs.Add(Audit(deployment.DeployedByUserId, "HarvestWatchErrorNotificationSent", deployment.Id, null, new { deployment.Status, result.MessageId }, now)); }
        }
        workRow.Note = SerializeWork(work);
        deployment.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void QueueOutboundEmail(HarvestWatchDeployment deployment, string type, DateTimeOffset now) => dbContext.HarvestWatchStatusHistories.Add(new HarvestWatchStatusHistory { HarvestWatchDeploymentId = deployment.Id, NewStatus = deployment.Status, Source = "OutboundEmail", ChangedAt = now, Note = SerializeWork(new HarvestWatchOutboundEmailWork { Type = type, Status = "Pending", NextRetryAt = now }) });
    private static bool IsDue(HarvestWatchOutboundEmailWork work, DateTimeOffset now) => (work.Status == "Pending" || work.Status == "FailedRetryable") && (work.NextRetryAt == null || work.NextRetryAt <= now);
    private static string DeliveryStatus(QcEmailSendResult result) => result.Success ? "Sent" : result.ReconnectRequired ? "PermanentFailure" : "FailedRetryable";
    private static DateTimeOffset NextRetry(DateTimeOffset now, int attempts) => now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(attempts, 6))));
    private static HarvestWatchOutboundEmailWork? DeserializeWork(string? value) { try { return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<HarvestWatchOutboundEmailWork>(value, AuditJson); } catch (JsonException) { return null; } }
    private static string SerializeWork(HarvestWatchOutboundEmailWork work) => JsonSerializer.Serialize(work, AuditJson);

    private async Task<string> GetVarietySnapshotAsync(Room room, CancellationToken cancellationToken)
    {
        var varieties = (await ledger.GetSnapshotsAsync(room.WarehouseId, [room.Id], cancellationToken)).Where(x => x.CurrentBins > 0).Select(x => string.IsNullOrWhiteSpace(x.VarietyName) ? x.Variety : x.VarietyName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        return varieties.Count == 0 ? "No current variety recorded" : string.Join(", ", varieties);
    }

    private bool IsEligible(Room room) => HarvestWatchConstants.EligibleFacilities.Contains(room.Warehouse.Code) && RoomSealState.IsEffectivelySealed(room, businessTime.UtcNow);
    private string EligibilityMessage(Room room) => !HarvestWatchConstants.EligibleFacilities.Contains(room.Warehouse.Code) ? "HarvestWatch deployment is available only for DH and EBS rooms." : "HarvestWatch deployment is available only while the room is currently sealed.";
    private static void EnsureManagerOrAdmin(ClaimsPrincipal principal) { if (!RoomSealingService.CanManage(principal)) throw new UnauthorizedAccessException("Manager or Admin role is required to manage HarvestWatch deployments."); }
    private static AuditLog Audit(int userId, string action, long id, object? before, object? after, DateTimeOffset now) => new() { UserId = userId, Action = action, EntityName = nameof(HarvestWatchDeployment), EntityKey = id.ToString(), BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before, AuditJson), AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after, AuditJson), SourceApplication = "Web", CreatedAt = now };
    private static string Truncate(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Length <= maximum ? value.Trim() : value.Trim()[..maximum];
}
