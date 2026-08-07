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

public sealed class EndOfDayFillService(
    CropQcDbContext dbContext,
    IEndOfDayFillInventorySource inventorySource,
    IQcEmailSender emailSender,
    EmailOptions emailOptions,
    IBusinessTimeService businessTime,
    IDataProtectionProvider dataProtectionProvider,
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
        EndOfDayFillReportSend? attempt = null;
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (await dbContext.EndOfDayFillSendReservations.AnyAsync(x => x.ReportGroupId == form.GroupId, cancellationToken))
            {
                return new(false, false, "Another send for this report group is already in progress. Wait for it to finish, then refresh.");
            }

            var latestSuccess = await dbContext.EndOfDayFillReportSends.AsNoTracking()
                .Where(x => x.ReportGroupId == form.GroupId
                    && x.PacificReportDate == reportDate
                    && x.Status == EndOfDayFillSendStatuses.Succeeded)
                .OrderByDescending(x => x.RevisionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestSuccess?.SnapshotHash == build.Snapshot.Hash)
            {
                return new(false, false,
                    $"No report data has changed since the last successful End of Day Fill Report sent at {businessTime.FormatPacific(latestSuccess.SentAt)}. A revision cannot be sent.");
            }

            var revision = latestSuccess is null ? 0 : latestSuccess.RevisionNumber + 1;
            var rendered = RenderEmail(build.Snapshot, revision, nowPacific);
            attempt = new EndOfDayFillReportSend
            {
                ReportGroupId = form.GroupId,
                ReportGroupName = build.Snapshot.GroupName,
                Facility = build.Snapshot.Facility,
                PacificReportDate = reportDate,
                RevisionNumber = revision,
                SenderUserId = user.Id,
                SenderEmail = user.Email,
                SenderDisplayName = user.DisplayName,
                RecipientsJson = JsonSerializer.Serialize(build.Snapshot.Recipients, SnapshotJsonOptions),
                PhysicalCountConfirmed = true,
                SnapshotHash = build.Snapshot.Hash,
                SnapshotJson = build.Snapshot.Json,
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
                ReportGroupId = form.GroupId,
                PacificReportDate = reportDate,
                RevisionNumber = revision,
                SnapshotHash = build.Snapshot.Hash,
                SendAttemptId = attempt.Id,
                CreatedAt = businessTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(ex, "Concurrent End of Day Fill reservation was rejected for group {GroupId}.", form.GroupId);
            dbContext.ChangeTracker.Clear();
            return new(false, false, "Another send for this report group is already in progress. Wait for it to finish, then refresh.");
        }

        QcEmailSendResult send;
        try
        {
            send = await emailSender.SendAsync(user, new QcEmailMessage(
                user.Email,
                string.Join(", ", build.Snapshot.Recipients),
                user.Email,
                attempt!.Subject,
                attempt.TextBody,
                attempt.HtmlBody,
                []), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "End of Day Fill Gmail send failed for attempt {AttemptId}.", attempt!.Id);
            send = QcEmailSendResult.Failed("The Gmail send failed unexpectedly.");
        }

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            var persisted = await dbContext.EndOfDayFillReportSends.SingleAsync(x => x.Id == attempt!.Id, cancellationToken);
            var reservation = await dbContext.EndOfDayFillSendReservations.SingleOrDefaultAsync(x => x.ReportGroupId == form.GroupId, cancellationToken);
            if (send.Success)
            {
                persisted.Status = EndOfDayFillSendStatuses.Succeeded;
                persisted.SentAt = businessTime.UtcNow;
                persisted.GmailMessageId = send.MessageId;
                persisted.SuccessRevisionKey = $"{form.GroupId}:{reportDate:yyyyMMdd}:{persisted.RevisionNumber}";
                persisted.SuccessSnapshotKey = $"{form.GroupId}:{reportDate:yyyyMMdd}:{persisted.RevisionNumber}:{persisted.SnapshotHash}";
            }
            else
            {
                persisted.Status = EndOfDayFillSendStatuses.Failed;
                persisted.FailureReason = SafeFailure(send.Error);
            }
            if (reservation is not null) dbContext.EndOfDayFillSendReservations.Remove(reservation);
            dbContext.AuditLogs.Add(BuildAudit(user.Id, send.Success ? "send-success" : "send-failure", "end-of-day-fill-report-send", persisted.Id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new { persisted.ReportGroupId, persisted.PacificReportDate, persisted.RevisionNumber, persisted.SnapshotHash, persisted.Status }, SnapshotJsonOptions)));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return send.Success
            ? new(true, false, $"{(attempt!.RevisionNumber == 0 ? "End of Day Fill Report" : $"REVISION {attempt.RevisionNumber}")} sent successfully.", attempt.Id)
            : new(false, false, send.Error ?? "The Gmail send failed.", attempt!.Id);
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
            .Include(x => x.Rooms).ThenInclude(x => x.Room).ThenInclude(x => x.Warehouse)
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
        foreach (var membership in group.Rooms)
        {
            if (!RoomBelongsToFacility(membership.Room, group.Facility))
            {
                issues.Add(new("cross-facility-room", $"Room {membership.Room.Code} does not belong to {group.Facility}.", membership.RoomId));
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
                lots = await inventorySource.GetCurrentLotsAsync(group.Rooms.Select(x => x.RoomId).ToList(), cancellationToken);
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
        foreach (var membership in group.Rooms.OrderBy(x => x.Room.SortOrder).ThenBy(x => x.Room.Code))
        {
            var roomLots = lots.Where(x => x.RoomId == membership.RoomId && x.CurrentBins > 0).ToList();
            if (roomLots.Count == 0) continue;
            if (membership.Room.CapacityBins <= 0)
                issues.Add(new("invalid-capacity", $"Room {membership.Room.Code} is occupied but has no valid configured capacity.", membership.RoomId));
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
                issues.Add(new("room-reconciliation", $"Room {membership.Room.Code} does not reconcile to its variety/grower detail.", membership.RoomId));
            rooms.Add(new EndOfDayFillRoomViewModel
            {
                RoomId = membership.RoomId,
                RoomCode = membership.Room.Code,
                RoomName = membership.Room.DisplayName ?? membership.Room.Name,
                CurrentBins = currentBins,
                CapacityBins = membership.Room.CapacityBins,
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
            group.Rooms.Select(x => x.RoomId).Order().ToArray(),
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
        var revisionPrefix = revision == 0 ? "" : $"REVISION {revision} — ";
        var subject = $"{revisionPrefix}End of Day Fill Report — {snapshot.Facility} — {date}";
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
                var identity = $"{variety.Name} — {variety.ProductionType} — {(variety.IsOrganic ? "Organic" : "Conventional")}";
                html.Append($"<h3 style=\"border-left:8px solid {encoder.Encode(variety.HexColor)};padding-left:8px\">{encoder.Encode(identity)} — {variety.Bins:N0} bins</h3><ul>");
                text.AppendLine(identity);
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

    private static bool RoomBelongsToFacility(Room room, string facility)
    {
        var warehouse = $"{room.Warehouse.Code} {room.Warehouse.Name}";
        var actual = warehouse.Contains("EBS", StringComparison.OrdinalIgnoreCase) || warehouse.Contains("Earl Brown", StringComparison.OrdinalIgnoreCase)
            ? "EBS"
            : warehouse.Contains("WP", StringComparison.OrdinalIgnoreCase)
                || warehouse.Contains("Windy Point", StringComparison.OrdinalIgnoreCase)
                || warehouse.Contains("MCD", StringComparison.OrdinalIgnoreCase)
                || warehouse.Contains("McDougall", StringComparison.OrdinalIgnoreCase)
                || warehouse.Contains("DH", StringComparison.OrdinalIgnoreCase)
                    ? "WP"
                    : "Other";
        return actual.Equals(facility, StringComparison.OrdinalIgnoreCase);
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
    private sealed record SnapshotBuild(EndOfDayFillPreviewViewModel Model, ReportSnapshot? Snapshot);
    private sealed record ReportSnapshot(string GroupName, string Facility, IReadOnlyList<string> Recipients, IReadOnlyList<EndOfDayFillRoomViewModel> Rooms, string Json, string Hash);
    private sealed record RenderedEmail(string Subject, string Html, string Text);
    private sealed record NormalizedSnapshot(int GroupId, string GroupName, string Facility, int[] ConfiguredRoomIds, string[] Recipients, NormalizedRoom[] Rooms);
    private sealed record NormalizedRoom(int RoomId, string RoomCode, string RoomName, int CapacityBins, int CurrentBins, NormalizedVariety[] Varieties);
    private sealed record NormalizedVariety(string CanonicalKey, string Name, string ProductionType, bool IsOrganic, NormalizedGrower[] Growers);
    private sealed record NormalizedGrower(string GrowerNumber, string GrowerName, int Bins);
}
