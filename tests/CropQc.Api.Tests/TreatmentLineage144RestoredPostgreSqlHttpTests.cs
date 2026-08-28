using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CropQc.Data;
using CropQc.Data.Entities;
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
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class TreatmentLineage144RestoredPostgreSqlHttpTests
{
    [Fact]
    public async Task TransferRoutes_ReproduceOrClearExactRestoredProductionBlocker_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TREATMENT_LINEAGE_144_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var expectReady = string.Equals(
            Environment.GetEnvironmentVariable("CROPQC_EXPECT_TREATMENT_LINEAGE_TRANSFER_READY"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        await using var db = new CropQcDbContext(
            new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options);
        var before = await ProtectedCountsAsync(db);
        var receiptId = await db.Receipts.AsNoTracking().Where(x => !x.IsDeleted).OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
        var sampleId = await db.QcSamples.AsNoTracking().Where(x => !x.IsDeleted).OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
        var roomId = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var actualRunId = await db.ActualRuns.AsNoTracking().OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
        await using var factory = new RestoreWebApplicationFactory(connectionString, transferCustodyEnabled: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);

        var disabledRoutes = new[]
        {
            "/",
            "/Receipts",
            $"/Receipts/{receiptId}",
            "/DailyQc",
            $"/Samples/{sampleId}",
            "/FieldSamples",
            "/Rooms",
            $"/Rooms/{roomId}",
            "/Inventory/ByVariety",
            "/Admin/RoomInventory",
            "/Admin/RoomInventory/Reconciliation",
            "/GrowerLots/Current",
            "/BinsRun?Section=Planner",
            "/BinsRun?Section=Actual",
            $"/BinsRun/ActualRuns/{actualRunId}",
            "/BinsRun?Section=RunTotals",
            "/BinsRun?Section=Transfer",
            "/BinsRun?Section=Transfer&Facility=EBS",
            "/ProcessorShipments",
            "/MasterData",
            "/MasterData/outside-warehouses",
            "/health",
            "/health/db",
            "/health/master-data"
        };
        foreach (var route in disabledRoutes)
        {
            var response = await client.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("could not be translated", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NpgsqlOperationInProgressException", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HTTP 500", html, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var route in new[] { "/BinsRun?Section=Transfer", "/BinsRun?Section=Transfer&Facility=EBS" })
        {
            var response = await client.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Internal Room Transfer", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TransferType=InterCrew", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TransferType=Outside", html, StringComparison.OrdinalIgnoreCase);
            if (expectReady)
                Assert.DoesNotContain("Treatment lineage requires review", html, StringComparison.OrdinalIgnoreCase);
        }

        await using var enabledFactory = new RestoreWebApplicationFactory(connectionString, transferCustodyEnabled: true);
        using var enabledClient = AuthenticatedClient(enabledFactory);
        var enabledTransfer = await enabledClient.GetStringAsync("/BinsRun?Section=Transfer&Facility=EBS");
        Assert.Contains("TransferType=InterCrew", enabledTransfer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TransferType=Outside", enabledTransfer, StringComparison.OrdinalIgnoreCase);
        var enabledOutside = await enabledClient.GetStringAsync("/BinsRun?Section=Transfer&TransferType=Outside&Facility=EBS");
        if (!expectReady)
            Assert.Contains("Treatment lineage requires review", enabledOutside, StringComparison.OrdinalIgnoreCase);

        if (expectReady)
        {
            Assert.Contains("Shared Receiving Queue", await enabledClient.GetStringAsync("/BinsRun?Section=Transfer&TransferType=InterCrew"), StringComparison.OrdinalIgnoreCase);
            await using var ebsFactory = new RestoreWebApplicationFactory(connectionString, "rob@earlbrownandsons.com", true);
            using var ebsClient = AuthenticatedClient(ebsFactory);
            var ebsQueue = await ebsClient.GetStringAsync("/BinsRun?Section=Transfer&TransferType=InterCrew");
            Assert.Contains("EBS Receiving Queue", ebsQueue, StringComparison.OrdinalIgnoreCase);
            await using var wpFactory = new RestoreWebApplicationFactory(connectionString, "ada@wp-packing.com", true);
            using var wpClient = AuthenticatedClient(wpFactory);
            var wpQueue = await wpClient.GetStringAsync("/BinsRun?Section=Transfer&TransferType=InterCrew");
            Assert.Contains("WP / DH Receiving Queue", wpQueue, StringComparison.OrdinalIgnoreCase);
            foreach (var route in new[]
            {
                "/BinsRun?Section=Transfer&TransferType=InterCrew",
                "/BinsRun?Section=Transfer&TransferType=InterCrew&Facility=EBS",
                "/BinsRun?Section=Transfer&TransferType=Outside",
                "/BinsRun?Section=Transfer&TransferType=Outside&Facility=EBS"
            })
            {
                var response = await enabledClient.GetAsync(route);
                var html = await response.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain("could not be translated", html, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("HTTP 500", html, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(before, await ProtectedCountsAsync(db));
    }

    [Fact]
    public async Task TransferCustody_FullSyntheticWorkflow_PassesOnCorrectedRestoredPostgreSql_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TREATMENT_LINEAGE_144_WORKFLOW_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);

        await using var factory = new RestoreWebApplicationFactory(connectionString, transferCustodyEnabled: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)],
                TestAuthenticationHandler.SchemeName))
        };
        var outsideService = scope.ServiceProvider.GetRequiredService<IOutsideWarehouseTransferService>();
        var interCrewService = scope.ServiceProvider.GetRequiredService<IInterCrewTransferService>();
        var ownerId = await db.Users.Where(x => x.Email == ApplicationAreas.OwnerEmail).Select(x => x.Id).SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var outside = new OutsideWarehouse
        {
            Code = "REHEARSAL-114",
            Name = "Disposable Rehearsal Warehouse",
            Address = "Disposable database only",
            Notes = "Transfer Custody release rehearsal",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = ownerId,
            UpdatedByUserId = ownerId
        };
        db.OutsideWarehouses.Add(outside);
        await db.SaveChangesAsync();

        var outsideSource = (await outsideService.GetInventoryAsync(default))
            .Where(x => x.IsAvailable && !x.IsRoomSealed && x.AvailableBins >= 3)
            .OrderByDescending(x => x.AvailableBins)
            .First();
        var outsideForm = new OutsideWarehouseTransferForm
        {
            OperationKey = "release-114-outside-partial",
            OutsideWarehouseId = outside.Id,
            SourceKey = outsideSource.SourceKey,
            ExpectedAvailableBins = outsideSource.AvailableBins,
            BinCount = 3,
            TransferredAt = DateTime.Now,
            TruckLoadBolNumber = "REHEARSAL-114",
            Notes = "Disposable partial outbound proof",
            ConfirmedReview = true
        };
        var outsideCreated = await outsideService.CreateAsync(outsideForm, default);
        Assert.True(outsideCreated.Success, outsideCreated.Error);
        var outsideDuplicate = await outsideService.CreateAsync(outsideForm, default);
        Assert.True(outsideDuplicate.Success);
        Assert.True(outsideDuplicate.AlreadyApplied);
        Assert.Null(await outsideService.ReverseAsync(new OutsideWarehouseTransferReversalForm
        {
            TransferId = outsideCreated.TransferId!.Value,
            OperationKey = "release-114-outside-reversal",
            Reason = "Disposable rehearsal reversal"
        }, default));

        var destinationRoomId = await UnsealedDestinationRoomIdAsync(db, "EBS");
        var exactSource = (await outsideService.GetInventoryAsync(default))
            .Where(x => x.IsAvailable && !x.IsRoomSealed && x.AvailableBins >= 7
                && (x.Facility.Equals("WP", StringComparison.OrdinalIgnoreCase)
                    || x.Facility.Equals("DH", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.AvailableBins)
            .First();
        var exactDispatch = await interCrewService.DispatchAsync(new InterCrewDispatchForm
        {
            OperationKey = "release-114-exact-dispatch",
            SourceKey = exactSource.SourceKey,
            ExpectedAvailableBins = exactSource.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 6,
            LoadedAt = DateTime.Now,
            TruckLoadBolNumber = "REHEARSAL-114-EXACT",
            ConfirmedReview = true
        }, default);
        Assert.True(exactDispatch.Success, exactDispatch.Error);
        var exactReceive = await interCrewService.ReceiveAsync(new InterCrewReceiveForm
        {
            TransferId = exactDispatch.TransferId!.Value,
            OperationKey = "release-114-exact-receive",
            DestinationRoomId = destinationRoomId,
            BinsReceived = 6,
            ReceivedAt = DateTime.Now,
            Note = "Exact-count disposable receipt"
        }, default);
        Assert.True(exactReceive.Success, exactReceive.Error);
        Assert.Equal(InterCrewTransferStatuses.Received,
            await db.InterCrewTransfers.Where(x => x.Id == exactDispatch.TransferId).Select(x => x.Status).SingleAsync());
        Assert.Null(await interCrewService.ReverseAsync(new InterCrewReversalForm
        {
            TransferId = exactDispatch.TransferId.Value,
            OperationKey = "release-114-exact-reversal",
            Reason = "Disposable exact-count reversal"
        }, default));

        var varianceSource = (await outsideService.GetInventoryAsync(default))
            .Where(x => x.IsAvailable && !x.IsRoomSealed && x.AvailableBins >= 8
                && (x.Facility.Equals("WP", StringComparison.OrdinalIgnoreCase)
                    || x.Facility.Equals("DH", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.AvailableBins)
            .First();
        var varianceDispatch = await interCrewService.DispatchAsync(new InterCrewDispatchForm
        {
            OperationKey = "release-114-variance-dispatch",
            SourceKey = varianceSource.SourceKey,
            ExpectedAvailableBins = varianceSource.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 7,
            LoadedAt = DateTime.Now,
            TruckLoadBolNumber = "REHEARSAL-114-VARIANCE",
            ConfirmedReview = true
        }, default);
        Assert.True(varianceDispatch.Success, varianceDispatch.Error);
        var varianceReceive = await interCrewService.ReceiveAsync(new InterCrewReceiveForm
        {
            TransferId = varianceDispatch.TransferId!.Value,
            OperationKey = "release-114-variance-receive",
            DestinationRoomId = destinationRoomId,
            BinsReceived = 5,
            ReceivedAt = DateTime.Now,
            Note = "Two-bin disposable variance"
        }, default);
        Assert.True(varianceReceive.Success, varianceReceive.Error);
        var variance = await db.InterCrewTransfers.SingleAsync(x => x.Id == varianceDispatch.TransferId);
        Assert.Equal(InterCrewTransferStatuses.ReceivedNeedsReview, variance.Status);
        Assert.Equal(-2, variance.VarianceBins);
        Assert.Null(await interCrewService.ReviewAsync(new InterCrewReviewForm
        {
            TransferId = variance.Id,
            OperationKey = "release-114-variance-review",
            Note = "Reviewed in disposable rehearsal"
        }, default));
        Assert.Null(await interCrewService.ReverseAsync(new InterCrewReversalForm
        {
            TransferId = variance.Id,
            OperationKey = "release-114-variance-reversal",
            Reason = "Disposable variance reversal"
        }, default));

        var beforeReceiveSource = (await outsideService.GetInventoryAsync(default))
            .Where(x => x.IsAvailable && !x.IsRoomSealed && x.AvailableBins >= 4
                && (x.Facility.Equals("WP", StringComparison.OrdinalIgnoreCase)
                    || x.Facility.Equals("DH", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.AvailableBins)
            .First();
        var beforeReceive = await interCrewService.DispatchAsync(new InterCrewDispatchForm
        {
            OperationKey = "release-114-before-receive-dispatch",
            SourceKey = beforeReceiveSource.SourceKey,
            ExpectedAvailableBins = beforeReceiveSource.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 4,
            LoadedAt = DateTime.Now,
            ConfirmedReview = true
        }, default);
        Assert.True(beforeReceive.Success, beforeReceive.Error);
        Assert.Null(await interCrewService.ReverseAsync(new InterCrewReversalForm
        {
            TransferId = beforeReceive.TransferId!.Value,
            OperationKey = "release-114-before-receive-reversal",
            Reason = "Disposable pre-receipt reversal"
        }, default));

        Assert.Equal(3, await db.InterCrewTransfers.CountAsync());
        Assert.All(await db.InterCrewTransfers.ToListAsync(), x => Assert.Equal(InterCrewTransferStatuses.Reversed, x.Status));
        Assert.Single(await db.OutsideWarehouseTransfers.Where(x => x.OperationKey == outsideForm.OperationKey).ToListAsync());
        Assert.True(await db.OutsideWarehouseTransfers.Where(x => x.OperationKey == outsideForm.OperationKey).Select(x => x.IsReversed).SingleAsync());
        Assert.False(await db.RoomInventoryAdjustments.AnyAsync(x => x.ChangeAmount < 0 && x.NewBinCount < 0));
        var readiness = await scope.ServiceProvider.GetRequiredService<ITreatmentLineageReadinessService>().VerifyAsync(default);
        Assert.True(readiness.Success, readiness.Message);
    }

    private static async Task<int> UnsealedDestinationRoomIdAsync(CropQcDbContext db, string facility)
    {
        var candidates = await db.Rooms.AsNoTracking().Include(x => x.Warehouse)
            .Where(x => x.IsActive && x.Warehouse.Code == facility)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var roomId in candidates)
        {
            var latest = await db.RoomSealEvents.AsNoTracking()
                .Where(x => x.RoomId == roomId && x.EffectiveAt <= now)
                .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id)
                .Select(x => x.Action)
                .FirstOrDefaultAsync();
            if (latest != RoomSealActions.Seal) return roomId;
        }
        throw new InvalidOperationException($"No unsealed active {facility} destination room exists in the restored database.");
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        return client;
    }

    private static async Task<(int Adjustments, long AdjustmentDelta, int Receipts, int BinsRuns, int ActualRuns, int Movements)> ProtectedCountsAsync(
        CropQcDbContext db) =>
        (
            await db.RoomInventoryAdjustments.CountAsync(),
            await db.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount),
            await db.Receipts.CountAsync(),
            await db.BinsRunEntries.CountAsync(),
            await db.ActualRuns.CountAsync(),
            await db.TreatmentLineageMovements.CountAsync()
        );

    private sealed class RestoreWebApplicationFactory(
        string connectionString,
        string email = ApplicationAreas.OwnerEmail,
        bool transferCustodyEnabled = false) : WebApplicationFactory<Program>
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
                ["TransferCustody:Enabled"] = transferCustodyEnabled.ToString(),
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft"] = "Error",
                ["RENDER_EXTERNAL_HOSTNAME"] = "treatment-lineage-144-restored.local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.AddSingleton(new TestIdentity(email));
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
        UrlEncoder encoder,
        TestIdentity testIdentity)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TreatmentLineage144RestoredRehearsal";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, testIdentity.Email)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed record TestIdentity(string Email);
}
