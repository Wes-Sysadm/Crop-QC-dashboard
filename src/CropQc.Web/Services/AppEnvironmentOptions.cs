using Microsoft.Extensions.Hosting;

namespace CropQc.Web.Services;

public sealed class AppEnvironmentOptions
{
    public string Kind { get; init; } = AppEnvironmentKinds.Development;
    public string DisplayName { get; init; } = "Development";

    public bool IsProduction => string.Equals(Kind, AppEnvironmentKinds.Production, StringComparison.OrdinalIgnoreCase);
    public bool IsStagingLike =>
        string.Equals(Kind, AppEnvironmentKinds.Staging, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Kind, AppEnvironmentKinds.Development, StringComparison.OrdinalIgnoreCase);

    public static AppEnvironmentOptions FromConfiguration(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var fallbackKind = hostEnvironment.IsProduction()
            ? AppEnvironmentKinds.Production
            : AppEnvironmentKinds.Development;
        var kind = NormalizeKind(configuration["AppEnvironment:Kind"] ?? fallbackKind);
        var displayName = configuration["AppEnvironment:DisplayName"];
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = kind;
        }

        return new AppEnvironmentOptions
        {
            Kind = kind,
            DisplayName = displayName.Trim()
        };
    }

    private static string NormalizeKind(string value)
    {
        if (string.Equals(value, AppEnvironmentKinds.Production, StringComparison.OrdinalIgnoreCase))
        {
            return AppEnvironmentKinds.Production;
        }

        if (string.Equals(value, AppEnvironmentKinds.Staging, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Test", StringComparison.OrdinalIgnoreCase))
        {
            return AppEnvironmentKinds.Staging;
        }

        return AppEnvironmentKinds.Development;
    }
}

public static class AppEnvironmentKinds
{
    public const string Production = "Production";
    public const string Staging = "Staging";
    public const string Development = "Development";
}
