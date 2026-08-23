using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CropQc.Data;
using CropQc.Web.Models;
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
    public async Task ProductionRestore_WpRunTotalsRenderConfiguredZeroSalesDesksReadOnly_WhenConfigured()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("CROPQC_SALES_DESK_RESTORE_POSTGRES");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(databaseUrl);
        await using var factory = new ProductionRestoreWebApplicationFactory(databaseUrl);
        long actualRunCount;
        long correctionCount;
        long binsRunEntryCount;
        long auditLogCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            actualRunCount = await db.ActualRuns.AsNoTracking().LongCountAsync();
            correctionCount = await db.ActualRunSalesDeskCorrections.AsNoTracking().LongCountAsync();
            binsRunEntryCount = await db.BinsRunEntries.AsNoTracking().LongCountAsync();
            auditLogCount = await db.AuditLogs.AsNoTracking().LongCountAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BenchmarkAuthenticationHandler.SchemeName);
        using var response = await client.GetAsync("/BinsRun?Section=RunTotals&ReportFacility=WP&ReportCropYear=2026");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<strong>WP Total</strong><span>4,247 bins</span>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Domex</strong><span>184 bins</span>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Honey Bear</strong><span>0 bins</span>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Viva Tierra</strong><span>0 bins</span>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Unassigned</strong><span>4,063 bins</span>", html, StringComparison.Ordinal);
        Assert.Contains("WP Total reconciles exactly", html, StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(actualRunCount, await db.ActualRuns.AsNoTracking().LongCountAsync());
            Assert.Equal(correctionCount, await db.ActualRunSalesDeskCorrections.AsNoTracking().LongCountAsync());
            Assert.Equal(binsRunEntryCount, await db.BinsRunEntries.AsNoTracking().LongCountAsync());
            Assert.Equal(auditLogCount, await db.AuditLogs.AsNoTracking().LongCountAsync());
            var assignedRun = await db.ActualRuns.AsNoTracking().SingleAsync(x => x.SalesDeskId != null);
            Assert.Equal(20, assignedRun.Id);
            Assert.Equal(1, assignedRun.SalesDeskId);
            Assert.Equal("Domex", assignedRun.SalesDeskNameSnapshot);
        }
    }

    [Fact]
    public async Task ProductionRestore_IncidentBdb7aeaf_RoomInventoryRoutesRemainReadOnlyAndRenderWithoutWarnings_WhenConfigured()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("CROPQC_PERF_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        await using var factory = new ProductionRestoreWebApplicationFactory(databaseUrl);
        long adjustmentCountBefore;
        int adjustmentQuantityBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            adjustmentCountBefore = await db.RoomInventoryAdjustments.AsNoTracking().LongCountAsync();
            adjustmentQuantityBefore = await db.RoomInventoryAdjustments.AsNoTracking().SumAsync(x => x.ChangeAmount);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BenchmarkAuthenticationHandler.SchemeName);
        foreach (var route in new[]
                 {
                     "/?Facility=All", "/?Facility=WP", "/?Facility=EBS",
                     "/Rooms?Facility=All", "/Rooms?Facility=WP", "/Rooms?Facility=EBS",
                     "/GrowerLots/Current?Facility=All", "/GrowerLots/Current?Facility=WP", "/GrowerLots/Current?Facility=EBS"
                 })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Room inventory cards and summaries could not be loaded", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Dashboard data could not be loaded", html, StringComparison.Ordinal);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(adjustmentCountBefore, await db.RoomInventoryAdjustments.AsNoTracking().LongCountAsync());
            Assert.Equal(adjustmentQuantityBefore, await db.RoomInventoryAdjustments.AsNoTracking().SumAsync(x => x.ChangeAmount));
        }
    }

    [Fact]
    public async Task ProductionRestore_ReadOnlyRouteMatrix_RecordsBoundedMemoryProfile_WhenConfigured()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("CROPQC_PERF_DATABASE_URL");
        var outputPath = Environment.GetEnvironmentVariable("CROPQC_PERF_OUTPUT");
        var profile = Environment.GetEnvironmentVariable("CROPQC_PERF_PROFILE") ?? "full";
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
            new("DashboardAll", "/?Facility=All"),
            new("DashboardWP", "/?Facility=WP"),
            new("DashboardEBS", "/?Facility=EBS"),
            new("Rooms", "/Rooms?Facility=All"),
            new("CurrentInventory", "/GrowerLots/Current?Facility=All"),
            new("CurrentInventoryCanonicalAlias", "/GrowerLots/Current?Facility=All&CropYear=2026&Variety=BART"),
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
        if (!string.Equals(profile, "core", StringComparison.OrdinalIgnoreCase))
        {
            var progressService = scope.ServiceProvider.GetRequiredService<IGrowerLotProgressService>();
            var overviewModel = await progressService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "All" }, CancellationToken.None);
            var firstGrower = overviewModel.Growers.FirstOrDefault(x => x.BinsRun > 0) ?? overviewModel.Growers.FirstOrDefault();
            GrowerVarietyProgressViewModel? firstVariety = null;
            GrowerLotProgressViewModel? firstLot = null;
            GrowerLotWeekProgressViewModel? firstWeek = null;
            if (firstGrower is not null)
            {
                var growerModel = await progressService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "All", ExpandedGrowerNumber = firstGrower.GrowerNumber }, CancellationToken.None);
                var growerVarieties = growerModel.Growers.Single(x => x.GrowerNumber == firstGrower.GrowerNumber).Varieties;
                firstVariety = growerVarieties.FirstOrDefault(x => x.BinsRun > 0) ?? growerVarieties.FirstOrDefault();
                if (firstVariety is not null)
                {
                    var lotModel = await progressService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "All", ExpandedGrowerNumber = firstGrower.GrowerNumber, ExpandedVarietyKey = firstVariety.VarietyKey }, CancellationToken.None);
                    var varietyLots = lotModel.Growers.Single(x => x.GrowerNumber == firstGrower.GrowerNumber).Varieties.Single(x => x.VarietyKey == firstVariety.VarietyKey).Lots;
                    firstLot = varietyLots.FirstOrDefault(x => x.BinsRun > 0) ?? varietyLots.FirstOrDefault();
                    if (firstLot is not null)
                    {
                        var weekModel = await progressService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "All", ExpandedGrowerNumber = firstGrower.GrowerNumber, ExpandedVarietyKey = firstVariety.VarietyKey, SelectedLotKey = firstLot.LotKey }, CancellationToken.None);
                        firstWeek = weekModel.Growers.Single(x => x.GrowerNumber == firstGrower.GrowerNumber).Varieties.Single(x => x.VarietyKey == firstVariety.VarietyKey).Lots.Single(x => x.LotKey == firstLot.LotKey).Weeks.FirstOrDefault();
                    }
                }
            }
            var runReporting = scope.ServiceProvider.GetRequiredService<IRunReportingService>();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], BenchmarkAuthenticationHandler.SchemeName));
            var runTotalsModel = await runReporting.GetAsync(new BinsRunFilterForm { Section = "RunTotals", ReportFacility = "WP", ReportCropYear = 2026 }, principal, CancellationToken.None);
            var firstRunVariety = runTotalsModel.Detail?.Varieties.FirstOrDefault();

            routes.Add(new BenchmarkRoute("RunReportingSummary", "/BinsRun?Section=Actual"));
            routes.Add(new BenchmarkRoute("RunReportingNoVariety", "/BinsRun?Section=RunTotals&ReportFacility=WP&ReportCropYear=2026"));
            if (firstRunVariety is not null)
            {
                routes.Add(new BenchmarkRoute("RunReportingSelectedVariety", $"/BinsRun?Section=RunTotals&ReportFacility=WP&ReportCropYear=2026&ReportVarietyKey={Uri.EscapeDataString(firstRunVariety.VarietyKey)}"));
            }
            routes.Add(new BenchmarkRoute("RunReportingNeedsReview", "/BinsRun?Section=NeedsReview"));
            routes.Add(new BenchmarkRoute("GrowerLotProgress", "/RunReporting/Growers?CropYear=2026&Facility=All"));
            if (firstGrower is not null)
            {
                var growerQuery = $"CropYear=2026&Facility=All&ExpandedGrowerNumber={Uri.EscapeDataString(firstGrower.GrowerNumber)}";
                routes.Add(new BenchmarkRoute("GrowerLotSelectedGrower", $"/RunReporting/Growers?{growerQuery}"));
                if (firstVariety is not null)
                {
                    var varietyQuery = $"{growerQuery}&ExpandedVarietyKey={Uri.EscapeDataString(firstVariety.VarietyKey)}";
                    routes.Add(new BenchmarkRoute("GrowerLotSelectedVariety", $"/RunReporting/Growers?{varietyQuery}"));
                    if (firstLot is not null)
                    {
                        var lotQuery = $"{varietyQuery}&SelectedLotKey={Uri.EscapeDataString(firstLot.LotKey)}";
                        routes.Add(new BenchmarkRoute("GrowerLotSelectedLot", $"/RunReporting/Growers?{lotQuery}"));
                        if (firstWeek is not null)
                        {
                            routes.Add(new BenchmarkRoute("GrowerLotSelectedWeek", $"/RunReporting/Growers?{lotQuery}&SelectedWeekStart={firstWeek.WeekStart:yyyy-MM-dd}&SupportingPage=1"));
                        }
                    }
                }
            }
        }
        if (fieldSampleId is not null)
        {
            routes.Add(new BenchmarkRoute("FieldSampleDetail", $"/FieldSamples/{fieldSampleId.Value}"));
        }

        var phases = new List<BenchmarkPhaseResult>();
        phases.Add(await RunPhaseAsync(client, "cold-route-matrix", routes, routes.Count, 1));
        phases.Add(await RunPhaseAsync(client, "dashboard-all-sequential-100", routes.Where(x => x.Name == "DashboardAll").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "dashboard-wp-sequential-100", routes.Where(x => x.Name == "DashboardWP").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "dashboard-ebs-sequential-100", routes.Where(x => x.Name == "DashboardEBS").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "rooms-sequential-100", routes.Where(x => x.Name == "Rooms").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "room-detail-sequential-100", routes.Where(x => x.Name == "RoomDetail").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "current-inventory-sequential-100", routes.Where(x => x.Name == "CurrentInventory").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "current-inventory-canonical-alias-sequential-100", routes.Where(x => x.Name == "CurrentInventoryCanonicalAlias").ToList(), 100, 1));
        phases.Add(await RunPhaseAsync(client, "sample-refresh-sequential-100", routes.Where(x => x.Name == "SampleRefresh").ToList(), 100, 1));
        if (!string.Equals(profile, "core", StringComparison.OrdinalIgnoreCase))
        {
            phases.Add(await RunPhaseAsync(client, "run-reporting-summary-sequential-100", routes.Where(x => x.Name == "RunReportingSummary").ToList(), 100, 1));
            phases.Add(await RunPhaseAsync(client, "run-reporting-no-variety-sequential-100", routes.Where(x => x.Name == "RunReportingNoVariety").ToList(), 100, 1));
            foreach (var routeName in new[] { "RunReportingSelectedVariety", "GrowerLotSelectedGrower", "GrowerLotSelectedVariety", "GrowerLotSelectedLot", "GrowerLotSelectedWeek" })
            {
                var selectedRoutes = routes.Where(x => x.Name == routeName).ToList();
                if (selectedRoutes.Count > 0)
                {
                    phases.Add(await RunPhaseAsync(client, $"{routeName}-sequential-100", selectedRoutes, 100, 1));
                }
            }
            phases.Add(await RunPhaseAsync(client, "run-reporting-needs-review-sequential-100", routes.Where(x => x.Name == "RunReportingNeedsReview").ToList(), 100, 1));
            phases.Add(await RunPhaseAsync(client, "grower-lot-progress-sequential-100", routes.Where(x => x.Name == "GrowerLotProgress").ToList(), 100, 1));
        }
        // WP/EBS dashboard variants have dedicated 100-request phases above. Keep the mixed
        // route weighting aligned with the historical benchmark by retaining one dashboard route.
        var mixedRoutes = routes.Where(x => x.Name is not "DashboardWP" and not "DashboardEBS" and not "CurrentInventoryCanonicalAlias").ToList();
        phases.Add(await RunPhaseAsync(client, "mixed-concurrency-2", mixedRoutes, 100, 2));
        phases.Add(await RunPhaseAsync(client, "mixed-concurrency-4", mixedRoutes, 100, 4));
        phases.Add(await RunPhaseAsync(client, "mixed-concurrency-8", mixedRoutes, 100, 8));
        var retainedMemoryPlateau = new List<BenchmarkPhaseResult>();
        for (var batch = 1; batch <= 5; batch++)
        {
            retainedMemoryPlateau.Add(await RunPhaseAsync(client, $"retained-memory-batch-{batch}", mixedRoutes, 20, 4));
        }

        var report = new
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Environment = "Production",
            Configuration = "Release",
            Database = Environment.GetEnvironmentVariable("CROPQC_PERF_DATASET") ?? "localhost-only verified production backup restore",
            Commit = Environment.GetEnvironmentVariable("CROPQC_PERF_COMMIT") ?? "unknown",
            Profile = profile,
            Runtime = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Phases = phases,
            RetainedMemoryPlateau = retainedMemoryPlateau
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Assert.All(phases, phase => Assert.Equal(phase.RequestCount, phase.SuccessfulRequests));
        Assert.All(retainedMemoryPlateau, phase => Assert.Equal(phase.RequestCount, phase.SuccessfulRequests));
        AssertAllocatedBytesPerRequestAtMost(phases, "dashboard-all-sequential-100", 4 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "dashboard-wp-sequential-100", 4 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "dashboard-ebs-sequential-100", 4 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "rooms-sequential-100", 16 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "current-inventory-sequential-100", 16 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "current-inventory-canonical-alias-sequential-100", 16 * 1024 * 1024);
        AssertAllocatedBytesPerRequestAtMost(phases, "sample-refresh-sequential-100", 1024 * 1024);
        if (!string.Equals(profile, "core", StringComparison.OrdinalIgnoreCase))
        {
            AssertAllocatedBytesPerRequestAtMost(phases, "run-reporting-summary-sequential-100", 16 * 1024 * 1024);
            AssertAllocatedBytesPerRequestAtMost(phases, "run-reporting-no-variety-sequential-100", 16 * 1024 * 1024);
            AssertAllocatedBytesPerRequestAtMost(phases, "run-reporting-needs-review-sequential-100", 16 * 1024 * 1024);
            AssertAllocatedBytesPerRequestAtMost(phases, "grower-lot-progress-sequential-100", 16 * 1024 * 1024);
        }
        // A mixed allocation average changes whenever a broken route starts returning its real
        // result instead of a small error page. Route-specific guards above detect real allocation
        // regressions without rewarding failed requests; concurrency is guarded by working set.
        Assert.True(
            phases.Single(x => x.Name == "mixed-concurrency-8").PeakWorkingSetBytes <= 384L * 1024 * 1024,
            "The concurrency-8 peak working set exceeded the 384 MiB production warning threshold.");
        Assert.True(
            retainedMemoryPlateau[^1].PostIdleWorkingSetBytes <= retainedMemoryPlateau[0].PostIdleWorkingSetBytes + 16L * 1024 * 1024,
            "Post-idle working set increased across retained-memory batches instead of reaching a plateau.");
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
        var routeResults = new ConcurrentDictionary<string, MutableRouteResult>(StringComparer.Ordinal);
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
                routeResults.AddOrUpdate(
                    route.Name,
                    _ => new MutableRouteResult(1, bytes),
                    (_, current) => current.Add(bytes));
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
        var endWorkingSet = process.WorkingSet64;
        ForceFullCollection();
        await Task.Delay(500);
        process.Refresh();
        var postIdleGc = GC.GetGCMemoryInfo();
        return new BenchmarkPhaseResult(
            name,
            requestCount,
            concurrency,
            successful,
            stopwatch.Elapsed.TotalMilliseconds,
            startWorkingSet,
            endWorkingSet,
            peakWorkingSet,
            process.WorkingSet64,
            GC.GetTotalAllocatedBytes(precise: true) - startAllocated,
            GC.CollectionCount(0) - startCollections[0],
            GC.CollectionCount(1) - startCollections[1],
            GC.CollectionCount(2) - startCollections[2],
            startGc.HeapSizeBytes,
            endGc.HeapSizeBytes,
            postIdleGc.HeapSizeBytes,
            LohSize(startGc),
            LohSize(endGc),
            LohSize(postIdleGc),
            startGc.FragmentedBytes,
            endGc.FragmentedBytes,
            postIdleGc.FragmentedBytes,
            responseBytes,
            routeResults.OrderBy(x => x.Key).ToDictionary(
                x => x.Key,
                x => new RouteResult(x.Value.RequestCount, x.Value.ResponseBytes),
                StringComparer.Ordinal));
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

    private static long LohSize(GCMemoryInfo info) =>
        info.GenerationInfo.Length > 3 ? info.GenerationInfo[3].SizeAfterBytes : 0;

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
        long PostIdleWorkingSetBytes,
        long TotalAllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        long StartHeapBytes,
        long EndHeapBytes,
        long PostIdleHeapBytes,
        long StartLohBytes,
        long EndLohBytes,
        long PostIdleLohBytes,
        long StartFragmentedBytes,
        long EndFragmentedBytes,
        long PostIdleFragmentedBytes,
        long ResponseBytes,
        IReadOnlyDictionary<string, RouteResult> RouteResults);

    private sealed record RouteResult(int RequestCount, long ResponseBytes);

    private sealed record MutableRouteResult(int RequestCount, long ResponseBytes)
    {
        public MutableRouteResult Add(long responseBytes) => new(RequestCount + 1, ResponseBytes + responseBytes);
    }

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
