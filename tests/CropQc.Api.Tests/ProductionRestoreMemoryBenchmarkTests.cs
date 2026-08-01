using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CropQc.Data;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class ProductionRestoreMemoryBenchmarkTests
{
    [Fact]
    public async Task ProductionRestore_ReadOnlyRouteMatrix_RecordsBoundedMemoryProfile_WhenConfigured()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("CROPQC_PERF_DATABASE_URL");
        var outputPath = Environment.GetEnvironmentVariable("CROPQC_PERF_OUTPUT");
        if (string.IsNullOrWhiteSpace(databaseUrl) || string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        await using var factory = new ProductionRestoreWebApplicationFactory(databaseUrl);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var roomId = await db.Rooms.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var receiptId = await db.Receipts.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var receiptSampleId = await db.QcSamples.AsNoTracking().Where(x => !x.IsDeleted && x.ReceiptId != null).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var fieldSampleId = await db.QcSamples.AsNoTracking().Where(x => !x.IsDeleted && x.ReceiptId == null).OrderBy(x => x.Id).Select(x => (long?)x.Id).FirstOrDefaultAsync();

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BenchmarkAuthenticationHandler.SchemeName);
        client.Timeout = TimeSpan.FromSeconds(30);

        var routes = new List<BenchmarkRoute>
        {
            new("Dashboard", "/"),
            new("Rooms", "/Rooms?Facility=All"),
            new("CurrentInventory", "/GrowerLots/Current?Facility=All"),
            new("RoomDetail", $"/Rooms/{roomId}"),
            new("Receipts", "/Receipts"),
            new("ReceiptDetail", $"/Receipts/{receiptId}"),
            new("DailyQc", "/DailyQc?facility=All"),
            new("SampleDetail", $"/Samples/{receiptSampleId}"),
            new("SampleRefresh", $"/Samples/{receiptSampleId}/refresh"),
            new("FieldSamples", "/FieldSamples"),
            new("BinsRunActual", "/BinsRun?Section=Actual"),
            new("BinsRunPlanner", "/BinsRun?Section=Planner")
        };
        if (fieldSampleId is not null)
        {
            routes.Add(new BenchmarkRoute("FieldSampleDetail", $"/FieldSamples/{fieldSampleId.Value}"));
        }

        var phases = new List<BenchmarkPhaseResult>();
        phases.Add(await RunPhaseAsync(client, "cold-route-matrix", routes, routes.Count, 1));
        phases.Add(await RunPhaseAsync(client, "rooms-sequential-100", routes.Where(x => x.Name == "Rooms").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "current-inventory-sequential-100", routes.Where(x => x.Name == "CurrentInventory").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "sample-refresh-sequential-100", routes.Where(x => x.Name == "SampleRefresh").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "mixed-concurrency-2", routes, 100, 2));
        phases.Add(await RunPhaseAsync(client, "mixed-concurrency-4", routes, 100, 4));
        phases.Add(await RunPhaseAsync(client, "mixed-concurrency-8", routes, 100, 8));

        var report = new
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Environment = "Production",
            Configuration = "Release",
            Database = "localhost-only verified production backup restore",
            Runtime = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Phases = phases
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Assert.All(phases, phase => Assert.Equal(phase.RequestCount, phase.SuccessfulRequests));
        AssertAllocatedBytesPerRequestAtMost(phases, "rooms-sequential-100", 12 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "current-inventory-sequential-100", 12 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "sample-refresh-sequential-100", 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "mixed-concurrency-8", 4 * 1024 * 1024);
        Assert.True(
            phases.Single(x => x.Name == "mixed-concurrency-8").PeakWorkingSetBytes <= 384L * 1024 * 1024,
            "The concurrency-8 peak working set exceeded the 384 MiB production warning threshold.");
    }

    private static void AssertAllocatedBytesPerRequestAtMost(
        IReadOnlyList<BenchmarkPhaseResult> phases,
        string phaseName,
        long thresholdBytes)
    {
        var phase = phases.Single(x => x.Name == phaseName);
        var perRequest = phase.TotalAllocatedBytes / phase.RequestCount;
        Assert.True(
            perRequest <= thresholdBytes,
            $"{phaseName} allocated {perRequest:N0} bytes/request; limit is {thresholdBytes:N0}.");
    }

    private static async Task<BenchmarkPhaseResult> RunPhaseAsync(
        HttpClient client,
        string name,
        IReadOnlyList<BenchmarkRoute> routes,
        int requestCount,
        int concurrency)
    {
        ForceFullCollection();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var startWorkingSet = process.WorkingSet64;
        var startAllocated = GC.GetTotalAllocatedBytes(precise: true);
        var startCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        var startGc = GC.GetGCMemoryInfo();
        var peakWorkingSet = startWorkingSet;
        var successful = 0;
        long responseBytes = 0;
        using var samplingCancellation = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!samplingCancellation.IsCancellationRequested)
            {
                process.Refresh();
                InterlockedExtensions.Max(ref peakWorkingSet, process.WorkingSet64);
                await Task.Delay(20, samplingCancellation.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        });

        var stopwatch = Stopwatch.StartNew();
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var requests = Enumerable.Range(0, requestCount).Select(async index =>
        {
            await gate.WaitAsync();
            try
            {
                var route = routes[index % routes.Count];
                using var response = await client.GetAsync(route.Path, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new InvalidOperationException($"{route.Name} returned {(int)response.StatusCode}.");
                }

                await using var content = await response.Content.ReadAsStreamAsync();
                var bytes = await DrainAsync(content);
                Interlocked.Add(ref responseBytes, bytes);
                Interlocked.Increment(ref successful);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();
        await Task.WhenAll(requests);
        stopwatch.Stop();
        samplingCancellation.Cancel();
        await sampler.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        process.Refresh();
        var endGc = GC.GetGCMemoryInfo();
        return new BenchmarkPhaseResult(
            name,
            requestCount,
            concurrency,
            successful,
            stopwatch.Elapsed.TotalMilliseconds,
            startWorkingSet,
            process.WorkingSet64,
            peakWorkingSet,
            GC.GetTotalAllocatedBytes(precise: true) - startAllocated,
            GC.CollectionCount(0) - startCollections[0],
            GC.CollectionCount(1) - startCollections[1],
            GC.CollectionCount(2) - startCollections[2],
            startGc.HeapSizeBytes,
            endGc.HeapSizeBytes,
            startGc.FragmentedBytes,
            endGc.FragmentedBytes,
            responseBytes);
    }

    private static async Task<long> DrainAsync(Stream content)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            total += read;
        }
        return total;
    }

    private static void ForceFullCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed class ProductionRestoreWebApplicationFactory(string databaseUrl) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DATABASE_PROVIDER"] = "PostgreSql",
                    ["ConnectionStrings:CropQc"] = databaseUrl,
                    ["Database:EnsureCreatedOnStartup"] = "false",
                    ["Database:SeedMasterDataOnStartup"] = "false",
                    ["Backups:Enabled"] = "false",
                    ["EbsDailyBinsEmail:Enabled"] = "false",
                    ["Email:Provider"] = "None",
                    ["FileStorage:Provider"] = "Local",
                    ["DataProtection:PersistKeysToFileSystem"] = "false",
                    ["PerformanceDiagnostics:Enabled"] = "true",
                    ["PerformanceDiagnostics:RequestTimingEnabled"] = "true",
                    ["PerformanceDiagnostics:EfQueryCountingEnabled"] = "true",
                    ["PerformanceDiagnostics:RecentRequestLimit"] = "5000",
                    ["PerformanceDiagnostics:IncludeUserIdentifier"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.AddDbContext<CropQcDbContext>((serviceProvider, options) =>
                {
                    CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, databaseUrl, sql => sql.CommandTimeout(3));
                    options.AddInterceptors(serviceProvider.GetRequiredService<PerformanceDbCommandInterceptor>());
                });
                foreach (var hostedService in services.Where(x => x.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hostedService);
                }
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = BenchmarkAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = BenchmarkAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, BenchmarkAuthenticationHandler>(BenchmarkAuthenticationHandler.SchemeName, _ => { });
            });
        }
    }

    private sealed class BenchmarkAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ProductionRestoreBenchmark";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed record BenchmarkRoute(string Name, string Path);

    private sealed record BenchmarkPhaseResult(
        string Name,
        int RequestCount,
        int Concurrency,
        int SuccessfulRequests,
        double ElapsedMilliseconds,
        long StartWorkingSetBytes,
        long EndWorkingSetBytes,
        long PeakWorkingSetBytes,
        long TotalAllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        long StartHeapBytes,
        long EndHeapBytes,
        long StartFragmentedBytes,
        long EndFragmentedBytes,
        long ResponseBytes);

    private static class InterlockedExtensions
    {
        public static void Max(ref long target, long value)
        {
            long current;
            while (value > (current = Volatile.Read(ref target))
                && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
