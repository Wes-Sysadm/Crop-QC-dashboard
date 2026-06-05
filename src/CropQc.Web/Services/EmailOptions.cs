namespace CropQc.Web.Services;

public sealed class EmailOptions
{
    public const string TestingQcDefaultRecipients = "rob@earlbrownandsons.com,wes@fruitandland.com";

    public string Provider { get; init; } = EmailProviders.None;
    public string FromAddress { get; init; } = "HL@fruitandland.com";
    public string ToAddress { get; init; } = TestingQcDefaultRecipients;
    public string QcDefaultRecipients { get; init; } = TestingQcDefaultRecipients;

    public string QcRecipientHeader =>
        string.Join(", ", QcRecipientList);

    public IReadOnlyList<string> QcRecipientList =>
        ParseRecipients(QcDefaultRecipients).Count > 0
            ? ParseRecipients(QcDefaultRecipients)
            : ParseRecipients(ToAddress);

    private static IReadOnlyList<string> ParseRecipients(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}

public static class EmailProviders
{
    public const string None = "None";
    public const string Placeholder = "Placeholder";
    public const string GmailUser = "GmailUser";
}

public sealed class GmailOptions
{
    public string SendScope { get; init; } = GmailScopes.Send;
}

public static class GmailScopes
{
    public const string Send = "https://www.googleapis.com/auth/gmail.send";
}
