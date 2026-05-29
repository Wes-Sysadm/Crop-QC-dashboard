namespace CropQc.Web.Auth;

public sealed class GoogleAuthenticationOptions
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public IReadOnlySet<string> AllowedDomains { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> AdminEmails { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool IsGoogleConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static GoogleAuthenticationOptions FromConfiguration(IConfiguration configuration)
    {
        var allowedDomains = (configuration["Authentication:AllowedGoogleDomains"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adminEmails = (configuration["Authentication:AdminEmails"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new GoogleAuthenticationOptions
        {
            ClientId = configuration["Authentication:Google:ClientId"],
            ClientSecret = configuration["Authentication:Google:ClientSecret"],
            AllowedDomains = allowedDomains,
            AdminEmails = adminEmails
        };
    }

    public bool IsAdminEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && AdminEmails.Contains(email.Trim().ToLowerInvariant());

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
