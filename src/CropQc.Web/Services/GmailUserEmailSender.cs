using CropQc.Data.Entities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace CropQc.Web.Services;

public interface IQcEmailSender
{
    Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken);
}

public sealed record QcEmailMessage(
    string From,
    string To,
    string? ReplyTo,
    string Subject,
    string Body);

public sealed record QcEmailSendResult(bool Success, string? MessageId, string? Error, bool ReconnectRequired = false)
{
    public static QcEmailSendResult Sent(string? messageId) => new(true, messageId, null);
    public static QcEmailSendResult Failed(string error, bool reconnectRequired = false) => new(false, null, error, reconnectRequired);
}

public sealed class GmailUserEmailSender(
    EmailOptions emailOptions,
    IGoogleCredentialStore credentialStore,
    IHttpClientFactory httpClientFactory,
    ILogger<GmailUserEmailSender> logger) : IQcEmailSender
{
    public async Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase))
        {
            return QcEmailSendResult.Failed($"Email provider is {emailOptions.Provider}. Set Email__Provider=GmailUser to send QC Summary email.");
        }

        if (string.IsNullOrWhiteSpace(sender.Email))
        {
            return QcEmailSendResult.Failed("A logged-in user is required to send QC Summary email.");
        }

        GoogleAccessTokenResult token;
        try
        {
            token = await credentialStore.GetAccessTokenAsync(sender, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Gmail credential lookup failed for sender {SenderEmail}.", sender.Email);
            return QcEmailSendResult.Failed("Gmail permission is required. Please reconnect Google/Gmail.", reconnectRequired: true);
        }

        if (token.AccessToken is null)
        {
            return QcEmailSendResult.Failed(token.Error ?? "Gmail permission is required. Please reconnect Google/Gmail.", token.ReconnectRequired);
        }

        var rawMessage = BuildRawMessage(message);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new GmailSendRequest(rawMessage));

        try
        {
            logger.LogInformation("Gmail send started for sender {SenderEmail}. To: {To}. Subject: {Subject}.", sender.Email, message.To, message.Subject);
            var client = httpClientFactory.CreateClient("GmailApi");
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var safeReason = SafeGmailError(response.StatusCode, responseBody);
                logger.LogWarning("Gmail send failed for sender {SenderEmail}. Status: {StatusCode}. Reason: {Reason}.", sender.Email, response.StatusCode, safeReason);
                var reconnect = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
                return QcEmailSendResult.Failed($"Gmail API rejected message: {safeReason}", reconnect);
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<GmailSendResponse>(responseBody);
            logger.LogInformation("Gmail send succeeded for sender {SenderEmail}. GmailMessageId: {MessageId}.", sender.Email, result?.Id ?? "(missing)");
            return QcEmailSendResult.Sent(result?.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gmail send failed for sender {SenderEmail}.", sender.Email);
            return QcEmailSendResult.Failed($"Gmail API send failed: {SafeErrorMessage(ex)}");
        }
    }

    public static string BuildRawMessage(QcEmailMessage message)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"From: {message.From}");
        builder.AppendLine($"To: {message.To}");
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            builder.AppendLine($"Reply-To: {message.ReplyTo}");
        }

        builder.AppendLine($"Subject: {message.Subject}");
        builder.AppendLine("MIME-Version: 1.0");
        builder.AppendLine("Content-Type: text/plain; charset=utf-8");
        builder.AppendLine("Content-Transfer-Encoding: 8bit");
        builder.AppendLine();
        builder.AppendLine(message.Body);
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string SafeGmailError(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return statusCode.ToString();
        }

        return responseBody.Length <= 500 ? responseBody : responseBody[..500];
    }

    private static string SafeErrorMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? "Unknown error." : exception.Message;

    private sealed record GmailSendRequest([property: JsonPropertyName("raw")] string Raw);

    private sealed class GmailSendResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
