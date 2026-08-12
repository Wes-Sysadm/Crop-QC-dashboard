using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CropQc.Data;
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

public sealed class RoomInventoryLossPostgreSqlHttpTests
{
    [Fact]
    public async Task Authenticated_run64_routes_render_reviewed_loss_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RUN64_ROOM_LOSS_HTTP_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new RestoreWebApplicationFactory(connectionString);
        int adjustmentCount;
        int lossCount;
        int auditCount;
        int endOfDaySendCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();
            lossCount = await db.RoomInventoryLosses.CountAsync();
            auditCount = await db.AuditLogs.CountAsync();
            endOfDaySendCount = await db.EndOfDayFillReportSends.CountAsync();
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
            ["/Rooms"] = "Rooms",
            ["/Rooms/17"] = "EVANS-7",
            ["/Receipts/208"] = "TR108859",
            ["/Admin/RoomInventory"] = "Current Inventory Baseline",
            ["/Admin/RoomInventory/Reconciliation"] = "Inventory Reconciliation",
            ["/BinsRun"] = "Runs &amp; Transfers",
            ["/EndOfDayFill"] = "End of Day Fill",
            ["/RunReporting/Growers"] = "Grower &amp; Lot Progress"
        };
        var html = new Dictionary<string, string>();
        foreach (var page in pages)
        {
            var response = await client.GetAsync(page.Key);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(page.Value, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
            html[page.Key] = body;
        }

        Assert.Contains("<strong>28</strong><span>Received bins</span>", html["/Receipts/208"], StringComparison.Ordinal);
        Assert.Contains("<strong>2</strong><span>Dropped after receiving</span>", html["/Receipts/208"], StringComparison.Ordinal);
        Assert.Contains("Dropped Bin History", html["/Receipts/208"], StringComparison.Ordinal);
        Assert.Contains("Dropped Bin History", html["/Rooms/17"], StringComparison.Ordinal);
        Assert.Contains("Receipt TR108859", html["/Rooms/17"], StringComparison.Ordinal);
        Assert.Contains("246 packable bins", html["/Rooms/17"], StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
            Assert.Equal(lossCount, await db.RoomInventoryLosses.CountAsync());
            Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
            Assert.Equal(endOfDaySendCount, await db.EndOfDayFillReportSends.CountAsync());
        }
    }

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
                    ["RENDER_EXTERNAL_HOSTNAME"] = "run64-room-loss-rehearsal.local"
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
        public const string SchemeName = "Run64RoomLossRehearsal";

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
