using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class PerformanceDiagnosticsTests
{
    [Fact]
    public void PerformanceDiagnostics_DefaultsToEnabledOutsideProduction()
    {
        var options = PerformanceDiagnosticsOptions.FromConfiguration(
            new ConfigurationBuilder().Build(),
            new FakeHostEnvironment("Development"));

        Assert.True(options.Enabled);
        Assert.True(options.RequestTimingEnabled);
        Assert.True(options.EfQueryCountingEnabled);
        Assert.False(options.IncludeUserIdentifier);
        Assert.True(options.LogEveryRequest);
        Assert.False(options.RuntimeMemoryTelemetryEnabled);
    }

    [Fact]
    public void PerformanceDiagnostics_DefaultsToDisabledInProduction()
    {
        var options = PerformanceDiagnosticsOptions.FromConfiguration(
            new ConfigurationBuilder().Build(),
            new FakeHostEnvironment("Production"));

        Assert.False(options.Enabled);
        Assert.False(options.RequestTimingEnabled);
        Assert.False(options.EfQueryCountingEnabled);
        Assert.False(options.LogEveryRequest);
        Assert.False(options.RuntimeMemoryTelemetryEnabled);
    }

    [Fact]
    public void ProductionMemoryTelemetry_IsExplicitClampedAndHasOrderedThresholds()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PerformanceDiagnostics:Enabled"] = "true",
            ["PerformanceDiagnostics:RuntimeMemoryTelemetryEnabled"] = "true",
            ["PerformanceDiagnostics:RuntimeMemoryTelemetryIntervalSeconds"] = "5",
            ["PerformanceDiagnostics:RuntimeMemoryWarningWorkingSetBytes"] = "1000",
            ["PerformanceDiagnostics:RuntimeMemoryCriticalWorkingSetBytes"] = "900",
            ["PerformanceDiagnostics:IncludeUserIdentifier"] = "false"
        }).Build();

        var options = PerformanceDiagnosticsOptions.FromConfiguration(configuration, new FakeHostEnvironment("Production"));

        Assert.True(options.RuntimeMemoryTelemetryEnabled);
        Assert.Equal(60, options.RuntimeMemoryTelemetryIntervalSeconds);
        Assert.Equal(1000, options.RuntimeMemoryWarningWorkingSetBytes);
        Assert.Equal(1001, options.RuntimeMemoryCriticalWorkingSetBytes);
        Assert.False(options.IncludeUserIdentifier);
        Assert.False(options.LogEveryRequest);
    }

    [Theory]
    [InlineData(99, 100, 200, RuntimeMemoryPressureLevel.Normal)]
    [InlineData(100, 100, 200, RuntimeMemoryPressureLevel.Elevated)]
    [InlineData(199, 100, 200, RuntimeMemoryPressureLevel.Elevated)]
    [InlineData(200, 100, 200, RuntimeMemoryPressureLevel.Critical)]
    public void RuntimeMemoryPressureClassifier_UsesConfiguredBoundaries(
        long workingSet,
        long warning,
        long critical,
        RuntimeMemoryPressureLevel expected) =>
        Assert.Equal(expected, RuntimeMemoryPressureClassifier.Classify(workingSet, warning, critical));

    [Fact]
    public void RequestActivityTracker_IsBoundedResettableAndDisposalIsIdempotent()
    {
        var tracker = new RequestActivityTracker();
        var first = tracker.Track();
        using var second = tracker.Track();

        var active = tracker.SnapshotAndResetPeriod();
        Assert.Equal(2, active.ActiveRequests);
        Assert.Equal(2, active.PeakActiveRequests);
        Assert.Equal(2, active.StartedRequests);
        Assert.Equal(0, active.CompletedRequests);

        first.Dispose();
        first.Dispose();
        second.Dispose();
        var completed = tracker.SnapshotAndResetPeriod();
        Assert.Equal(0, completed.ActiveRequests);
        Assert.Equal(2, completed.PeakActiveRequests);
        Assert.Equal(0, completed.StartedRequests);
        Assert.Equal(2, completed.CompletedRequests);

        var reset = tracker.SnapshotAndResetPeriod();
        Assert.Equal(0, reset.ActiveRequests);
        Assert.Equal(0, reset.PeakActiveRequests);
        Assert.Equal(0, reset.StartedRequests);
        Assert.Equal(0, reset.CompletedRequests);
    }

    [Fact]
    public async Task RuntimeMemoryTelemetryHostedService_CancelsPromptly()
    {
        var service = new RuntimeMemoryTelemetryHostedService(
            new PerformanceDiagnosticsOptions
            {
                Enabled = true,
                RuntimeMemoryTelemetryEnabled = true,
                RuntimeMemoryTelemetryIntervalSeconds = 60
            },
            new RequestActivityTracker(),
            NullLogger<RuntimeMemoryTelemetryHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task RequestDiagnostics_ResetQueryCounterForEachRequest()
    {
        var options = new PerformanceDiagnosticsOptions
        {
            Enabled = true,
            RequestTimingEnabled = true,
            EfQueryCountingEnabled = true
        };
        var counter = new PerformanceQueryCounter();
        var externalCounter = new PerformanceExternalCallCounter();
        var metricSink = new BoundedPerformanceRequestMetricSink(options);
        counter.Increment();
        counter.Increment();
        var middleware = new RequestPerformanceDiagnosticsMiddleware(
            context =>
            {
                Assert.Equal(0, counter.Count);
                counter.Increment();
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            NullLogger<RequestPerformanceDiagnosticsMiddleware>.Instance,
            options,
            metricSink,
            externalCounter);

        await middleware.InvokeAsync(new DefaultHttpContext(), counter);

        Assert.Equal(1, counter.Count);
        var metric = Assert.Single(metricSink.Snapshot());
        Assert.Equal(1, metric.EfCommandCount);
        Assert.Equal(StatusCodes.Status204NoContent, metric.StatusCode);
    }

    [Fact]
    public void QueryCounter_CanResetBetweenRequests()
    {
        var counter = new PerformanceQueryCounter();
        counter.Increment();
        counter.Increment();

        counter.Reset();

        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void QueryCounter_TracksDatabaseElapsedAndFailures()
    {
        var counter = new PerformanceQueryCounter();

        counter.Increment();
        counter.AddElapsed(TimeSpan.FromMilliseconds(12.5));
        counter.IncrementFailed();

        Assert.Equal(1, counter.Count);
        Assert.Equal(12.5, counter.ElapsedMilliseconds, precision: 1);
        Assert.Equal(12.5, counter.SlowestCommandMilliseconds, precision: 1);
        Assert.Equal(1, counter.FailedCount);

        counter.Reset();

        Assert.Equal(0, counter.Count);
        Assert.Equal(0, counter.ElapsedMilliseconds);
        Assert.Equal(0, counter.SlowestCommandMilliseconds);
        Assert.Equal(0, counter.FailedCount);
    }

    [Fact]
    public async Task RequestDiagnostics_CapturesResponseSizeAndExternalCalls()
    {
        var options = new PerformanceDiagnosticsOptions
        {
            Enabled = true,
            RequestTimingEnabled = true,
            EfQueryCountingEnabled = true
        };
        var counter = new PerformanceQueryCounter();
        var externalCounter = new PerformanceExternalCallCounter();
        var metricSink = new BoundedPerformanceRequestMetricSink(options);
        var middleware = new RequestPerformanceDiagnosticsMiddleware(
            async context =>
            {
                externalCounter.Increment("GoogleDrive");
                await context.Response.WriteAsync("baseline");
            },
            NullLogger<RequestPerformanceDiagnosticsMiddleware>.Instance,
            options,
            metricSink,
            externalCounter);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, counter);

        var metric = Assert.Single(metricSink.Snapshot());
        Assert.Equal(8, metric.ResponseBytes);
        Assert.Equal(1, metric.ExternalCallCount);
        Assert.Equal(1, metric.ExternalProviderCounts["GoogleDrive"]);
        Assert.True(metric.ProcessAllocatedBytesDelta >= 0);
    }

    [Fact]
    public async Task RequestDiagnostics_DisabledDoesNotRecordMetrics()
    {
        var options = new PerformanceDiagnosticsOptions
        {
            Enabled = false,
            RequestTimingEnabled = false,
            EfQueryCountingEnabled = false
        };
        var counter = new PerformanceQueryCounter();
        var externalCounter = new PerformanceExternalCallCounter();
        var metricSink = new BoundedPerformanceRequestMetricSink(options);
        var middleware = new RequestPerformanceDiagnosticsMiddleware(
            context =>
            {
                counter.Increment();
                externalCounter.Increment("GmailApi");
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            NullLogger<RequestPerformanceDiagnosticsMiddleware>.Instance,
            options,
            metricSink,
            externalCounter);

        await middleware.InvokeAsync(new DefaultHttpContext(), counter);

        Assert.Empty(metricSink.Snapshot());
    }

    [Fact]
    public async Task RequestActivity_IsReleasedWhenRequestFailsBeforeDiagnosticsRun()
    {
        var options = new PerformanceDiagnosticsOptions();
        var tracker = new RequestActivityTracker();
        var middleware = new RequestPerformanceDiagnosticsMiddleware(
            _ => throw new InvalidOperationException("test failure"),
            NullLogger<RequestPerformanceDiagnosticsMiddleware>.Instance,
            options,
            new BoundedPerformanceRequestMetricSink(options),
            new PerformanceExternalCallCounter(),
            tracker);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(new DefaultHttpContext(), new PerformanceQueryCounter()));

        var snapshot = tracker.SnapshotAndResetPeriod();
        Assert.Equal(0, snapshot.ActiveRequests);
        Assert.Equal(1, snapshot.PeakActiveRequests);
        Assert.Equal(1, snapshot.StartedRequests);
        Assert.Equal(1, snapshot.CompletedRequests);
    }

    [Fact]
    public async Task ExternalCallCounter_IsIsolatedAcrossConcurrentRequests()
    {
        var options = new PerformanceDiagnosticsOptions
        {
            Enabled = true,
            RequestTimingEnabled = true,
            EfQueryCountingEnabled = true
        };
        var externalCounter = new PerformanceExternalCallCounter();
        var metricSink = new BoundedPerformanceRequestMetricSink(options);

        async Task InvokeAsync(string provider, int expected)
        {
            var counter = new PerformanceQueryCounter();
            var middleware = new RequestPerformanceDiagnosticsMiddleware(
                async context =>
                {
                    for (var i = 0; i < expected; i++)
                    {
                        externalCounter.Increment(provider);
                        await Task.Yield();
                    }

                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                },
                NullLogger<RequestPerformanceDiagnosticsMiddleware>.Instance,
                options,
                metricSink,
                externalCounter);
            await middleware.InvokeAsync(new DefaultHttpContext(), counter);
        }

        await Task.WhenAll(
            InvokeAsync("GmailApi", 2),
            InvokeAsync("GoogleDrive", 3));

        var metrics = metricSink.Snapshot();
        Assert.Equal(2, metrics.Count);
        Assert.Contains(metrics, x => x.ExternalProviderCounts.TryGetValue("GmailApi", out var count) && count == 2 && x.ExternalCallCount == 2);
        Assert.Contains(metrics, x => x.ExternalProviderCounts.TryGetValue("GoogleDrive", out var count) && count == 3 && x.ExternalCallCount == 3);
    }

    [Fact]
    public void EfCommandInterceptor_DoesNotLogSqlOrParameterValues()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "PerformanceDbCommandInterceptor.cs"));

        Assert.DoesNotContain("CommandText", source);
        Assert.DoesNotContain("Parameters", source);
        Assert.DoesNotContain("LogInformation", source);
        Assert.DoesNotContain("LogWarning", source);
    }

    [Fact]
    public void Layout_AppendsContentVersionToLocalSiteCss()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("href=\"~/css/site.css\"", layout);
        Assert.Contains("asp-append-version=\"true\"", layout);
    }

    [Fact]
    public void DateFilters_UsePacificBusinessDayRangesInsteadOfDateMemberEquality()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var station = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "QcStationController.cs"));

        Assert.Contains("BusinessTime.UtcRangeForPacificDate(BusinessTime.PacificDate(BusinessTime.UtcNow))", dashboard);
        Assert.Contains("x.SampleTakenAt >= todayRange.Start && x.SampleTakenAt < todayRange.End", dashboard);
        Assert.Contains("x.ReceivedAt >= todayRange.Start && x.ReceivedAt < todayRange.End", dashboard);
        Assert.Contains("UtcDayRange.ForUtcDay(DateTimeOffset.UtcNow)", station);
        Assert.DoesNotContain("SampleTakenAt.Date == DateTimeOffset.UtcNow.Date", dashboard);
        Assert.DoesNotContain("ReceivedAt.Date == today", dashboard);
        Assert.DoesNotContain("SampleTakenAt.Date == today", station);
    }

    [Fact]
    public void Program_RegistersPerformanceDiagnosticsMiddlewareAndInterceptor()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var settings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.json"));

        Assert.Contains("PerformanceDiagnosticsOptions.FromConfiguration", program);
        Assert.Contains("PerformanceDbCommandInterceptor", program);
        Assert.Contains("BoundedPerformanceRequestMetricSink", program);
        Assert.Contains("PerformanceExternalCallCounter", program);
        Assert.Contains("RequestActivityTracker", program);
        Assert.Contains("RuntimeMemoryTelemetryHostedService", program);
        Assert.Contains("UseMiddleware<RequestPerformanceDiagnosticsMiddleware>", program);
        Assert.Contains("\"PerformanceDiagnostics\"", settings);
        Assert.Contains("\"QueryCountWarningThreshold\"", settings);
        Assert.Contains("\"ResponseBytesWarningThreshold\"", settings);
    }

    [Fact]
    public void HighTrafficPages_UseBoundedReadPaths()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var binsRunController = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs"));

        Assert.Contains("await BuildRoomLotSummariesAsync(null, cancellationToken)", dashboard);
        var compactStart = dashboard.IndexOf("private async Task<IReadOnlyList<RoomConditionSample>> LoadRoomConditionSamplesAsync", StringComparison.Ordinal);
        var compactEnd = dashboard.IndexOf("private async Task<IReadOnlyList<RoomLotSummaryViewModel>> BuildAdjustmentOnlyLotSummariesAsync", compactStart, StringComparison.Ordinal);
        var compactPath = dashboard[compactStart..compactEnd];
        Assert.Contains("RoomConditionSampleHeader", compactPath);
        Assert.Contains("RoomConditionFruitRowData", compactPath);
        Assert.DoesNotContain(".Include(", compactPath);
        Assert.DoesNotContain(".AsSplitQuery()", compactPath);
        Assert.Contains("activeSection.Equals(\"Planner\"", binsRunController);
        Assert.Contains("? new BinsRunPageViewModel { Filter = filter }", binsRunController);
        Assert.Contains(": await binsRunService.GetPageAsync", binsRunController);
    }

    [Fact]
    public void ProductionTelemetryAndPolling_AreBoundedAndDoNotIncludeUserIdentifiers()
    {
        var render = File.ReadAllText(FindRepositoryFile("render.yaml")).ReplaceLineEndings("\n");
        var autosave = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "field-sample-autosave.js"));
        var sampleView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));

        Assert.Contains("PerformanceDiagnostics__RecentRequestLimit\n        value: 0", render);
        Assert.Contains("PerformanceDiagnostics__IncludeUserIdentifier\n        value: false", render);
        Assert.Contains("PerformanceDiagnostics__LogEveryRequest\n        value: false", render);
        Assert.Contains("PerformanceDiagnostics__RuntimeMemoryTelemetryIntervalSeconds\n        value: 60", render);
        Assert.Contains("Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command\n        value: Warning", render);
        Assert.DoesNotContain("setInterval(refreshFieldSample, 3000)", autosave);
        Assert.Contains("Math.Clamp(parsedRefreshInterval, 15, 300)", sampleView);
    }

    [Fact]
    public void BaselineWorkflowCatalog_CoversRequestedHighTrafficWorkflows()
    {
        var workflows = PerformanceBaselineWorkflowCatalog.Workflows;

        Assert.Equal(19, workflows.Count);
        Assert.Contains(workflows, x => x.Name == "Dashboard initial load");
        Assert.Contains(workflows, x => x.Name == "Crop Year Review initial card list");
        Assert.Contains(workflows, x => x.Name == "Photo metadata section opening");
        Assert.All(workflows, workflow =>
        {
            Assert.False(string.IsNullOrWhiteSpace(workflow.RouteTemplate));
            Assert.False(string.IsNullOrWhiteSpace(workflow.ExpectedScaleSignal));
        });
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? "";
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "CropQc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
