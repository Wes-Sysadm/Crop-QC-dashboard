using System.Text;
using System.Text.Encodings.Web;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IEbsDailyBinsEmailService
{
    Task<EbsDailyBinsEmailSendResult> SendAsync(string? requestedByEmail, bool isTest, CancellationToken cancellationToken);
    Task<bool> TrySendScheduledAsync(CancellationToken cancellationToken);
}

public sealed record EbsDailyBinsEmailSendResult(bool Success, string Message);

public static class EbsDailyBinsEmailSettings
{
    public const string RecipientsKey = "EbsDailyBinsEmailRecipients";
    public const string EnabledKey = "EbsDailyBinsEmailEnabled";
    public const string SendHourLocalKey = "EbsDailyBinsEmailSendHourLocal";
    public const string SenderEmailKey = "EbsDailyBinsEmailSender";
    public const string LastSentDateKey = "EbsDailyBinsEmailLastSentDate";
    public const string DefaultRecipients = "rob@earlbrownandsons.com,wes@fruitandland.com";
}

public sealed class EbsDailyBinsEmailService(
    CropQcDbContext dbContext,
    IDashboardDataService dashboardDataService,
    IQcEmailSender emailSender,
    IBusinessTimeService businessTime,
    ILogger<EbsDailyBinsEmailService> logger) : IEbsDailyBinsEmailService
{
    public async Task<EbsDailyBinsEmailSendResult> SendAsync(string? requestedByEmail, bool isTest, CancellationToken cancellationToken)
    {
        var recipientsValue = await GetConfigValueAsync(EbsDailyBinsEmailSettings.RecipientsKey, cancellationToken);
        var recipients = QcEmailRecipientParser.Parse(recipientsValue);
        if (recipients.InvalidRecipients.Count > 0)
        {
            return new(false, $"Invalid EBS daily bin recipient: {string.Join(", ", recipients.InvalidRecipients)}.");
        }

        if (recipients.Recipients.Count == 0)
        {
            return new(false, "Add at least one EBS daily bin email recipient before sending.");
        }

        var senderEmail = string.IsNullOrWhiteSpace(requestedByEmail)
            ? await GetConfigValueAsync(EbsDailyBinsEmailSettings.SenderEmailKey, cancellationToken)
            : requestedByEmail;
        var sender = await FindSenderAsync(senderEmail, cancellationToken);
        if (sender is null)
        {
            return new(false, $"EBS daily bin email sender {senderEmail ?? "(missing)"} was not found as an active user.");
        }

        var dashboard = await dashboardDataService.GetHomeDashboardAsync(
            new RoomSummaryFilterForm { Facility = "EBS", EbsLocation = "All EBS", RoomStatus = "All" },
            cancellationToken);
        var currentRooms = dashboard.RoomSummaries
            .Where(x => string.Equals(x.Facility, "EBS", StringComparison.OrdinalIgnoreCase) && (x.CurrentBinsCount ?? 0) > 0)
            .OrderBy(x => x.LocationGroup)
            .ThenBy(x => x.RoomCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalBins = currentRooms.Sum(x => x.CurrentBinsCount ?? 0);
        var totalLots = currentRooms.Sum(x => x.CurrentLotsCount);
        var today = LocalToday();
        var subject = $"{(isTest ? "[TEST] " : "")}EBS Daily Bin Availability - {today:yyyy-MM-dd}";
        var htmlBody = BuildHtmlBody(currentRooms, totalBins, totalLots, today, isTest);
        var textBody = BuildTextBody(currentRooms, totalBins, totalLots, today, isTest);
        var message = new QcEmailMessage(
            sender.Email,
            string.Join(", ", recipients.Recipients),
            sender.Email,
            subject,
            textBody,
            htmlBody,
            []);
        var sendResult = await emailSender.SendAsync(sender, message, cancellationToken);
        if (!sendResult.Success)
        {
            return new(false, sendResult.Error ?? "EBS daily bin email send failed.");
        }

        if (!isTest)
        {
            await SetConfigValueAsync(EbsDailyBinsEmailSettings.LastSentDateKey, today.ToString("yyyy-MM-dd"), cancellationToken);
        }

        return new(true, $"EBS daily bin email sent to {message.To}.");
    }

    public async Task<bool> TrySendScheduledAsync(CancellationToken cancellationToken)
    {
        if (!BoolConfig(await GetConfigValueAsync(EbsDailyBinsEmailSettings.EnabledKey, cancellationToken)))
        {
            return false;
        }

        var now = businessTime.NowPacific;
        var sendHour = IntConfig(await GetConfigValueAsync(EbsDailyBinsEmailSettings.SendHourLocalKey, cancellationToken), 17);
        if (now.Hour < sendHour)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(now.DateTime).ToString("yyyy-MM-dd");
        var lastSent = await GetConfigValueAsync(EbsDailyBinsEmailSettings.LastSentDateKey, cancellationToken);
        if (string.Equals(lastSent, today, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var result = await SendAsync(null, isTest: false, cancellationToken);
        if (!result.Success)
        {
            logger.LogWarning("Scheduled EBS daily bin email was not sent: {Message}", result.Message);
        }

        return result.Success;
    }

    private async Task<User?> FindSenderAsync(string? senderEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            return null;
        }

        return await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == senderEmail.Trim() && x.IsActive, cancellationToken);
    }

    private async Task<string?> GetConfigValueAsync(string key, CancellationToken cancellationToken) =>
        await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task SetConfigValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var config = await dbContext.DashboardConfigurations.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (config is null)
        {
            dbContext.DashboardConfigurations.Add(new DashboardConfiguration
            {
                Key = key,
                Value = value,
                Description = "Last successful EBS daily bin email send date.",
                ValueType = "Date",
                CreatedAt = businessTime.UtcNow
            });
        }
        else
        {
            config.Value = value;
            config.UpdatedAt = businessTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildTextBody(IReadOnlyList<RoomSummaryItemViewModel> rooms, int totalBins, int totalLots, DateOnly date, bool isTest)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{(isTest ? "TEST - " : "")}EBS Daily Bin Availability");
        builder.AppendLine(date.ToString("yyyy-MM-dd"));
        builder.AppendLine();
        builder.AppendLine($"Total bins currently in EBS storage: {totalBins}");
        builder.AppendLine($"Current grower lots: {totalLots}");
        builder.AppendLine();
        foreach (var room in rooms)
        {
            builder.AppendLine($"{room.LocationGroup} {room.RoomCode}: {room.CurrentBinsCount ?? 0} bins, {room.CurrentLotsCount} lots");
        }

        if (rooms.Count == 0)
        {
            builder.AppendLine("No EBS rooms currently show bins in storage.");
        }

        return builder.ToString();
    }

    private static string BuildHtmlBody(IReadOnlyList<RoomSummaryItemViewModel> rooms, int totalBins, int totalLots, DateOnly date, bool isTest)
    {
        var builder = new StringBuilder();
        builder.Append("<h1>").Append(isTest ? "TEST - " : "").Append("EBS Daily Bin Availability</h1>");
        builder.Append("<p>").Append(HtmlEncoder.Default.Encode(date.ToString("yyyy-MM-dd"))).Append("</p>");
        builder.Append("<p><strong>Total bins currently in EBS storage:</strong> ").Append(totalBins).Append("<br>");
        builder.Append("<strong>Current grower lots:</strong> ").Append(totalLots).Append("</p>");
        builder.Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\"><thead><tr><th>Location</th><th>Room</th><th>Bins</th><th>Lots</th><th>Lot summary</th></tr></thead><tbody>");
        foreach (var room in rooms)
        {
            builder.Append("<tr><td>").Append(HtmlEncoder.Default.Encode(room.LocationGroup)).Append("</td><td>")
                .Append(HtmlEncoder.Default.Encode(room.RoomCode)).Append("</td><td>")
                .Append(room.CurrentBinsCount ?? 0).Append("</td><td>")
                .Append(room.CurrentLotsCount).Append("</td><td>")
                .Append(HtmlEncoder.Default.Encode(room.LotSummary)).Append("</td></tr>");
        }

        if (rooms.Count == 0)
        {
            builder.Append("<tr><td colspan=\"5\">No EBS rooms currently show bins in storage.</td></tr>");
        }

        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static bool BoolConfig(string? value) =>
        bool.TryParse(value, out var parsed) && parsed;

    private static int IntConfig(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 0, 23) : fallback;

    private DateOnly LocalToday() =>
        DateOnly.FromDateTime(businessTime.NowPacific.DateTime);
}

public sealed class EbsDailyBinsEmailHostedService(IServiceScopeFactory scopeFactory, ILogger<EbsDailyBinsEmailHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scheduled EBS daily bin email check failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEbsDailyBinsEmailService>();
        await service.TrySendScheduledAsync(cancellationToken);
    }
}
