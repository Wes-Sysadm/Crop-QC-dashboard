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

public sealed class EndOfDayFillWarehousePostgreSqlHttpTests
{
    [Fact]
    public async Task AuthenticatedFourWarehousePreviews_AreIndependentAndReconcileWithoutWrites_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_EOD_WAREHOUSE_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new RestoreWebApplicationFactory(connectionString);
        var before = await ProtectedCountsAsync(factory.Services);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IEndOfDayFillService>();
        var inventorySource = scope.ServiceProvider.GetRequiredService<IEndOfDayFillInventorySource>();
        var groups = await db.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Rooms)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.WarehouseId)
            .ToListAsync();

        Assert.Equal(4, groups.Count);
        Assert.Equal(
            [(4, "WP", "WP End of Day Fill"), (3, "McDougall", "MCD End of Day Fill"), (2, "DH", "DH End of Day Fill"), (1, "EBS", "EBS End of Day Fill")],
            groups.Select(x => (x.WarehouseId, x.Warehouse.Code, x.Name)).ToArray());
        Assert.All(groups, group => Assert.All(group.Rooms, room => Assert.Equal(group.WarehouseId, room.WarehouseId)));

        var expectedLabels = new Dictionary<int, string> { [4] = "WP", [3] = "MCD", [2] = "DH", [1] = "EBS" };
        var combinedPreviewBins = 0;
        foreach (var group in groups)
        {
            var preview = await service.GetPreviewAsync(ApplicationAreas.OwnerEmail, group.Id, CancellationToken.None);
            Assert.Equal(group.Id, preview.SelectedGroupId);
            Assert.Equal(group.WarehouseId, preview.WarehouseId);
            Assert.Equal(expectedLabels[group.WarehouseId], preview.WarehouseLabel);
            Assert.DoesNotContain(preview.Issues, x => x.Code != "gmail");
            Assert.Equal(preview.RoomSummary.TotalCurrentBins, preview.Rooms.Sum(x => x.CurrentBins));
            Assert.Equal(group.Rooms.Count(x => x.IsActive), preview.ConfiguredRoomCount);
            Assert.Equal(preview.Rooms.Count, preview.OccupiedRoomCount);
            Assert.Equal(preview.RoomSummary.TotalCurrentBins, preview.RoomSummary.Rooms.Sum(x => x.CurrentBins));
            Assert.Equal(group.Rooms.Where(x => x.IsActive).Sum(x => x.CapacityBins), preview.RoomSummary.TotalCapacityBins);
            Assert.Equal(preview.RoomSummary.TotalCapacityBins, preview.RoomSummary.Rooms.Sum(x => x.CapacityBins));
            Assert.Equal(
                group.Rooms.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Select(x => x.Id),
                preview.RoomSummary.Rooms.Select(x => x.RoomId));
            Assert.NotNull(preview.RoomSummary.TotalPercentFull);
            Assert.Equal(
                decimal.Round(preview.RoomSummary.TotalCurrentBins * 100m / preview.RoomSummary.TotalCapacityBins, 1),
                preview.RoomSummary.TotalPercentFull);
            Assert.Equal(preview.RoomSummary.TotalCurrentBins, preview.Rooms.Sum(x => x.Varieties.Sum(v => v.Growers.Sum(g => g.Bins))));
            Assert.All(preview.RoomSummary.Rooms.Where(x => x.CurrentBins == 0), x =>
            {
                Assert.Equal(0m, x.PercentFull);
                Assert.Empty(x.Varieties);
            });
            combinedPreviewBins += preview.RoomSummary.TotalCurrentBins;

            var response = await client.GetAsync($"/EndOfDayFill?groupId={group.Id}");
            var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(group.Name, body, StringComparison.Ordinal);
            Assert.Contains($"Warehouse {expectedLabels[group.WarehouseId]}", body, StringComparison.Ordinal);
            Assert.Contains("Room Summary", body, StringComparison.Ordinal);
            Assert.Contains("Detailed Room Breakdown", body, StringComparison.Ordinal);
            Assert.All(preview.RoomSummary.Rooms, room => Assert.Contains($">{room.RoomCode}<", body, StringComparison.Ordinal));
            AssertNoDatabaseTranslationFailure(body);
        }

        var includedRoomIds = groups.SelectMany(x => x.Rooms).Where(x => x.IsActive).Select(x => x.Id).Distinct().ToArray();
        var allWarehouseRoomIds = await db.Rooms.AsNoTracking()
            .Where(x => x.IsActive && new[] { 1, 2, 3, 4 }.Contains(x.WarehouseId))
            .Select(x => x.Id)
            .ToArrayAsync();
        var authoritativeIncludedBins = (await inventorySource.GetCurrentLotsAsync(includedRoomIds, CancellationToken.None)).Sum(x => x.CurrentBins);
        var excludedRoomIds = allWarehouseRoomIds.Except(includedRoomIds).ToArray();
        var excludedBins = excludedRoomIds.Length == 0
            ? 0
            : (await inventorySource.GetCurrentLotsAsync(excludedRoomIds, CancellationToken.None)).Sum(x => x.CurrentBins);
        Assert.Equal(authoritativeIncludedBins, combinedPreviewBins);
        Assert.Equal(authoritativeIncludedBins + excludedBins, combinedPreviewBins + excludedBins);
        Assert.Equal(before, await ProtectedCountsAsync(factory.Services));
    }

    private static void AssertNoDatabaseTranslationFailure(string body)
    {
        Assert.DoesNotContain("could not be translated", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP 500", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProtectedCounts> ProtectedCountsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        return new(
            await db.EndOfDayFillReportSends.CountAsync(),
            await db.EndOfDayFillReportRecipients.CountAsync(),
            await db.RoomInventoryAdjustments.CountAsync(),
            await db.AuditLogs.CountAsync());
    }

    private sealed record ProtectedCounts(int Sends, int Recipients, int InventoryAdjustments, int AuditLogs);

    private sealed class RestoreWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
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
                ["RENDER_EXTERNAL_HOSTNAME"] = "eod-warehouse-rehearsal.local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "EodWarehouseRehearsal";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
