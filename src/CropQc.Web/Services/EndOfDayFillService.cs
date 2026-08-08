using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IEndOfDayFillService
{
    Task<bool> HasActiveAssignmentAsync(string? email, CancellationToken cancellationToken);
    Task<bool> HasGroupAssignmentAsync(string? email, int groupId, CancellationToken cancellationToken);
    Task<EndOfDayFillPreviewViewModel> GetPreviewAsync(string? email, int? groupId, CancellationToken cancellationToken);
    Task<EndOfDayFillSendResult> SendAsync(string? email, EndOfDayFillSendForm form, CancellationToken cancellationToken);
    Task<EndOfDayFillHistoryPageViewModel> GetHistoryAsync(string? email, CancellationToken cancellationToken);
    Task<EndOfDayFillHistoryDetailViewModel?> GetHistoryDetailAsync(string? email, long id, CancellationToken cancellationToken);
}

public interface IEndOfDayFillInventorySource
{
    Task<IReadOnlyList<RoomLotSummaryViewModel>> GetCurrentLotsAsync(IReadOnlyCollection<int> roomIds, CancellationToken cancellationToken);
}

public sealed class EndOfDayFillInventorySource(IDashboardDataService dashboardDataService) : IEndOfDayFillInventorySource
{
    public Task<IReadOnlyList<RoomLotSummaryViewModel>> GetCurrentLotsAsync(IReadOnlyCollection<int> roomIds, CancellationToken cancellationToken) =>
        dashboardDataService.GetAuthoritativeCurrentRoomLotsAsync(roomIds, cancellationToken);
}

public static class EndOfDayFillRecoveryPolicy
{
    public const int AdvisoryLockNamespace = 1162102342;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CriticalSendTimeout = TimeSpan.FromMinutes(2);
    public const int FinalizationAttempts = 3;
}

