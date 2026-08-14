using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CropQc.Data;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class ReviewedGrowerMasterPostgreSqlHttpTests
{
    [Fact]
    public async Task Authenticated_run69_routes_render_authoritative_names_without_operational_writes_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RUN69_GROWER_HTTP_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new RestoreWebApplicationFactory(connectionString);
        var before = await CaptureProtectedTotalsAsync(factory.Services);
        long receipt1080Id;
        long receipt9392Id;
        long actualRun1080Id;
        int room1080Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            receipt1080Id = await db.Receipts.AsNoTracking()
                .Where(x => x.GrowerNumber == "1080")
                .OrderByDescending(x => x.ReceivedAt)
                .Select(x => x.Id)
                .FirstAsync();
            receipt9392Id = await db.Receipts.AsNoTracking()
                .Where(x => x.GrowerNumber == "9392")
                .OrderByDescending(x => x.ReceivedAt)
                .Select(x => x.Id)
                .FirstAsync();
            actualRun1080Id = await db.BinsRunEntries.AsNoTracking()
                .Where(x => x.ActualRunId != null && (x.GrowerNumberSnapshot == "1080" || x.LotNumber == "1080"))
                .OrderByDescending(x => x.RunAt)
                .Select(x => x.ActualRunId!.Value)
                .FirstAsync();
            var roomIds = await db.Rooms.AsNoTracking().Select(x => x.Id).ToListAsync();
            var lots = await scope.ServiceProvider.GetRequiredService<IDashboardDataService>()
                .GetAuthoritativeCurrentRoomLotsAsync(roomIds, CancellationToken.None);
            var current1080 = lots.First(x => x.GrowerNumber == "1080" && x.CurrentBins > 0);
            room1080Id = current1080.RoomId;
            Assert.Equal("WINDY POINT", current1080.GrowerName);
            Assert.Equal("1080", current1080.GrowerNumber);

            var resolver = await scope.ServiceProvider.GetRequiredService<ICanonicalGrowerService>()
                .LoadResolutionSetAsync(CancellationToken.None);
            Assert.Equal("MFR - FUJI ORCH-BLK E", resolver.DisplayName("MFR - FUJI BLK E", "1050"));
            Assert.Equal("WINDY POINT", resolver.DisplayName("WP ORCHARD", "1080"));
            Assert.Equal("WP Orchard - EP Non-Chilean", resolver.DisplayName("WP ORCHARD", "1082"));
            Assert.Equal("Baldwin Pears", resolver.DisplayName("BALDWIN PEAR", "1530"));
            Assert.Equal("MFR - HOOKER PL CONV", resolver.DisplayName("MFR - HOOKER PL CONV", "9392"));
            Assert.False(await db.CanonicalGrowerNumbers.AsNoTracking().AnyAsync(x => x.GrowerNumber == "9392"));
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);

        var pages = new Dictionary<string, string>
        {
            ["/"] = "Dashboard",
            ["/Receipts"] = "Receiving",
            ["/DailyQc"] = "Daily QC",
            ["/Rooms"] = "Rooms",
            ["/GrowerLots/Current"] = "Grower Lots In Storage",
            ["/Admin/RoomInventory"] = "Current Inventory Baseline",
            ["/Admin/RoomInventory/Reconciliation"] = "Inventory Reconciliation",
            ["/BinsRun"] = "Runs &amp; Transfers",
            ["/EndOfDayFill"] = "End of Day Fill",
            ["/RunReporting/Growers"] = "Grower &amp; Lot Progress",
            [$"/Receipts/{receipt1080Id}"] = "WINDY POINT",
            [$"/Receipts/{receipt1080Id}/Edit"] = "WINDY POINT",
            [$"/Rooms/{room1080Id}"] = "WINDY POINT",
            [$"/BinsRun/ActualRuns/{actualRun1080Id}"] = "WINDY POINT"
        };
        foreach (var page in pages)
        {
            var response = await client.GetAsync(page.Key);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(page.Value, body, StringComparison.OrdinalIgnoreCase);
            AssertNoDatabaseTranslationFailure(body);
        }

        var receiving = await GetOkAsync(client, "/Receipts?Grower=WP%20ORCHARD");
        Assert.Contains("WINDY POINT", receiving, StringComparison.Ordinal);
        Assert.Contains("WP Orchard - EP Non-Chilean", receiving, StringComparison.Ordinal);
        Assert.DoesNotContain("Grower mapping needed", receiving, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var inventoryService = scope.ServiceProvider.GetRequiredService<IRoomInventoryImportService>();
            var inventoryModel = await inventoryService.GetPageAsync(
                new RoomInventoryImportForm { Grower = "WP ORCHARD" },
                CancellationToken.None);
            var inventoryEvidence = string.Join(" | ", inventoryModel.CurrentLots.Select(
                x => $"{x.GrowerNumber}:{x.Grower}:{x.LotNumber}:{x.CurrentBins}"));
            Assert.True(
                inventoryModel.CurrentLots.Any(x => x.Grower == "WP Orchard - EP Non-Chilean"),
                inventoryEvidence);
        }

        var inventory = await GetOkAsync(client, "/Admin/RoomInventory?Grower=WP%20ORCHARD");
        Assert.Contains("WP Orchard - EP Non-Chilean", inventory, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(inventory);

        var receipt1080 = await GetOkAsync(client, "/Receipts?Grower=1080");
        Assert.Contains("WINDY POINT", receipt1080, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(receipt1080);

        var runReporting1080 = await GetOkAsync(client, "/RunReporting/Growers?GrowerNumber=1080");
        Assert.Contains("WINDY POINT", runReporting1080, StringComparison.Ordinal);
        Assert.Contains("1080", runReporting1080, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(runReporting1080);

        var receipt9392 = await GetOkAsync(client, $"/Receipts/{receipt9392Id}");
        Assert.Contains("MFR - HOOKER PL CONV", receipt9392, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(receipt9392);

        Assert.Equal(before, await CaptureProtectedTotalsAsync(factory.Services));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.False(await db.CanonicalGrowerNumbers.AsNoTracking().AnyAsync(x => x.GrowerNumber == "9392"));
        }
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body;
    }

    private static void AssertNoDatabaseTranslationFailure(string body)
    {
        Assert.DoesNotContain("could not be translated", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP 500", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProtectedTotals> CaptureProtectedTotalsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        return new(
            await db.Receipts.CountAsync(),
            await db.Receipts.SumAsync(x => (long)x.BinCount),
            await db.RoomInventoryAdjustments.CountAsync(),
            await db.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount),
            await db.RoomTransfers.CountAsync(),
            await db.RoomTransfers.SumAsync(x => (long)x.BinCount),
            await db.RoomInventoryLosses.CountAsync(),
            await db.RoomInventoryLosses.SumAsync(x => (long)x.BinCount),
            await db.BinsRunEntries.CountAsync(),
            await db.BinsRunEntries.SumAsync(x => (long)x.BinsRun),
            await db.ActualRuns.CountAsync(),
            await db.ActualRunRevisions.CountAsync(),
            await db.QcSamples.CountAsync(),
            await db.AuditLogs.CountAsync(),
            await db.EndOfDayFillReportSends.CountAsync());
    }

    private sealed record ProtectedTotals(
        int Receipts,
        long ReceiptBins,
        int Adjustments,
        long AdjustmentBins,
        int Transfers,
        long TransferBins,
        int Losses,
        long LossBins,
        int BinsRunEntries,
        long BinsRunBins,
        int ActualRuns,
        int ActualRunRevisions,
        int QcSamples,
        int AuditLogs,
        int EndOfDaySends);

    private sealed class RestoreWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["ConnectionStrings:CropQc"] = connectionString,
                    ["Database:EnsureCreatedOnStartup"] = "false",
                    ["Database:SeedMasterDataOnStartup"] = "false",
                    ["Backups:Enabled"] = "false",
                    ["EbsDailyBinsEmail:Enabled"] = "false",
                    ["Email:Provider"] = "None",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Logging:LogLevel:Microsoft"] = "Error",
                    ["RENDER_EXTERNAL_HOSTNAME"] = "run69-reviewed-grower-rehearsal.local"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Run69ReviewedGrowerRehearsal";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
