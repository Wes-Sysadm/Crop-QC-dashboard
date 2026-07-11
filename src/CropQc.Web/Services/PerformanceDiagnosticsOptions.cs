using Microsoft.Extensions.Hosting;

namespace CropQc.Web.Services;

public sealed class PerformanceDiagnosticsOptions
{
    public bool Enabled { get; init; }
    public bool RequestTimingEnabled { get; init; }
    public bool EfQueryCountingEnabled { get; init; }
    public int? QueryCountWarningThreshold { get; init; }
    public bool IncludeUserIdentifier { get; init; }

    public static PerformanceDiagnosticsOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("PerformanceDiagnostics");
        var defaultEnabled = !environment.IsProduction();
        var enabled = section.GetValue<bool?>("Enabled") ?? defaultEnabled;

        return new PerformanceDiagnosticsOptions
        {
            Enabled = enabled,
            RequestTimingEnabled = enabled && (section.GetValue<bool?>("RequestTimingEnabled") ?? true),
            EfQueryCountingEnabled = enabled && (section.GetValue<bool?>("EfQueryCountingEnabled") ?? true),
            QueryCountWarningThreshold = section.GetValue<int?>("QueryCountWarningThreshold"),
            IncludeUserIdentifier = section.GetValue<bool?>("IncludeUserIdentifier") ?? false
        };
    }
}
