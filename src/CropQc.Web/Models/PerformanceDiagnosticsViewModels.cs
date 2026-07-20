namespace CropQc.Web.Models;

public sealed record PerformanceDiagnosticsPageViewModel(
    string EnvironmentDisplayName,
    int RecentRequestLimit,
    IReadOnlyList<PerformanceRequestMetricViewModel> Metrics);

public sealed record PerformanceRequestMetricViewModel(
    DateTimeOffset CapturedAt,
    string Method,
    string Path,
    string? EndpointName,
    int StatusCode,
    double ElapsedMilliseconds,
    int EfCommandCount,
    double EfCommandElapsedMilliseconds,
    int EfCommandFailureCount,
    long? ResponseBytes,
    int ExternalCallCount,
    string ExternalProviders,
    bool WarningThresholdExceeded,
    string TraceIdentifier);
