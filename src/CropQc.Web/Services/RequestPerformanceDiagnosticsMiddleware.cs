using System.Diagnostics;
using System.Security.Claims;

namespace CropQc.Web.Services;

public sealed class RequestPerformanceDiagnosticsMiddleware(
    RequestDelegate next,
    ILogger<RequestPerformanceDiagnosticsMiddleware> logger,
    PerformanceDiagnosticsOptions options)
{
    public async Task InvokeAsync(HttpContext context, IPerformanceQueryCounter queryCounter)
    {
        if (!options.Enabled || (!options.RequestTimingEnabled && !options.EfQueryCountingEnabled))
        {
            await next(context);
            return;
        }

        queryCounter.Reset();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            var queryCount = queryCounter.Count;
            var userIdentifier = options.IncludeUserIdentifier
                ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(ClaimTypes.Email)
                : null;

            if (options.EfQueryCountingEnabled
                && options.QueryCountWarningThreshold is { } threshold
                && queryCount > threshold)
            {
                logger.LogWarning(
                    "Request {RequestMethod} {RequestPath} completed with {StatusCode} in {ElapsedMilliseconds} ms using {EfQueryCount} EF commands. TraceIdentifier: {TraceIdentifier}. UserIdentifier: {UserIdentifier}.",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    elapsedMilliseconds,
                    queryCount,
                    context.TraceIdentifier,
                    userIdentifier);
            }
            else
            {
                logger.LogInformation(
                    "Request {RequestMethod} {RequestPath} completed with {StatusCode} in {ElapsedMilliseconds} ms using {EfQueryCount} EF commands. TraceIdentifier: {TraceIdentifier}. UserIdentifier: {UserIdentifier}.",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    elapsedMilliseconds,
                    queryCount,
                    context.TraceIdentifier,
                    userIdentifier);
            }
        }
    }
}
