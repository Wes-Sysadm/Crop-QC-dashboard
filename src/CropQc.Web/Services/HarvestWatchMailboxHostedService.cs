using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json.Serialization;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

/// <summary>Polls only recent, correlated replies from the configured Gmail mailbox.</summary>
public sealed class HarvestWatchMailboxHostedService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<HarvestWatchMailboxHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try { await PollAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "HarvestWatch mailbox poll failed; the application remains available and deployments remain pending."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<CropQcDbContext>();
        var emailOptions = services.GetRequiredService<EmailOptions>();
        if (!string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase)) return;
        var wes = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == HarvestWatchConstants.VerificationRecipient && x.IsActive, cancellationToken);
        if (wes is null) return;
        var scopeValue = await db.UserGoogleCredentials.AsNoTracking()
            .Where(x => x.UserId == wes.Id && x.Provider == GoogleCredentialStore.HarvestWatchMailboxProviderName)
            .Select(x => x.Scope)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(scopeValue) || !GoogleCredentialStore.HasGmailReadScope(scopeValue))
        {
            logger.LogInformation("HarvestWatch mailbox polling is waiting for {Email} to reconnect Google/Gmail with read permission.", HarvestWatchConstants.VerificationRecipient);
            return;
        }
        var credentialStore = services.GetRequiredService<IGoogleCredentialStore>();
        var token = await credentialStore.GetMailboxAccessTokenAsync(wes, cancellationToken);
        if (token.AccessToken is null) { logger.LogWarning("HarvestWatch mailbox poll could not obtain Gmail access: {Error}", token.Error); return; }
        var cursor = await db.HarvestWatchMailboxCursors.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new HarvestWatchMailboxCursor { Id = 1 };
        if (db.Entry(cursor).State == EntityState.Detached) db.HarvestWatchMailboxCursors.Add(cursor);
        var after = (cursor.LastPolledAt ?? DateTimeOffset.UtcNow.AddDays(-2)).UtcDateTime.ToString("yyyy/MM/dd");
        var query = Uri.EscapeDataString($"from:{HarvestWatchConstants.VerificationRecipient} after:{after} [HW:");
        var client = httpClientFactory.CreateClient("GmailApi");
        var processor = services.GetRequiredService<IHarvestWatchService>();
        string? pageToken = null;
        do
        {
            var pageUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages?q={query}&maxResults=100" + (string.IsNullOrWhiteSpace(pageToken) ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var listResponse = await client.SendAsync(listRequest, cancellationToken);
            if (!listResponse.IsSuccessStatusCode) { logger.LogWarning("HarvestWatch Gmail list returned {StatusCode}; cursor will not advance.", listResponse.StatusCode); return; }
            var list = await listResponse.Content.ReadFromJsonAsync<GmailMessageList>(cancellationToken) ?? new GmailMessageList();
            foreach (var message in list.Messages ?? [])
            {
                if (await db.HarvestWatchInboundMessages.AsNoTracking().AnyAsync(x => x.GmailMessageId == message.Id, cancellationToken)) continue;
                var detailed = await GetMessageAsync(client, token.AccessToken, message.Id, cancellationToken);
                if (detailed is null) return;
                var headers = detailed.Payload?.Headers ?? [];
                var sender = NormalizeAddress(Header(headers, "From"));
                var subject = Header(headers, "Subject");
                var receivedAt = long.TryParse(detailed.InternalDate, out var milliseconds) ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : DateTimeOffset.UtcNow;
                await processor.ProcessInboundReplyAsync(new HarvestWatchInboundReply(message.Id, sender, subject, ReadBody(detailed.Payload), receivedAt), cancellationToken);
            }
            pageToken = list.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));
        cursor.LastPolledAt = DateTimeOffset.UtcNow;
        cursor.UpdatedAt = cursor.LastPolledAt.Value;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<GmailMessage?> GetMessageAsync(HttpClient client, string token, string id, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(id)}?format=full");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<GmailMessage>(cancellationToken) : null;
    }

    private static string Header(IEnumerable<GmailHeader> headers, string name) => headers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
    private static string NormalizeAddress(string value)
    {
        try { return new MailAddress(value).Address.Trim().ToLowerInvariant(); }
        catch (FormatException) { return value.Trim().ToLowerInvariant(); }
    }
    private static string ReadBody(GmailPayload? payload)
    {
        if (payload is null) return "";
        if (payload.MimeType?.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(payload.Body?.Data))
        {
            try { return Encoding.UTF8.GetString(Base64UrlDecode(payload.Body.Data)); } catch (FormatException) { return ""; }
        }
        return string.Join("\n", (payload.Parts ?? []).Select(ReadBody).Where(x => !string.IsNullOrWhiteSpace(x)));
    }
    private static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));

    private sealed class GmailMessageList { [JsonPropertyName("messages")] public List<GmailMessageReference>? Messages { get; set; } [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; } }
    private sealed class GmailMessageReference { [JsonPropertyName("id")] public string Id { get; set; } = ""; }
    private sealed class GmailMessage { [JsonPropertyName("internalDate")] public string? InternalDate { get; set; } [JsonPropertyName("payload")] public GmailPayload? Payload { get; set; } }
    private sealed class GmailPayload { [JsonPropertyName("mimeType")] public string? MimeType { get; set; } [JsonPropertyName("headers")] public List<GmailHeader>? Headers { get; set; } [JsonPropertyName("body")] public GmailBody? Body { get; set; } [JsonPropertyName("parts")] public List<GmailPayload>? Parts { get; set; } }
    private sealed class GmailHeader { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("value")] public string Value { get; set; } = ""; }
    private sealed class GmailBody { [JsonPropertyName("data")] public string? Data { get; set; } }
}
