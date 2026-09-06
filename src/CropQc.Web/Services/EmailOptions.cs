using Microsoft.Extensions.Configuration;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace CropQc.Web.Services;

public sealed class EmailOptions
{
    public string Provider { get; init; } = EmailProviders.None;
    public string FromAddress { get; init; } = "HL@fruitandland.com";
    public string ToAddress { get; init; } = "";
    public string QcDefaultRecipients { get; init; } = "";
    public string QcReportDefaultRecipient { get; init; } = QcReportEmailDefaults.RequiredRecipient;
    public bool IsProduction { get; init; }

    public string QcRecipientHeader =>
        string.Join(", ", QcRecipientList);

    public IReadOnlyList<string> QcRecipientList =>
        QcEmailRecipientParser.Parse(QcReportDefaultRecipient).Recipients.Count > 0
            ? QcEmailRecipientParser.Parse(QcReportDefaultRecipient).Recipients
            : [QcReportEmailDefaults.RequiredRecipient];
}

public static class QcEmailRecipientSettings
{
    public const string Key = "QcEmailDefaultRecipients";
}

public sealed record QcEmailRecipientParseResult(IReadOnlyList<string> Recipients, IReadOnlyList<string> InvalidRecipients);

public sealed record QcEmailRecipientResolution(
    string RequiredDefaultRecipient,
    int? ResolvedOrchardId,
    string? ResolvedOrchardName,
    IReadOnlyList<string> ActiveManagerRecipients,
    IReadOnlyList<string> AdditionalRecipients,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<string> SkippedInvalidAddresses,
    bool OrchardHadNoConfiguredManager,
    bool OrchardCouldNotBeResolved,
    string Source)
{
    public int? ResolvedGrowerNumberId { get; init; }
    public string? ResolvedGrowerNumber { get; init; }
    public IReadOnlyList<string> ActiveGrowerNumberRecipients { get; init; } = [];
    public bool GrowerNumberCouldNotBeResolved { get; init; }

    public QcEmailRecipientResolution(IReadOnlyList<string> recipients, string source)
        : this(
            recipients.FirstOrDefault() ?? QcReportEmailDefaults.RequiredRecipient,
            null,
            null,
            [],
            [],
            recipients,
            [],
            false,
            true,
            source)
    {
    }

    public string Header => string.Join(", ", Recipients);
    public bool IsConfigured => Recipients.Count > 0;
}

public static class QcEmailRecipientSources
{
    public const string AdminConfiguration = "Admin Configuration";
    public const string FallbackConfiguration = "Email:QcReportDefaultRecipient";
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
    Task<QcEmailRecipientResolution> ResolveForSampleAsync(long sampleId, IReadOnlyCollection<string>? additionalRecipients, CancellationToken cancellationToken) =>
        ResolveAsync(cancellationToken);
}

