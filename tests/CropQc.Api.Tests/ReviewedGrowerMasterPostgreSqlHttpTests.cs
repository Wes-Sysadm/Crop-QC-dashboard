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
    public async Task Authenticated_run67_routes_render_authoritative_names_without_operational_writes_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RUN67_GROWER_HTTP_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new RestoreWebApplicationFactory(connectionString);
        var before = await CaptureProtectedTotalsAsync(factory.Services);

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
            ["/RunReporting/Growers"] = "Grower &amp; Lot Progress"
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

        Assert.Equal(before, await CaptureProtectedTotalsAsync(factory.Services));
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
                    ["RENDER_EXTERNAL_HOSTNAME"] = "run67-reviewed-grower-rehearsal.local"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
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
        public const string SchemeName = "Run67ReviewedGrowerRehearsal";

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
