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
    public long? ProcessAllocatedBytesWarningThreshold { get; init; }
    public int RecentRequestLimit { get; init; } = 100;
    public bool IncludeUserIdentifier { get; init; }
    public bool LogEveryRequest { get; init; }
    public bool RuntimeMemoryTelemetryEnabled { get; init; }
    public int RuntimeMemoryTelemetryIntervalSeconds { get; init; } = 60;
    public long RuntimeMemoryWarningWorkingSetBytes { get; init; } = 384L * 1024 * 1024;
    public long RuntimeMemoryCriticalWorkingSetBytes { get; init; } = 450L * 1024 * 1024;

    public static PerformanceDiagnosticsOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("PerformanceDiagnostics");
        var defaultEnabled = !environment.IsProduction();
        var enabled = section.GetValue<bool?>("Enabled") ?? defaultEnabled;

        var warningWorkingSetBytes = Math.Max(1, section.GetValue<long?>("RuntimeMemoryWarningWorkingSetBytes") ?? 384L * 1024 * 1024);
        var criticalWorkingSetBytes = Math.Max(warningWorkingSetBytes + 1, section.GetValue<long?>("RuntimeMemoryCriticalWorkingSetBytes") ?? 450L * 1024 * 1024);
        return new PerformanceDiagnosticsOptions
        {
            Enabled = enabled,
            RequestTimingEnabled = enabled && (section.GetValue<bool?>("RequestTimingEnabled") ?? true),
            EfQueryCountingEnabled = enabled && (section.GetValue<bool?>("EfQueryCountingEnabled") ?? true),
            QueryCountWarningThreshold = section.GetValue<int?>("QueryCountWarningThreshold"),
            RequestElapsedWarningThresholdMs = section.GetValue<double?>("RequestElapsedWarningThresholdMs"),
            DatabaseElapsedWarningThresholdMs = section.GetValue<double?>("DatabaseElapsedWarningThresholdMs"),
            ResponseBytesWarningThreshold = section.GetValue<long?>("ResponseBytesWarningThreshold"),
            ProcessAllocatedBytesWarningThreshold = section.GetValue<long?>("ProcessAllocatedBytesWarningThreshold"),
            RecentRequestLimit = Math.Max(0, section.GetValue<int?>("RecentRequestLimit") ?? 100),
            IncludeUserIdentifier = section.GetValue<bool?>("IncludeUserIdentifier") ?? false,
            LogEveryRequest = section.GetValue<bool?>("LogEveryRequest") ?? !environment.IsProduction(),
            RuntimeMemoryTelemetryEnabled = enabled && (section.GetValue<bool?>("RuntimeMemoryTelemetryEnabled") ?? false),
            RuntimeMemoryTelemetryIntervalSeconds = Math.Clamp(section.GetValue<int?>("RuntimeMemoryTelemetryIntervalSeconds") ?? 60, 60, 3600),
            RuntimeMemoryWarningWorkingSetBytes = warningWorkingSetBytes,
            RuntimeMemoryCriticalWorkingSetBytes = criticalWorkingSetBytes
        };
    }
}
