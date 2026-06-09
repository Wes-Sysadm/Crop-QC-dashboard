using Microsoft.Extensions.Configuration;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace CropQc.Web.Services;

public sealed class EmailOptions
{
    public const string TestingQcDefaultRecipients = "rob@earlbrownandsons.com,wes@fruitandland.com";

    public string Provider { get; init; } = EmailProviders.None;
    public string FromAddress { get; init; } = "HL@fruitandland.com";
    public string ToAddress { get; init; } = "";
    public string QcDefaultRecipients { get; init; } = "";
    public bool IsProduction { get; init; }

    public string QcRecipientHeader =>
        string.Join(", ", QcRecipientList);

    public IReadOnlyList<string> QcRecipientList =>
        QcEmailRecipientParser.Parse(QcDefaultRecipients).Recipients.Count > 0
            ? QcEmailRecipientParser.Parse(QcDefaultRecipients).Recipients
            : QcEmailRecipientParser.Parse(ToAddress).Recipients;
}

public static class QcEmailRecipientSettings
{
    public const string Key = "QcEmailDefaultRecipients";
}

public sealed record QcEmailRecipientParseResult(IReadOnlyList<string> Recipients, IReadOnlyList<string> InvalidRecipients);

public sealed record QcEmailRecipientResolution(IReadOnlyList<string> Recipients, string Source)
{
    public string Header => string.Join(", ", Recipients);
    public bool IsConfigured => Recipients.Count > 0;
}

public static class QcEmailRecipientSources
{
    public const string AdminConfiguration = "Admin Configuration";
    public const string FallbackConfiguration = "Render/appsettings fallback";
    public const string NotConfigured = "Not configured";
}

public static class QcEmailRecipientParser
{
    public static QcEmailRecipientParseResult Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new QcEmailRecipientParseResult([], []);
        }

        var recipients = new List<string>();
        var invalid = new List<string>();
        foreach (var item in value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            try
            {
                var address = new MailAddress(item.Trim()).Address;
                if (!recipients.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    recipients.Add(address);
                }
            }
            catch (FormatException)
            {
                invalid.Add(item.Trim());
            }
        }

        return new QcEmailRecipientParseResult(recipients, invalid.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public interface IQcEmailRecipientResolver
{
    Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken);
}

public sealed class QcEmailRecipientResolver(CropQcDbContext dbContext, EmailOptions emailOptions) : IQcEmailRecipientResolver
{
    public async Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken)
    {
        var configuredValue = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key == QcEmailRecipientSettings.Key)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken);
        var configured = QcEmailRecipientParser.Parse(configuredValue);
        if (configured.Recipients.Count > 0)
        {
            return new QcEmailRecipientResolution(configured.Recipients, QcEmailRecipientSources.AdminConfiguration);
        }

        if (emailOptions.QcRecipientList.Count > 0)
        {
            return new QcEmailRecipientResolution(emailOptions.QcRecipientList, QcEmailRecipientSources.FallbackConfiguration);
        }

        return new QcEmailRecipientResolution([], QcEmailRecipientSources.NotConfigured);
    }
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
            ToAddress = configuration["Email:ToAddress"] ?? configuration["Email:QcDefaultRecipients"] ?? "",
            QcDefaultRecipients = configuration["Email:QcDefaultRecipients"] ?? configuration["Email:ToAddress"] ?? "",
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
