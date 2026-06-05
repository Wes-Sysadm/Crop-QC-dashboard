namespace CropQc.Web.Services;

public sealed class EmailOptions
{
    public string Provider { get; init; } = EmailProviders.None;
    public string FromAddress { get; init; } = "HL@fruitandland.com";
    public string ToAddress { get; init; } = "QC@fruitandland.com";
    public string QcDefaultRecipients { get; init; } = "QC@fruitandland.com";
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
