namespace CropQc.Web.Auth;

public sealed class GoogleAuthenticationOptions
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public int SessionDays { get; init; } = 7;
    public IReadOnlySet<string> AllowedDomains { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> BootstrapAdminEmails { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool IsGoogleConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static GoogleAuthenticationOptions FromConfiguration(IConfiguration configuration)
    {
        var allowedDomains = (configuration["Authentication:AllowedGoogleDomains"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bootstrapAdminEmails = (configuration["Authentication:BootstrapAdminEmails"] ?? configuration["Authentication:AdminEmails"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new GoogleAuthenticationOptions
        {
            ClientId = configuration["Authentication:Google:ClientId"],
            ClientSecret = configuration["Authentication:Google:ClientSecret"],
            SessionDays = ReadSessionDays(configuration),
            AllowedDomains = allowedDomains,
            BootstrapAdminEmails = bootstrapAdminEmails
        };
    }

    private static int ReadSessionDays(IConfiguration configuration)
    {
        var configuredDays = configuration.GetValue<int?>("Authentication:SessionDays");
        return configuredDays is > 0 ? configuredDays.Value : 7;
    }

    public bool IsBootstrapAdminEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && BootstrapAdminEmails.Contains(email.Trim().ToLowerInvariant());

    public bool IsAllowedEmail(string? email)
    {
        var domain = GetEmailDomain(email);
        return domain is not null && AllowedDomains.Contains(domain);
    }

    public static string? GetEmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var atIndex = email.LastIndexOf('@');
        return atIndex < 0 || atIndex == email.Length - 1
            ? null
            : email[(atIndex + 1)..].Trim().ToLowerInvariant();
    }
}
