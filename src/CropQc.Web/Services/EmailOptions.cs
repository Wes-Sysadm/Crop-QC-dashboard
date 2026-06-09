using Microsoft.Extensions.Configuration;

namespace CropQc.Web.Services;

public sealed class EmailOptions
{
    public const string TestingQcDefaultRecipients = "rob@earlbrownandsons.com,wes@fruitandland.com";

    public string Provider { get; init; } = EmailProviders.None;
    public string FromAddress { get; init; } = "HL@fruitandland.com";
    public string ToAddress { get; init; } = TestingQcDefaultRecipients;
    public string QcDefaultRecipients { get; init; } = TestingQcDefaultRecipients;
    public bool IsProduction { get; init; }

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

public static class EmailOptionsFactory
{
    public static EmailOptions Create(IConfiguration configuration, bool isProduction) =>
        Create(configuration, isProduction, ReadExplicitProviderFromEnvironment());

    public static EmailOptions Create(IConfiguration configuration, bool isProduction, string? explicitEnvironmentProvider)
    {
        var provider = string.IsNullOrWhiteSpace(explicitEnvironmentProvider)
            ? configuration["Email:Provider"]
            : explicitEnvironmentProvider;
        var hasExplicitEnvironmentProvider = !string.IsNullOrWhiteSpace(explicitEnvironmentProvider);
        if (isProduction
            && !hasExplicitEnvironmentProvider
            && (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, EmailProviders.None, StringComparison.OrdinalIgnoreCase)))
        {
            provider = EmailProviders.GmailUser;
        }

        return new EmailOptions
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? EmailProviders.None : provider.Trim(),
            FromAddress = configuration["Email:FromAddress"] ?? "HL@fruitandland.com",
            ToAddress = configuration["Email:ToAddress"] ?? configuration["Email:QcDefaultRecipients"] ?? EmailOptions.TestingQcDefaultRecipients,
            QcDefaultRecipients = configuration["Email:QcDefaultRecipients"] ?? configuration["Email:ToAddress"] ?? EmailOptions.TestingQcDefaultRecipients,
            IsProduction = isProduction
        };
    }

    public static string? ReadExplicitProviderFromEnvironment() =>
        Environment.GetEnvironmentVariable("Email__Provider")
        ?? Environment.GetEnvironmentVariable("EMAIL__PROVIDER");
}

public sealed class GmailOptions
{
    public string SendScope { get; init; } = GmailScopes.Send;
}

public static class GmailScopes
{
    public const string Send = "https://www.googleapis.com/auth/gmail.send";
}
