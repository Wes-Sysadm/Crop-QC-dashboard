namespace CropQc.Web.Services;

public sealed record PerformanceRequestMetric(
    DateTimeOffset CapturedAt,
    string Method,
    string Path,
    string? EndpointName,
    int StatusCode,
    double ElapsedMilliseconds,
    int EfCommandCount,
    double EfCommandElapsedMilliseconds,
    double SlowestEfCommandMilliseconds,
    int EfCommandFailureCount,
    long ProcessAllocatedBytesDelta,
    long? ResponseBytes,
    int ExternalCallCount,
    IReadOnlyDictionary<string, int> ExternalProviderCounts,
    bool WarningThresholdExceeded,
    string TraceIdentifier,
    string? UserIdentifier);

public interface IPerformanceRequestMetricSink
{
    void Add(PerformanceRequestMetric metric);
    IReadOnlyList<PerformanceRequestMetric> Snapshot();
    void Clear();
}

public sealed class BoundedPerformanceRequestMetricSink(PerformanceDiagnosticsOptions options) : IPerformanceRequestMetricSink
{
    private readonly object sync = new();
    private readonly Queue<PerformanceRequestMetric> metrics = new();
    private readonly int limit = Math.Max(0, options.RecentRequestLimit);

    public void Add(PerformanceRequestMetric metric)
    {
        if (!options.Enabled || limit == 0)
        {
            return;
        }

        lock (sync)
        {
            metrics.Enqueue(metric);
            while (metrics.Count > limit)
            {
                metrics.Dequeue();
            }
        }
    }

    public IReadOnlyList<PerformanceRequestMetric> Snapshot()
    {
        lock (sync)
        {
            return metrics.ToArray();
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            metrics.Clear();
        }
    }
}