public sealed class EndOfDayFillService(
    CropQcDbContext dbContext,
    IEndOfDayFillInventorySource inventorySource,
    IQcEmailSender emailSender,
    EmailOptions emailOptions,
    IBusinessTimeService businessTime,
    IDataProtectionProvider dataProtectionProvider,
    IFacilityContextService facilityContext,
    ILogger<EndOfDayFillService> logger) : IEndOfDayFillService
{
    private const string TokenPurpose = "CropQc.EndOfDayFill.Preview.v1";
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(TokenPurpose);

    public async Task<bool> HasActiveAssignmentAsync(string? email, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        return normalized.Length > 0 && await dbContext.EndOfDayFillUserGroupAssignments.AsNoTracking()
            .AnyAsync(x => x.User.IsActive && x.User.Email.ToLower() == normalized && x.ReportGroup.IsActive, cancellationToken);
    }

    public async Task<bool> HasGroupAssignmentAsync(string? email, int groupId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        return normalized.Length > 0 && await dbContext.EndOfDayFillUserGroupAssignments.AsNoTracking()
            .AnyAsync(x => x.User.IsActive && x.User.Email.ToLower() == normalized && x.ReportGroupId == groupId && x.ReportGroup.IsActive, cancellationToken);
    }

    public async Task<EndOfDayFillPreviewViewModel> GetPreviewAsync(string? email, int? groupId, CancellationToken cancellationToken)
    {
        var user = await FindAssignedUserAsync(email, cancellationToken);
        if (user is null)
        {
            return new EndOfDayFillPreviewViewModel
            {
                Issues = [new("unauthorized", "You are not assigned to an active End of Day Fill report group.")]
            };
        }

        var groups = user.UserAssignments
            .Where(x => x.ReportGroup.IsActive)
            .Select(x => new EndOfDayFillGroupOption(x.ReportGroupId, x.ReportGroup.Name, x.ReportGroup.Facility))
            .OrderBy(x => x.Name)
            .ToList();
        var selectedId = groupId ?? (groups.Count == 1 ? groups[0].Id : (int?)null);
        if (selectedId is null)
        {
            return new EndOfDayFillPreviewViewModel { Groups = groups };
        }

        if (!groups.Any(x => x.Id == selectedId.Value))
        {
            return new EndOfDayFillPreviewViewModel
            {
                Groups = groups,
                Issues = [new("unauthorized-group", "The selected report group is not assigned to your account.")]
            };
        }

        var build = await BuildSnapshotAsync(user, selectedId.Value, cancellationToken);
        var model = build.Model;
        model.Groups = groups;
        model.Form = new EndOfDayFillSendForm { GroupId = selectedId.Value };
        var reservation = await GetPendingReservationAsync(selectedId.Value, cancellationToken);
        if (reservation is not null)
        {
            model.PendingAttempt = PendingView(reservation);
            model.Issues = [.. model.Issues, new(
                model.PendingAttempt.IsStale ? "uncertain-send" : "send-in-progress",
                model.PendingAttempt.IsStale
                    ? "A previous email send has an uncertain outcome. Do not resend until an administrator verifies Gmail Sent and resolves it."
                    : "A previous email send is still being processed. Wait for it to finish, then refresh.")];
        }
        if (build.Snapshot is not null && model.Issues.Count == 0)
        {
            model.PreviewToken = protector.Protect(JsonSerializer.Serialize(
                new PreviewToken(user.Id, selectedId.Value, build.Snapshot.Hash), SnapshotJsonOptions));
            model.Form.PreviewToken = model.PreviewToken;
        }
        return model;
    }

    public async Task<EndOfDayFillSendResult> SendAsync(string? email, EndOfDayFillSendForm form, CancellationToken cancellationToken)
    {
        if (!form.PhysicalCountConfirmed)
        {
            return new(false, false, "Confirm that the physical count is complete before sending.");
        }

        var user = await FindAssignedUserAsync(email, cancellationToken);
        if (user is null || !user.UserAssignments.Any(x => x.ReportGroupId == form.GroupId && x.ReportGroup.IsActive))
        {
            return new(false, false, "You are not authorized to send the selected report group.");
        }

        PreviewToken token;
        try
        {
            token = JsonSerializer.Deserialize<PreviewToken>(protector.Unprotect(form.PreviewToken), SnapshotJsonOptions)
                ?? throw new InvalidOperationException("Missing preview token.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, true, "The preview could not be verified. Refresh the preview and confirm the physical count again.");
        }

        var build = await BuildSnapshotAsync(user, form.GroupId, cancellationToken);
        if (build.Snapshot is null || build.Model.Issues.Count > 0)
        {
            return new(false, true, build.Model.Issues.FirstOrDefault()?.Message ?? "The report preflight failed. Refresh and review the report.");
        }
        if (token.UserId != user.Id || token.GroupId != form.GroupId || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(token.SnapshotHash), Encoding.ASCII.GetBytes(build.Snapshot.Hash)))
        {
            return new(false, true, "Inventory or report configuration changed since review. Review the updated preview and confirm the physical count again.");
        }

        var nowPacific = businessTime.NowPacific;
        var reportDate = DateOnly.FromDateTime(nowPacific.DateTime);
        var reservationResult = await CreateReservationAsync(user, form.GroupId, build.Snapshot, nowPacific, reportDate, cancellationToken);
        if (reservationResult.Attempt is null)
        {
            return new(false, false, reservationResult.Message ?? "The send reservation could not be created safely. Refresh and try again.");
        }
        var attempt = reservationResult.Attempt;

        QcEmailSendResult send;
        using var criticalSection = new CancellationTokenSource(EndOfDayFillRecoveryPolicy.CriticalSendTimeout);
        try
        {
            send = await emailSender.SendAsync(user, new QcEmailMessage(
                user.Email,
                string.Join(", ", build.Snapshot.Recipients),
                user.Email,
                attempt!.Subject,
                attempt.TextBody,
                attempt.HtmlBody,
                []), criticalSection.Token);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "End of Day Fill Gmail dispatch outcome is uncertain for attempt {AttemptId}. The reservation remains fail-closed.", attempt!.Id);
            return new(false, false, "The email delivery result could not be safely finalized. Do not resend until an administrator verifies the pending attempt.", attempt.Id);
        }

        using var finalizationSection = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        if (!await FinalizeSendAsync(attempt!.Id, form.GroupId, reportDate, user.Id, send, finalizationSection.Token))
        {
            return new(false, false, "The email delivery result could not be safely finalized. Do not resend until an administrator verifies the pending attempt.", attempt.Id);
        }

        return send.Success
            ? new(true, false, $"{(attempt!.RevisionNumber == 0 ? "End of Day Fill Report" : $"REVISION {attempt.RevisionNumber}")} sent successfully.", attempt.Id)
            : new(false, false, send.Error ?? "The Gmail send failed.", attempt!.Id);
    }

    private async Task<ReservationResult> CreateReservationAsync(
        User user,
        int groupId,
        ReportSnapshot snapshot,
        DateTimeOffset nowPacific,
        DateOnly reportDate,
        CancellationToken cancellationToken)
    {
        for (var reservationAttempt = 1; reservationAttempt <= 3; reservationAttempt++)
        {
            try
            {
                dbContext.ChangeTracker.Clear();
                var isPostgreSql = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    isPostgreSql ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
                    cancellationToken);
                if (isPostgreSql)
                {
                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({EndOfDayFillRecoveryPolicy.AdvisoryLockNamespace}, {groupId})",
                        cancellationToken);
                }
                var existingReservation = await dbContext.EndOfDayFillSendReservations.AsNoTracking()
                    .Include(x => x.SendAttempt)
                    .SingleOrDefaultAsync(x => x.ReportGroupId == groupId, cancellationToken);
                if (existingReservation is not null)
                {
                    var stale = businessTime.UtcNow - existingReservation.CreatedAt >= EndOfDayFillRecoveryPolicy.StaleAfter;
                    return new(null, stale
                        ? "A previous send has an uncertain outcome. Do not resend until an administrator verifies the sender's Gmail Sent folder and resolves the pending attempt."
                        : "Another send for this report group is already in progress. Wait for it to finish, then refresh.");
                }

                var latestSuccess = await dbContext.EndOfDayFillReportSends.AsNoTracking()
                    .Where(x => x.ReportGroupId == groupId && x.PacificReportDate == reportDate && x.Status == EndOfDayFillSendStatuses.Succeeded)
                    .OrderByDescending(x => x.RevisionNumber)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latestSuccess?.SnapshotHash == snapshot.Hash)
                    return new(null, $"No report data has changed since the last successful End of Day Fill Report sent at {businessTime.FormatPacific(latestSuccess.SentAt)}. A revision cannot be sent.");

                var revision = latestSuccess is null ? 0 : latestSuccess.RevisionNumber + 1;
                var rendered = RenderEmail(snapshot, revision, nowPacific);
                var attempt = new EndOfDayFillReportSend
                {
                    ReportGroupId = groupId,
                    ReportGroupName = snapshot.GroupName,
                    Facility = snapshot.Facility,
                    PacificReportDate = reportDate,
                    RevisionNumber = revision,
                    SenderUserId = user.Id,
                    SenderEmail = user.Email,
                    SenderDisplayName = user.DisplayName,
                    RecipientsJson = JsonSerializer.Serialize(snapshot.Recipients, SnapshotJsonOptions),
                    PhysicalCountConfirmed = true,
                    SnapshotHash = snapshot.Hash,
                    SnapshotJson = snapshot.Json,
                    Subject = rendered.Subject,
                    HtmlBody = rendered.Html,
                    TextBody = rendered.Text,
                    Status = EndOfDayFillSendStatuses.Pending,
                    CreatedAt = businessTime.UtcNow,
                    AttemptedAt = businessTime.UtcNow
                };
                dbContext.EndOfDayFillReportSends.Add(attempt);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.EndOfDayFillSendReservations.Add(new EndOfDayFillSendReservation
                {
                    ReportGroupId = groupId,
                    PacificReportDate = reportDate,
                    RevisionNumber = revision,
                    SnapshotHash = snapshot.Hash,
                    SendAttemptId = attempt.Id,
                    CreatedAt = businessTime.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(attempt, null);
            }
            catch (Exception ex) when (IsSerializationConflict(ex) && reservationAttempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "End of Day Fill reservation serialization conflict for group {GroupId}; retry {Retry} of 3.", groupId, reservationAttempt + 1);
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(50 * reservationAttempt), cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                logger.LogInformation(ex, "Concurrent End of Day Fill reservation was rejected for group {GroupId}.", groupId);
                dbContext.ChangeTracker.Clear();
                return new(null, "Another send for this report group is already in progress. Wait for it to finish, then refresh.");
            }
            catch (Exception ex) when (IsSerializationConflict(ex))
            {
                logger.LogError(ex, "End of Day Fill reservation serialization conflict persisted after bounded retries for group {GroupId}.", groupId);
                dbContext.ChangeTracker.Clear();
                return new(null, "The send reservation could not be created safely because of concurrent activity. Refresh and try again.");
            }
        }
        return new(null, "The send reservation could not be created safely. Refresh and try again.");
    }

    private static bool IsSerializationConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.SerializationFailure or Npgsql.PostgresErrorCodes.DeadlockDetected }) return true;
        }
        return false;
    }

    private async Task<bool> FinalizeSendAsync(
        long attemptId,
        int groupId,
        DateOnly reportDate,
        int userId,
        QcEmailSendResult send,
        CancellationToken cancellationToken)
    {
        for (var finalizationAttempt = 1; finalizationAttempt <= EndOfDayFillRecoveryPolicy.FinalizationAttempts; finalizationAttempt++)
        {
            try
            {
                dbContext.ChangeTracker.Clear();
                var isPostgreSql = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    isPostgreSql ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
                    cancellationToken);
                if (isPostgreSql)
                {
                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({EndOfDayFillRecoveryPolicy.AdvisoryLockNamespace}, {groupId})",
                        cancellationToken);
                }
                var persisted = await dbContext.EndOfDayFillReportSends.SingleAsync(x => x.Id == attemptId, cancellationToken);
                var reservation = await dbContext.EndOfDayFillSendReservations.SingleOrDefaultAsync(
                    x => x.ReportGroupId == groupId && x.SendAttemptId == attemptId, cancellationToken);
                if (persisted.Status != EndOfDayFillSendStatuses.Pending || reservation is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    logger.LogCritical("End of Day Fill finalization found inconsistent persisted state for attempt {AttemptId}; status {Status}; reservation present {ReservationPresent}.", attemptId, persisted.Status, reservation is not null);
                    dbContext.ChangeTracker.Clear();
                    return false;
                }

                if (send.Success)
                {
                    persisted.Status = EndOfDayFillSendStatuses.Succeeded;
                    persisted.SentAt = businessTime.UtcNow;
                    persisted.GmailMessageId = send.MessageId;
                    persisted.SuccessRevisionKey = $"{groupId}:{reportDate:yyyyMMdd}:{persisted.RevisionNumber}";
                    persisted.SuccessSnapshotKey = $"{groupId}:{reportDate:yyyyMMdd}:{persisted.RevisionNumber}:{persisted.SnapshotHash}";
                }
                else
                {
                    persisted.Status = EndOfDayFillSendStatuses.Failed;
                    persisted.FailureReason = SafeFailure(send.Error);
                }
                dbContext.EndOfDayFillSendReservations.Remove(reservation);
                dbContext.AuditLogs.Add(BuildAudit(userId, send.Success ? "send-success" : "send-failure", "end-of-day-fill-report-send", persisted.Id.ToString(CultureInfo.InvariantCulture),
                    JsonSerializer.Serialize(new { persisted.ReportGroupId, persisted.PacificReportDate, persisted.RevisionNumber, persisted.SnapshotHash, persisted.Status }, SnapshotJsonOptions)));
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (Exception ex) when (finalizationAttempt < EndOfDayFillRecoveryPolicy.FinalizationAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "End of Day Fill finalization attempt {FinalizationAttempt} failed for send attempt {AttemptId}; retrying.", finalizationAttempt, attemptId);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * finalizationAttempt), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "End of Day Fill email result could not be finalized after {FinalizationAttempts} attempts for send attempt {AttemptId}. The reservation remains fail-closed.", finalizationAttempt, attemptId);
                dbContext.ChangeTracker.Clear();
                return false;
            }
        }
        dbContext.ChangeTracker.Clear();
        return false;
    }

    public async Task<EndOfDayFillHistoryPageViewModel> GetHistoryAsync(string? email, CancellationToken cancellationToken)
    {
        var user = await FindAssignedUserAsync(email, cancellationToken);
        if (user is null) return new();
        var groupIds = user.UserAssignments.Select(x => x.ReportGroupId).ToList();
        var sends = await dbContext.EndOfDayFillReportSends.AsNoTracking()
            .Where(x => groupIds.Contains(x.ReportGroupId))
            .OrderByDescending(x => x.AttemptedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return new EndOfDayFillHistoryPageViewModel
        {
            Sends = sends.Select(x => new EndOfDayFillHistoryItemViewModel(x.Id, x.ReportGroupName, x.Facility, x.PacificReportDate,
                x.RevisionNumber, x.SenderDisplayName, string.Join(", ", JsonSerializer.Deserialize<List<string>>(x.RecipientsJson, SnapshotJsonOptions) ?? []),
                x.Status, x.AttemptedAt, x.SentAt, x.FailureReason)).ToList()
        };
    }

    public async Task<EndOfDayFillHistoryDetailViewModel?> GetHistoryDetailAsync(string? email, long id, CancellationToken cancellationToken)
    {
        var user = await FindAssignedUserAsync(email, cancellationToken);
        if (user is null) return null;
        var groupIds = user.UserAssignments.Select(x => x.ReportGroupId).ToList();
        var send = await dbContext.EndOfDayFillReportSends.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && groupIds.Contains(x.ReportGroupId), cancellationToken);
        return send is null ? null : new EndOfDayFillHistoryDetailViewModel
        {
            Id = send.Id,
            GroupName = send.ReportGroupName,
            Facility = send.Facility,
            ReportDate = send.PacificReportDate,
            RevisionNumber = send.RevisionNumber,
            Sender = $"{send.SenderDisplayName} <{send.SenderEmail}>",
            Recipients = string.Join(", ", JsonSerializer.Deserialize<List<string>>(send.RecipientsJson, SnapshotJsonOptions) ?? []),
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
    }

    private async Task<User?> FindAssignedUserAsync(string? email, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        if (normalized.Length == 0) return null;
        return await dbContext.Users
            .Include(x => x.GoogleCredentials)
            .Include(x => x.UserAssignments)
            .ThenInclude(x => x.ReportGroup)
            .SingleOrDefaultAsync(x => x.IsActive && x.Email.ToLower() == normalized, cancellationToken);
    }

    private async Task<SnapshotBuild> BuildSnapshotAsync(User user, int groupId, CancellationToken cancellationToken)
    {
        var group = await dbContext.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Rooms).ThenInclude(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        var recipients = await dbContext.EndOfDayFillReportRecipients.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.NormalizedEmailAddress)
            .Select(x => x.EmailAddress)
            .ToListAsync(cancellationToken);
        var issues = new List<EndOfDayFillValidationIssue>();
        if (group is null || !group.IsActive)
        {
            issues.Add(new("inactive-group", "The selected report group is missing or inactive."));
            return new(new EndOfDayFillPreviewViewModel { SelectedGroupId = groupId, Issues = issues }, null);
        }
        if (group.Rooms.Count == 0) issues.Add(new("no-rooms", "Assign at least one room to this report group in Master Data."));
        foreach (var room in group.Rooms)
        {
            if (!facilityContext.GetOperatingCompanyFacility(room.Warehouse.Code, room.Warehouse.Name).Equals(group.Facility, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new("cross-facility-room", $"Room {room.Code} does not belong to {group.Facility}.", room.Id));
            }
        }
        if (recipients.Count == 0) issues.Add(new("no-recipients", "Add at least one active report recipient in Master Data."));
        foreach (var recipient in recipients.Where(x => !IsValidEmail(x))) issues.Add(new("invalid-recipient", $"Recipient {recipient} is invalid."));
        var gmailReady = string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase)
            && user.GoogleCredentials.Any(x => string.Equals(x.Provider, "Google", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(x.AccessTokenEncrypted) || !string.IsNullOrWhiteSpace(x.RefreshTokenEncrypted))
            && x.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(GmailScopes.Send, StringComparer.OrdinalIgnoreCase));
        if (!gmailReady) issues.Add(new("gmail", "Gmail permission is required. Reconnect Google/Gmail before sending."));

        IReadOnlyList<RoomLotSummaryViewModel> lots = [];
        if (group.Rooms.Count > 0 && issues.All(x => x.Code != "cross-facility-room"))
        {
            try
            {
                lots = await inventorySource.GetCurrentLotsAsync(group.Rooms.Select(x => x.Id).ToList(), cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                issues.Add(new("inventory-conflict", $"Authoritative room inventory could not be reconciled: {ex.Message}"));
            }
        }

        foreach (var lot in lots)
        {
            if (string.IsNullOrWhiteSpace(lot.CanonicalVarietyKey) || string.IsNullOrWhiteSpace(lot.CanonicalVarietyName))
                issues.Add(new("missing-variety", $"Room {lot.RoomCode} has positive inventory without a canonical variety.", lot.RoomId));
            if (string.IsNullOrWhiteSpace(lot.ProductionType))
                issues.Add(new("missing-production", $"Room {lot.RoomCode} has positive inventory without a production type.", lot.RoomId));
            if (lot.IsOrganic is null)
                issues.Add(new("missing-organic", $"Room {lot.RoomCode} has positive inventory without Organic/Conventional identity.", lot.RoomId));
            if (string.IsNullOrWhiteSpace(lot.GrowerNumber))
                issues.Add(new("missing-grower", $"Room {lot.RoomCode} has positive inventory without an authoritative grower number.", lot.RoomId));
        }

        var rooms = new List<EndOfDayFillRoomViewModel>();
        foreach (var room in group.Rooms.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code))
        {
            var roomLots = lots.Where(x => x.RoomId == room.Id && x.CurrentBins > 0).ToList();
            if (roomLots.Count == 0) continue;
            if (room.CapacityBins <= 0)
                issues.Add(new("invalid-capacity", $"Room {room.Code} is occupied but has no valid configured capacity.", room.Id));
            var varieties = roomLots
                .GroupBy(x => new { x.CanonicalVarietyKey, x.CanonicalVarietyName, x.ProductionType, Organic = x.IsOrganic == true, x.VarietyHexColor })
                .Select(v => new EndOfDayFillVarietyViewModel
                {
                    CanonicalKey = v.Key.CanonicalVarietyKey,
                    Name = v.Key.CanonicalVarietyName,
                    ProductionType = v.Key.ProductionType,
                    IsOrganic = v.Key.Organic,
                    HexColor = v.Key.VarietyHexColor,
                    Bins = v.Sum(x => x.CurrentBins),
                    Growers = v.GroupBy(x => new { x.GrowerNumber, x.GrowerName })
                        .Select(g => new EndOfDayFillGrowerViewModel(g.Key.GrowerNumber, g.Key.GrowerName, g.Sum(x => x.CurrentBins)))
                        .OrderBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.GrowerName, StringComparer.OrdinalIgnoreCase).ToList()
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProductionType, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.IsOrganic)
                .ToList();
            var currentBins = roomLots.Sum(x => x.CurrentBins);
            if (varieties.Sum(x => x.Growers.Sum(g => g.Bins)) != currentBins)
                issues.Add(new("room-reconciliation", $"Room {room.Code} does not reconcile to its variety/grower detail.", room.Id));
            rooms.Add(new EndOfDayFillRoomViewModel
            {
                RoomId = room.Id,
                RoomCode = room.Code,
                RoomName = room.DisplayName ?? room.Name,
                CurrentBins = currentBins,
                CapacityBins = room.CapacityBins,
                Varieties = varieties
            });
        }

        var model = new EndOfDayFillPreviewViewModel
        {
            SelectedGroupId = group.Id,
            GroupName = group.Name,
            Facility = group.Facility,
            Recipients = recipients,
            Rooms = rooms,
            Issues = issues,
            GmailReady = gmailReady
        };
        if (issues.Count > 0) return new(model, null);

        var normalized = new NormalizedSnapshot(
            group.Id,
            group.Name.Trim(),
            group.Facility,
            group.Rooms.Select(x => x.Id).Order().ToArray(),
            recipients.Select(NormalizeEmail).Order(StringComparer.Ordinal).ToArray(),
            rooms.Select(room => new NormalizedRoom(room.RoomId, room.RoomCode, room.RoomName, room.CapacityBins, room.CurrentBins,
                room.Varieties.Select(v => new NormalizedVariety(v.CanonicalKey, v.Name, v.ProductionType, v.IsOrganic,
                    v.Growers.Select(g => new NormalizedGrower(g.GrowerNumber.Trim(), g.GrowerName.Trim(), g.Bins)).ToArray())).ToArray())).ToArray());
        var json = JsonSerializer.Serialize(normalized, SnapshotJsonOptions);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new(model, new ReportSnapshot(group.Name, group.Facility, recipients, rooms, json, hash));
    }

    private static RenderedEmail RenderEmail(ReportSnapshot snapshot, int revision, DateTimeOffset nowPacific)
    {
        var encoder = HtmlEncoder.Default;
        var date = nowPacific.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var time = nowPacific.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var revisionPrefix = revision == 0 ? "" : $"REVISION {revision} - ";
        var subject = $"{revisionPrefix}End of Day Fill Report - {snapshot.Facility} - {date}";
        var html = new StringBuilder("<main style=\"font-family:Arial,sans-serif;max-width:720px;margin:auto\">");
        html.Append("<h1>End of Day Fill Report</h1>");
        if (revision > 0) html.Append($"<p><strong style=\"font-size:1.2em\">REVISION {revision}</strong></p>");
        html.Append($"<p><strong>{encoder.Encode(snapshot.GroupName)} — {encoder.Encode(snapshot.Facility)}</strong><br>End of Day Fill Report as of {date} — {time} Pacific</p>");
        var text = new StringBuilder("End of Day Fill Report\n");
        if (revision > 0) text.AppendLine($"REVISION {revision}");
        text.AppendLine($"{snapshot.GroupName} — {snapshot.Facility}").AppendLine($"End of Day Fill Report as of {date} — {time} Pacific").AppendLine();
        foreach (var room in snapshot.Rooms)
        {
            html.Append($"<section style=\"border:1px solid #bbb;border-radius:8px;padding:12px;margin:12px 0\"><h2 style=\"margin:0\">{encoder.Encode(room.RoomCode)} — {encoder.Encode(room.RoomName)}</h2><p><strong>{room.CurrentBins:N0} / {room.CapacityBins:N0} bins — {room.PercentFull:N1}% full</strong></p>");
            text.AppendLine($"{room.RoomCode} — {room.RoomName}").AppendLine($"{room.CurrentBins:N0} / {room.CapacityBins:N0} bins — {room.PercentFull:N1}% full");
            foreach (var variety in room.Varieties)
            {
                var identity = VarietyDisplayIdentity(variety);
                html.Append($"<h3 style=\"border-left:8px solid {encoder.Encode(variety.HexColor)};padding-left:8px\">{encoder.Encode(identity)} — {variety.Bins:N0} bins</h3><ul>");
                text.AppendLine($"{identity} — {variety.Bins:N0} bins");
                foreach (var grower in variety.Growers)
                {
                    var growerName = string.IsNullOrWhiteSpace(grower.GrowerName) ? "" : $" — {grower.GrowerName}";
                    html.Append($"<li>Grower {encoder.Encode(grower.GrowerNumber)}{encoder.Encode(growerName)} — {grower.Bins:N0} bins</li>");
                    text.AppendLine($"  Grower {grower.GrowerNumber}{growerName} — {grower.Bins:N0} bins");
                }
                html.Append("</ul>");
            }
            html.Append("</section>");
            text.AppendLine();
        }
        html.Append("</main>");
        return new(subject, html.ToString(), text.ToString());
    }

    private static string VarietyDisplayIdentity(EndOfDayFillVarietyViewModel variety)
    {
        var organicLabel = variety.IsOrganic ? "Organic" : "Conventional";
        var productionType = variety.ProductionType.Trim();
        return string.IsNullOrWhiteSpace(productionType)
            || productionType.Equals(organicLabel, StringComparison.OrdinalIgnoreCase)
            ? $"{variety.Name} — {organicLabel}"
            : $"{variety.Name} — {productionType} — {organicLabel}";
    }

    private async Task<EndOfDayFillSendReservation?> GetPendingReservationAsync(int groupId, CancellationToken cancellationToken) =>
        await dbContext.EndOfDayFillSendReservations.AsNoTracking()
            .Include(x => x.SendAttempt)
            .SingleOrDefaultAsync(x => x.ReportGroupId == groupId, cancellationToken);

    private EndOfDayFillPendingAttemptViewModel PendingView(EndOfDayFillSendReservation reservation)
    {
        var send = reservation.SendAttempt;
        return new(
            send.Id,
            send.ReportGroupName,
            $"{send.SenderDisplayName} <{send.SenderEmail}>",
            send.AttemptedAt,
            send.Subject,
            string.Join(", ", JsonSerializer.Deserialize<List<string>>(send.RecipientsJson, SnapshotJsonOptions) ?? []),
            send.RevisionNumber,
            send.SnapshotHash,
            businessTime.UtcNow - reservation.CreatedAt >= EndOfDayFillRecoveryPolicy.StaleAfter);
    }

    private static bool IsValidEmail(string email)
    {
        try { return new MailAddress(email).Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }

    private static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? "";
    private static string SafeFailure(string? error) => string.IsNullOrWhiteSpace(error) ? "Email send failed." : error.Trim()[..Math.Min(error.Trim().Length, 2000)];
    private static AuditLog BuildAudit(int userId, string action, string entity, string key, string after) => new()
    {
        UserId = userId,
        Action = action,
        EntityName = entity,
        EntityKey = key,
        AfterValuesJson = after,
        SourceApplication = "CropQc.Web",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed record PreviewToken(int UserId, int GroupId, string SnapshotHash);
    private sealed record ReservationResult(EndOfDayFillReportSend? Attempt, string? Message);
    private sealed record SnapshotBuild(EndOfDayFillPreviewViewModel Model, ReportSnapshot? Snapshot);
    private sealed record ReportSnapshot(string GroupName, string Facility, IReadOnlyList<string> Recipients, IReadOnlyList<EndOfDayFillRoomViewModel> Rooms, string Json, string Hash);
    private sealed record RenderedEmail(string Subject, string Html, string Text);
    private sealed record NormalizedSnapshot(int GroupId, string GroupName, string Facility, int[] ConfiguredRoomIds, string[] Recipients, NormalizedRoom[] Rooms);
    private sealed record NormalizedRoom(int RoomId, string RoomCode, string RoomName, int CapacityBins, int CurrentBins, NormalizedVariety[] Varieties);
    private sealed record NormalizedVariety(string CanonicalKey, string Name, string ProductionType, bool IsOrganic, NormalizedGrower[] Growers);
    private sealed record NormalizedGrower(string GrowerNumber, string GrowerName, int Bins);
}
