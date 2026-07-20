using CropQc.Shared.Storage;
using CropQc.Web.Auth;

namespace CropQc.Web.Services;

public sealed class StagingEnvironmentOptions
{
    public IReadOnlySet<string> AllowedTestUserEmails { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ProductionDatabaseMarkers { get; init; } = [];
    public IReadOnlyList<string> ProductionGoogleDriveFolderIds { get; init; } = [];
    public bool GoogleDriveIsolationConfirmed { get; init; }

    public static StagingEnvironmentOptions FromConfiguration(IConfiguration configuration)
    {
        return new StagingEnvironmentOptions
        {
            AllowedTestUserEmails = ReadCsv(configuration["Staging:AllowedTestUserEmails"]
                ?? configuration["Authentication:StagingAllowedTestUserEmails"])
                .Select(email => email.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            ProductionDatabaseMarkers = ReadCsv(configuration["Staging:ProductionDatabaseMarkers"]),
            ProductionGoogleDriveFolderIds = ReadCsv(configuration["Staging:ProductionGoogleDriveFolderIds"]),
            GoogleDriveIsolationConfirmed = configuration.GetValue<bool>("Staging:GoogleDriveIsolationConfirmed")
        };
    }

    public bool IsAllowedTestUser(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && AllowedTestUserEmails.Contains(email.Trim().ToLowerInvariant());

    private static IReadOnlyList<string> ReadCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
}

public static class StagingEnvironmentValidator
{
    public static void Validate(
        IConfiguration configuration,
        AppEnvironmentOptions appEnvironment,
        StagingEnvironmentOptions stagingOptions,
        GoogleAuthenticationOptions googleAuthenticationOptions,
        EmailOptions emailOptions,
        FileStorageOptions fileStorageOptions,
        GoogleDriveStorageOptions googleDriveStorageOptions,
        PerformanceDiagnosticsOptions performanceDiagnosticsOptions)
    {
        if (!appEnvironment.IsStaging)
        {
            return;
        }

        var errors = BuildValidationErrors(
            configuration,
            appEnvironment,
            stagingOptions,
            googleAuthenticationOptions,
            emailOptions,
            fileStorageOptions,
            googleDriveStorageOptions,
            performanceDiagnosticsOptions);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Staging environment is not safely configured: " + string.Join(" ", errors));
        }
    }

    public static IReadOnlyList<string> BuildValidationErrors(
        IConfiguration configuration,
        AppEnvironmentOptions appEnvironment,
        StagingEnvironmentOptions stagingOptions,
        GoogleAuthenticationOptions googleAuthenticationOptions,
        EmailOptions emailOptions,
        FileStorageOptions fileStorageOptions,
        GoogleDriveStorageOptions googleDriveStorageOptions,
        PerformanceDiagnosticsOptions performanceDiagnosticsOptions)
    {
        if (!appEnvironment.IsStaging)
        {
            return [];
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(appEnvironment.DisplayName)
            || appEnvironment.DisplayName.Contains("Production", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("AppEnvironment__DisplayName must identify staging and must not contain Production.");
        }

        var provider = configuration["DATABASE_PROVIDER"] ?? configuration["Database:Provider"] ?? "";
        if (!provider.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("DATABASE_PROVIDER must be PostgreSql for staging.");
        }

        var connectionString = configuration.GetConnectionString(configuration["Database:ConnectionStringName"] ?? CropQc.Data.CropQcDatabase.DefaultConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("ConnectionStrings__CropQc must point to an isolated staging database.");
        }
        else
        {
            foreach (var marker in stagingOptions.ProductionDatabaseMarkers)
            {
                if (!string.IsNullOrWhiteSpace(marker)
                    && connectionString.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("ConnectionStrings__CropQc appears to reference a known production database identifier.");
                    break;
                }
            }
        }

        if (!googleAuthenticationOptions.IsGoogleConfigured)
        {
            errors.Add("Authentication__Google__ClientId and Authentication__Google__ClientSecret are required for staging.");
        }

        if (googleAuthenticationOptions.AllowedDomains.Count == 0)
        {
            errors.Add("Authentication__AllowedGoogleDomains must be configured for staging.");
        }

        if (stagingOptions.AllowedTestUserEmails.Count == 0)
        {
            errors.Add("Staging__AllowedTestUserEmails must explicitly list approved test accounts.");
        }
        else
        {
            foreach (var email in stagingOptions.AllowedTestUserEmails)
            {
                if (!googleAuthenticationOptions.IsAllowedEmail(email))
                {
                    errors.Add("Every staging test user must belong to an allowed Google domain.");
                    break;
                }
            }
        }

        if (!string.Equals(emailOptions.Provider, EmailProviders.None, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Email__Provider must be None for staging unless a separate reviewed mail sink is implemented.");
        }

        var storageProvider = string.IsNullOrWhiteSpace(fileStorageOptions.Provider)
            ? FileStorageProviders.Local
            : fileStorageOptions.Provider;
        if (string.Equals(storageProvider, FileStorageProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase)
            && !stagingOptions.GoogleDriveIsolationConfirmed)
        {
            errors.Add("Staging Google Drive storage requires Staging__GoogleDriveIsolationConfirmed=true.");
        }

        foreach (var productionFolderId in stagingOptions.ProductionGoogleDriveFolderIds)
        {
            if (string.IsNullOrWhiteSpace(productionFolderId))
            {
                continue;
            }

            if (string.Equals(googleDriveStorageOptions.RootFolderId, productionFolderId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(googleDriveStorageOptions.SharedDriveId, productionFolderId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Staging Google Drive settings reference a known production folder id.");
                break;
            }
        }

        if (!performanceDiagnosticsOptions.Enabled || performanceDiagnosticsOptions.RecentRequestLimit <= 0)
        {
            errors.Add("PerformanceDiagnostics must be enabled with bounded recent-request retention for staging validation.");
        }

        if ((configuration["QcStation:ApiBaseUrl"] ?? "").Contains("crop-qc-dashboard.onrender.com", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("QcStation__ApiBaseUrl must point to staging, not production.");
        }

        return errors;
    }
}
