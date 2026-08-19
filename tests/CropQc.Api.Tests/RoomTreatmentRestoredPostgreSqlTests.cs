using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CropQc.Data;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class RoomTreatmentRestoredPostgreSqlTests
{
    private static readonly DateTimeOffset RehearsalNow = DateTimeOffset.Parse("2026-08-18T18:00:00Z");

    [Fact]
    public async Task Restored_production_application_and_authenticated_routes_preserve_operational_quantities_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_ROOM_TREATMENT_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);

        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new CropQcDbContext(options);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var activeRoomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
        var all = await ledger.GetSnapshotsAsync(null, activeRoomIds, default);
        var roomSnapshots = all
            .Where(x => x.CurrentBins > 0 && (x.FruitType == "Apple" || x.FruitType == "Pear"))
            .GroupBy(x => x.RoomId)
            .Where(x => x.Select(y => y.FruitType).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .OrderByDescending(x => x.Sum(y => y.CurrentBins))
            .First()
            .ToList();
        var roomId = roomSnapshots[0].RoomId;
        var crop = roomSnapshots[0].FruitType == "Pear" ? "Pears" : "Apples";
        var chemicalId = await db.TreatmentChemicals.AsNoTracking()
            .Where(x => x.IsActive && x.Crop == crop)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .FirstAsync();

        var before = await ProtectedCountsAsync(db);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "RestoredTest"))
        };
        var treatmentService = new RoomTreatmentService(
            db,
            ledger,
            new AdminAccess(),
            new FixedHttpContextAccessor(context),
            new PacificBusinessTimeService(new FixedClock(RehearsalNow)),
            NullLogger<RoomTreatmentService>.Instance);
        var operationKey = "restored-room-treatment-rehearsal-20260818-v2";
        Assert.False(await db.RoomTreatmentApplications.AnyAsync(x => x.OperationKey == operationKey));

        var applied = await treatmentService.ApplyAsync(new RoomTreatmentApplyForm
        {
            RoomId = roomId,
            TreatmentChemicalId = chemicalId,
            AppliedAt = RehearsalNow,
            OperationKey = operationKey,
            Notes = "Disposable restored-production treatment rehearsal",
            ConfirmedReview = true
        }, default);

        Assert.Null(applied.Error);
        var application = await db.RoomTreatmentApplications.AsNoTracking().Include(x => x.Sources)
            .SingleAsync(x => x.Id == applied.ApplicationId);
        Assert.Equal(roomSnapshots.Sum(x => x.CurrentBins), application.TotalBinsSnapshot);
        Assert.Equal(application.TotalBinsSnapshot, application.Sources.Sum(x => x.BinsTreated));
        Assert.Equal(before, await ProtectedCountsAsync(db));
        var roomData = await treatmentService.GetRoomDataAsync(roomId, default);
        Assert.Contains(roomData.Current, x => x.Treatments.Any(y => y.Id == application.Id));

        await using var factory = new RestoreWebApplicationFactory(connectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var routes = new Dictionary<string, string>
        {
            ["/"] = "Dashboard",
            ["/Rooms"] = "Apply Treatment",
            [$"/Rooms/{roomId}"] = "Current Treatment Status",
            [$"/Rooms/{roomId}/Treatments/Apply"] = "Apply Treatment",
            ["/MasterData/treatment-chemicals"] = "Treatment Chemicals",
            ["/Admin/RoomInventory"] = "Current Inventory Baseline",
            ["/BinsRun"] = "Runs &amp; Transfers",
            ["/EndOfDayFill"] = "End of Day Fill",
            ["/RunReporting/Growers"] = "Grower &amp; Lot Progress"
        };
        var stopwatch = Stopwatch.StartNew();
        foreach (var route in routes)
        {
            var response = await client.GetAsync(route.Key);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(route.Value, html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Npgsql", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HTTP 500", html, StringComparison.OrdinalIgnoreCase);
        }
        stopwatch.Stop();

        Assert.Equal(before, await ProtectedCountsAsync(db));
        Console.WriteLine(
            $"Restored treatment rehearsal: room={roomSnapshots[0].Facility}/{roomSnapshots[0].Room}; " +
            $"fruitRows={roomSnapshots.Count}; bins={application.TotalBinsSnapshot}; application={application.Id}; " +
            $"routes={routes.Count}; elapsedMs={stopwatch.ElapsedMilliseconds}; " +
            $"workingSetBytes={Process.GetCurrentProcess().WorkingSet64}; peakWorkingSetBytes={Process.GetCurrentProcess().PeakWorkingSet64}.");
    }

    private static async Task<(int Adjustments, long AdjustmentDelta, int Receipts, int GrowerLots, int Transfers, int Losses, int BinsRuns, int ActualRuns, int QcSamples, int EodSends, int RunExpectations)> ProtectedCountsAsync(CropQcDbContext db) =>
        (
            await db.RoomInventoryAdjustments.CountAsync(),
            await db.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount),
            await db.Receipts.CountAsync(),
            await db.GrowerLots.CountAsync(),
            await db.RoomTransfers.CountAsync(),
            await db.RoomInventoryLosses.CountAsync(),
            await db.BinsRunEntries.CountAsync(),
            await db.ActualRuns.CountAsync(),
            await db.QcSamples.CountAsync(),
            await db.EndOfDayFillReportSends.CountAsync(),
            await db.RunExpectations.CountAsync()
        );

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
                ["RENDER_EXTERNAL_HOSTNAME"] = "room-treatment-restored-rehearsal.local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
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
        public const string SchemeName = "RoomTreatmentRestoredRehearsal";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class AdminAccess : IUserAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(PageAccessLevel.Admin);
        public void InvalidateAll() { }
    }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
