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
            options);

        await middleware.InvokeAsync(new DefaultHttpContext(), counter);

        Assert.Equal(1, counter.Count);
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
    public void DateFilters_UseUtcDayRangesInsteadOfDateMemberEquality()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var station = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "QcStationController.cs"));

        Assert.Contains("UtcDayRange.ForUtcDay(DateTimeOffset.UtcNow)", dashboard);
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
        Assert.Contains("UseMiddleware<RequestPerformanceDiagnosticsMiddleware>", program);
        Assert.Contains("\"PerformanceDiagnostics\"", settings);
        Assert.Contains("\"QueryCountWarningThreshold\"", settings);
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
