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
    public async Task Authenticated_post_v2_restore_routes_render_latest_names_and_preserve_transferred_qc_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_REVIEWED_GROWER_V2_POSTGRES");
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
            Assert.Equal("WP ORCHARD ORG CHIL", current1080.GrowerName);
            Assert.Equal("1080", current1080.GrowerNumber);

            var resolver = await scope.ServiceProvider.GetRequiredService<ICanonicalGrowerService>()
                .LoadResolutionSetAsync(CancellationToken.None);
            Assert.Equal("MFR - FUJI BLK E ORG", resolver.DisplayName("MFR - FUJI BLK E", "1050"));
            Assert.Equal("WP ORCHARD ORG CHIL", resolver.DisplayName("WINDY POINT", "1080"));
            Assert.Equal("EAST POINT ORG", resolver.DisplayName("WP ORCHARD", "1082"));
            Assert.Equal("WP ORCHARD CONV", resolver.DisplayName("WP ORCHARD", "1084"));
            Assert.Equal("Baldwin Pears ORG", resolver.DisplayName("BALDWIN PEAR", "1530"));
            Assert.Equal("MFR - HOOKER PL CONV", resolver.DisplayName("MFR - HOOKER PL CONV", "9392"));
            Assert.True(await db.CanonicalGrowerNumbers.AsNoTracking().AnyAsync(x => x.GrowerNumber == "9392" && x.IsActive));
            var tr108869 = await db.Receipts.AsNoTracking().SingleAsync(x => x.CompuTechReceiptId == "TR108869");
            Assert.Equal(243, tr108869.Id);
            Assert.Equal("9392", tr108869.GrowerNumber);
            var sample263 = await db.QcSamples.AsNoTracking().SingleAsync(x => x.Id == 263);
            Assert.Equal(243, sample263.ReceiptId);
            Assert.True(await db.RoomTransfers.AsNoTracking().AnyAsync(x => x.LotNumber == "9392"));
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
            [$"/Receipts/{receipt1080Id}"] = "WP ORCHARD ORG CHIL",
            [$"/Receipts/{receipt1080Id}/Edit"] = "WP ORCHARD ORG CHIL",
            [$"/Rooms/{room1080Id}"] = "WP ORCHARD ORG CHIL",
            [$"/BinsRun/ActualRuns/{actualRun1080Id}"] = "WP ORCHARD ORG CHIL"
        };
        foreach (var page in pages)
        {
            var response = await client.GetAsync(page.Key);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(page.Value, body, StringComparison.OrdinalIgnoreCase);
            AssertNoDatabaseTranslationFailure(body);
        }

        var receiving1080 = await GetOkAsync(client, "/Receipts?Grower=WINDY%20POINT");
        Assert.Contains("WP ORCHARD ORG CHIL", receiving1080, StringComparison.Ordinal);
        Assert.DoesNotContain("Grower mapping needed", receiving1080, StringComparison.OrdinalIgnoreCase);

        const string prior1082Name = "WP Orchard - EP Non-Chilean";
        var receiving1082 = await GetOkAsync(client, "/Receipts?Grower=WP%20Orchard%20-%20EP%20Non-Chilean");
        Assert.Contains("EAST POINT ORG", receiving1082, StringComparison.Ordinal);
        Assert.DoesNotContain("Grower mapping needed", receiving1082, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var inventoryService = scope.ServiceProvider.GetRequiredService<IRoomInventoryImportService>();
            var inventoryModel = await inventoryService.GetPageAsync(
                new RoomInventoryImportForm { Grower = prior1082Name },
                CancellationToken.None);
            var inventoryEvidence = string.Join(" | ", inventoryModel.CurrentLots.Select(
                x => $"{x.GrowerNumber}:{x.Grower}:{x.LotNumber}:{x.CurrentBins}"));
            Assert.True(
                inventoryModel.CurrentLots.Any(x => x.Grower == "EAST POINT ORG"),
                inventoryEvidence);
        }

        var inventory = await GetOkAsync(client, "/Admin/RoomInventory?Grower=WP%20Orchard%20-%20EP%20Non-Chilean");
        Assert.Contains("EAST POINT ORG", inventory, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(inventory);

        var receipt1080 = await GetOkAsync(client, "/Receipts?Grower=1080");
        Assert.Contains("WP ORCHARD ORG CHIL", receipt1080, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(receipt1080);

        var runReporting1080 = await GetOkAsync(client, "/RunReporting/Growers?GrowerNumber=1080");
        Assert.Contains("WP ORCHARD ORG CHIL", runReporting1080, StringComparison.Ordinal);
        Assert.Contains("1080", runReporting1080, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(runReporting1080);

        var receipt9392 = await GetOkAsync(client, $"/Receipts/{receipt9392Id}");
        Assert.Contains("MFR - HOOKER PL CONV", receipt9392, StringComparison.Ordinal);
        AssertNoDatabaseTranslationFailure(receipt9392);

        Assert.Equal(before, await CaptureProtectedTotalsAsync(factory.Services));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.True(await db.CanonicalGrowerNumbers.AsNoTracking().AnyAsync(x => x.GrowerNumber == "9392" && x.IsActive));
            Assert.Equal(243, await db.QcSamples.AsNoTracking().Where(x => x.Id == 263).Select(x => x.ReceiptId).SingleAsync());
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
            await db.RunExpectations.CountAsync(),
            await db.RunExpectationSources.CountAsync(),
            await db.GrowerLots.CountAsync(),
            await db.QcSamples.CountAsync(),
            await db.QcFruitReadings.CountAsync(),
            await db.QcPhotos.CountAsync(),
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
        int RunExpectations,
        int RunExpectationSources,
        int GrowerLots,
        int QcSamples,
        int QcFruitReadings,
        int QcPhotos,
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
                    ["RENDER_EXTERNAL_HOSTNAME"] = "reviewed-grower-v2-rehearsal.local"
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
        public const string SchemeName = "ReviewedGrowerV2Rehearsal";

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
