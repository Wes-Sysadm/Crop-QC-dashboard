using Microsoft.Extensions.Hosting;

namespace CropQc.Web.Services;

public sealed class PerformanceDiagnosticsOptions
{
    public bool Enabled { get; init; }
    public bool RequestTimingEnabled { get; init; }
    public bool EfQueryCountingEnabled { get; init; }
    public int? QueryCountWarningThreshold { get; init; }
    public double? RequestElapsedWarningThresholdMs { get; init; }
    public double? DatabaseElapsedWarningThresholdMs { get; init; }
    public long? ResponseBytesWarningThreshold { get; init; }
    public int RecentRequestLimit { get; init; } = 100;
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
            RequestElapsedWarningThresholdMs = section.GetValue<double?>("RequestElapsedWarningThresholdMs"),
            DatabaseElapsedWarningThresholdMs = section.GetValue<double?>("DatabaseElapsedWarningThresholdMs"),
            ResponseBytesWarningThreshold = section.GetValue<long?>("ResponseBytesWarningThreshold"),
            RecentRequestLimit = Math.Max(0, section.GetValue<int?>("RecentRequestLimit") ?? 100),
            IncludeUserIdentifier = section.GetValue<bool?>("IncludeUserIdentifier") ?? false
        };
    }
}
