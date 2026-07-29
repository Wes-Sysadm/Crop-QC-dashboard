using System.Diagnostics;
using System.Security.Claims;

namespace CropQc.Web.Services;

public sealed class RequestPerformanceDiagnosticsMiddleware(
    RequestDelegate next,
    ILogger<RequestPerformanceDiagnosticsMiddleware> logger,
    PerformanceDiagnosticsOptions options,
    IPerformanceRequestMetricSink metricSink,
    IPerformanceExternalCallCounter externalCallCounter)
{
    public async Task InvokeAsync(HttpContext context, IPerformanceQueryCounter queryCounter)
    {
        if (!options.Enabled || (!options.RequestTimingEnabled && !options.EfQueryCountingEnabled))
        {
            await next(context);
            return;
        }

        queryCounter.Reset();
        externalCallCounter.Reset();
        var allocatedBytesAtStart = GC.GetTotalAllocatedBytes(precise: false);
        var stopwatch = Stopwatch.StartNew();
        var originalBody = context.Response.Body;
        CountingResponseBodyStream? countingBody = null;
        if (options.RequestTimingEnabled && CanCountResponseBytes(context))
        {
            countingBody = new CountingResponseBodyStream(originalBody);
            context.Response.Body = countingBody;
        }

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            stopwatch.Stop();
            var elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            var queryCount = queryCounter.Count;
            var databaseElapsedMilliseconds = queryCounter.ElapsedMilliseconds;
            var allocatedBytesDelta = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesAtStart);
            var externalProviderCounts = externalCallCounter.ProviderCounts;
            var responseBytes = countingBody?.BytesWritten;
            var userIdentifier = options.IncludeUserIdentifier
                ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(ClaimTypes.Email)
                : null;
            var endpointName = context.GetEndpoint()?.DisplayName;
            var warningThresholdExceeded = ThresholdExceeded(
                elapsedMilliseconds,
                queryCount,
                databaseElapsedMilliseconds,
                responseBytes);

            var metric = new PerformanceRequestMetric(
                DateTimeOffset.UtcNow,
                context.Request.Method,
                context.Request.Path.Value ?? "",
                endpointName,
                context.Response.StatusCode,
                elapsedMilliseconds,
                queryCount,
                databaseElapsedMilliseconds,
                queryCounter.SlowestCommandMilliseconds,
                queryCounter.FailedCount,
                allocatedBytesDelta,
                responseBytes,
                externalCallCounter.TotalCount,
                externalProviderCounts,
                warningThresholdExceeded,
                context.TraceIdentifier,
                userIdentifier);
            metricSink.Add(metric);

            if (warningThresholdExceeded)
            {
                logger.LogWarning(
                    "Request {RequestMethod} {RequestPath} completed with {StatusCode} in {ElapsedMilliseconds} ms using {EfQueryCount} EF commands in {EfElapsedMilliseconds} ms; slowest EF command {SlowestEfCommandMilliseconds} ms; process allocation delta {ProcessAllocatedBytesDelta} bytes; {ResponseBytes} response bytes. External calls: {ExternalCallCount}. TraceIdentifier: {TraceIdentifier}. UserIdentifier: {UserIdentifier}.",
                    metric.Method,
                    metric.Path,
                    metric.StatusCode,
                    metric.ElapsedMilliseconds,
                    metric.EfCommandCount,
                    metric.EfCommandElapsedMilliseconds,
                    metric.SlowestEfCommandMilliseconds,
                    metric.ProcessAllocatedBytesDelta,
                    metric.ResponseBytes,
                    metric.ExternalCallCount,
                    metric.TraceIdentifier,
                    metric.UserIdentifier);
            }
            else
            {
                logger.LogInformation(
                    "Request {RequestMethod} {RequestPath} completed with {StatusCode} in {ElapsedMilliseconds} ms using {EfQueryCount} EF commands in {EfElapsedMilliseconds} ms; slowest EF command {SlowestEfCommandMilliseconds} ms; process allocation delta {ProcessAllocatedBytesDelta} bytes; {ResponseBytes} response bytes. External calls: {ExternalCallCount}. TraceIdentifier: {TraceIdentifier}. UserIdentifier: {UserIdentifier}.",
                    metric.Method,
                    metric.Path,
                    metric.StatusCode,
                    metric.ElapsedMilliseconds,
                    metric.EfCommandCount,
                    metric.EfCommandElapsedMilliseconds,
                    metric.SlowestEfCommandMilliseconds,
                    metric.ProcessAllocatedBytesDelta,
                    metric.ResponseBytes,
                    metric.ExternalCallCount,
                    metric.TraceIdentifier,
                    metric.UserIdentifier);
            }
        }
    }

    private bool ThresholdExceeded(
        double elapsedMilliseconds,
        int queryCount,
        double databaseElapsedMilliseconds,
        long? responseBytes) =>
        options.EfQueryCountingEnabled
            && options.QueryCountWarningThreshold is { } queryThreshold
            && queryCount > queryThreshold
        || options.RequestElapsedWarningThresholdMs is { } elapsedThreshold
            && elapsedMilliseconds > elapsedThreshold
        || options.DatabaseElapsedWarningThresholdMs is { } databaseThreshold
            && databaseElapsedMilliseconds > databaseThreshold
        || options.ResponseBytesWarningThreshold is { } responseThreshold
            && responseBytes is { } bytes
            && bytes > responseThreshold;

    private static bool CanCountResponseBytes(HttpContext context) =>
        !HttpMethods.IsHead(context.Request.Method);
}