public sealed class QcEmailRecipientResolver(
    CropQcDbContext dbContext,
    EmailOptions emailOptions,
    ILogger<QcEmailRecipientResolver> logger) : IQcEmailRecipientResolver
{
    public Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken) =>
        ResolveCoreAsync(null, null, cancellationToken);

    public Task<QcEmailRecipientResolution> ResolveForSampleAsync(long sampleId, IReadOnlyCollection<string>? additionalRecipients, CancellationToken cancellationToken) =>
        ResolveCoreAsync(sampleId, additionalRecipients, cancellationToken);

    private async Task<QcEmailRecipientResolution> ResolveCoreAsync(long? sampleId, IReadOnlyCollection<string>? additionalRecipients, CancellationToken cancellationToken)
    {
        var configuredDefault = QcEmailRecipientParser.Parse(emailOptions.QcReportDefaultRecipient);
        var requiredDefault = QcReportEmailDefaults.RequiredRecipient;
        if (configuredDefault.InvalidRecipients.Count > 0
            || configuredDefault.Recipients.Count != 1
            || !string.Equals(configuredDefault.Recipients[0], requiredDefault, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Invalid Email:QcReportDefaultRecipient configuration was ignored; using the required QC recipient {RequiredRecipient}.",
                QcReportEmailDefaults.RequiredRecipient);
        }

        int? orchardId = null;
        string? orchardName = null;
        int? growerNumberId = null;
        string? growerNumber = null;
        if (sampleId is not null)
        {
            var sampleIdentity = await dbContext.QcSamples.AsNoTracking()
                .Where(x => x.Id == sampleId.Value)
                .Select(x => new
                {
                    DirectId = x.CanonicalOrchardBlock == null ? (int?)null : x.CanonicalOrchardBlock.CanonicalOrchardId,
                    ReceiptId = x.Receipt == null || x.Receipt.CanonicalOrchardBlock == null
                        ? (int?)null
                        : x.Receipt.CanonicalOrchardBlock.CanonicalOrchardId,
                    GrowerNumber = x.Receipt == null ? x.FieldSampleGrowerNumber : x.Receipt.GrowerNumber
                })
                .SingleOrDefaultAsync(cancellationToken);
            orchardId = sampleIdentity?.DirectId ?? sampleIdentity?.ReceiptId;
            growerNumber = CanonicalGrowerService.NormalizeGrowerNumber(sampleIdentity?.GrowerNumber);
            if (orchardId is not null)
            {
                orchardName = await dbContext.CanonicalOrchards.AsNoTracking()
                    .Where(x => x.Id == orchardId.Value)
                    .Select(x => x.OrchardName)
                    .SingleOrDefaultAsync(cancellationToken);
                if (OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(orchardName))
                {
                    logger.LogWarning(
                        "QC report sample {SampleId} resolved an invalid numeric orchard identity {OrchardId}; orchard-manager recipients were not included.",
                        sampleId,
                        orchardId);
                    orchardId = null;
                    orchardName = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(growerNumber))
            {
                var matchingNumbers = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
                    .Where(x => x.IsActive
                        && x.CanonicalGrower.IsActive
                        && x.NormalizedGrowerNumber == growerNumber)
                    .Select(x => x.Id)
                    .Take(2)
                    .ToListAsync(cancellationToken);
                if (matchingNumbers.Count == 1)
                {
                    growerNumberId = matchingNumbers[0];
                }
                else
                {
                    logger.LogWarning(
                        "QC report sample {SampleId} Grower Number {GrowerNumber} did not resolve to exactly one active canonical Grower Number; no Grower Number recipients were included.",
                        sampleId,
                        growerNumber);
                }
            }
        }

        List<string> growerRecipientValues = growerNumberId is null
            ? []
            : await dbContext.GrowerReportRecipients.AsNoTracking()
                .Where(x => x.CanonicalGrowerNumberId == growerNumberId.Value && x.IsActive && !x.IsDeleted)
                .OrderBy(x => x.EmailAddress)
                .Select(x => x.EmailAddress)
                .ToListAsync(cancellationToken);
        List<string> orchardRecipientValues = orchardId is null
            ? []
            : await dbContext.OrchardReportRecipients.AsNoTracking()
                .Where(x => x.CanonicalOrchardId == orchardId.Value && x.IsActive && !x.IsDeleted)
                .OrderBy(x => x.EmailAddress)
                .Select(x => x.EmailAddress)
                .ToListAsync(cancellationToken);
        var growerRecipients = QcEmailRecipientParser.Parse(string.Join(';', growerRecipientValues));
        var managers = QcEmailRecipientParser.Parse(string.Join(';', orchardRecipientValues));
        var additional = QcEmailRecipientParser.Parse(string.Join(';', additionalRecipients ?? []));
        var skipped = growerRecipients.InvalidRecipients
            .Concat(managers.InvalidRecipients)
            .Concat(additional.InvalidRecipients)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (skipped.Length > 0)
        {
            logger.LogWarning(
                "Skipped {InvalidRecipientCount} invalid QC report recipient address(es) for sample {SampleId} and orchard {OrchardId}.",
                skipped.Length,
                sampleId,
                orchardId);
        }

        var recipients = new[] { requiredDefault }
            .Concat(growerRecipients.Recipients)
            .Concat(managers.Recipients)
            .Concat(additional.Recipients)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unresolved = orchardId is null;
        if (sampleId is not null && unresolved)
        {
            logger.LogInformation(
                "QC report sample {SampleId} has no confirmed canonical orchard; Grower Number and required recipients remain eligible.",
                sampleId);
        }
        else if (sampleId is not null && managers.Recipients.Count == 0)
        {
            logger.LogInformation(
                "QC report sample {SampleId} resolved orchard {OrchardId}, but no active valid manager recipient is configured.",
                sampleId,
                orchardId);
        }

        return new QcEmailRecipientResolution(
            requiredDefault,
            orchardId,
            orchardName,
            managers.Recipients,
            additional.Recipients,
            recipients,
            skipped,
            orchardId is not null && managers.Recipients.Count == 0,
            unresolved,
            QcEmailRecipientSources.FallbackConfiguration)
        {
            ResolvedGrowerNumberId = growerNumberId,
            ResolvedGrowerNumber = growerNumber,
            ActiveGrowerNumberRecipients = growerRecipients.Recipients,
            GrowerNumberCouldNotBeResolved = sampleId is not null
                && !string.IsNullOrWhiteSpace(growerNumber)
                && growerNumberId is null
        };
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
            QcReportDefaultRecipient = configuration["Email:QcReportDefaultRecipient"] ?? QcReportEmailDefaults.RequiredRecipient,
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
    public string ReadScope { get; init; } = GmailScopes.Readonly;
}

public static class GmailScopes
{
    public const string Send = "https://www.googleapis.com/auth/gmail.send";
    public const string Readonly = "https://www.googleapis.com/auth/gmail.readonly";
}
