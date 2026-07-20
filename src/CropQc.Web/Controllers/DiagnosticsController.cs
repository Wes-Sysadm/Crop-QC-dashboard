using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/Diagnostics")]
[Authorize(Policy = AccessPolicyNames.ConfigurationAdmin)]
public sealed class DiagnosticsController(
    IPerformanceRequestMetricSink metricSink,
    PerformanceDiagnosticsOptions diagnosticsOptions,
    AppEnvironmentOptions appEnvironment) : Controller
{
    [HttpGet("Requests")]
    public IActionResult Requests()
    {
        if (!appEnvironment.IsStaging)
        {
            return NotFound();
        }

        var model = new PerformanceDiagnosticsPageViewModel(
            appEnvironment.DisplayName,
            diagnosticsOptions.RecentRequestLimit,
            metricSink.Snapshot()
                .OrderByDescending(x => x.CapturedAt)
                .Select(x => new PerformanceRequestMetricViewModel(
                    x.CapturedAt,
                    x.Method,
                    x.Path,
                    x.EndpointName,
                    x.StatusCode,
                    x.ElapsedMilliseconds,
                    x.EfCommandCount,
                    x.EfCommandElapsedMilliseconds,
                    x.EfCommandFailureCount,
                    x.ResponseBytes,
                    x.ExternalCallCount,
                    FormatExternalProviders(x.ExternalProviderCounts),
                    x.WarningThresholdExceeded,
                    x.TraceIdentifier))
                .ToArray());

        return View(model);
    }

    private static string FormatExternalProviders(IReadOnlyDictionary<string, int> providers) =>
        providers.Count == 0
            ? "None"
            : string.Join(", ", providers.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value}"));
}
