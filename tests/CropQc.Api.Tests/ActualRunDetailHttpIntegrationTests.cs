using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text;
using CropQc.Data;
using CropQc.Data.Entities;
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

public sealed class ActualRunDetailHttpIntegrationTests
{
    [Fact]
    public async Task GrowerSummaryUpload_UsesAuthenticatedMvcPathWithoutDuplicatingRunOrChangingInventory()
    {
        await using var factory = new ActualRunWebApplicationFactory();
        long actualRunId;
        int adjustmentCountBefore;
        int adjustmentQuantityBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            actualRunId = await SeedPackoutActualRunAsync(db, historicalReconstruction: true);
            adjustmentCountBefore = await db.RoomInventoryAdjustments.CountAsync();
            adjustmentQuantityBefore = await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var detail = await client.GetAsync($"/BinsRun/ActualRuns/{actualRunId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailHtml = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Historical Reconstructed Benchmark", detailHtml, StringComparison.Ordinal);
        Assert.Contains("This benchmark was reconstructed after the physical run", detailHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("No eligible size observations were available when this expectation was frozen", detailHtml, StringComparison.Ordinal);
        var tokenMatch = Regex.Match(detailHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(tokenMatch.Success, "Actual Run detail did not render an antiforgery token for Packout upload.");
        var token = WebUtility.HtmlDecode(tokenMatch.Groups["token"].Value);

        using var firstUpload = GrowerSummaryUpload(token);
        var firstResponse = await client.PostAsync($"/BinsRun/ActualRuns/{actualRunId}/Packout", firstUpload);
        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        var reviewLocation = firstResponse.Headers.Location?.OriginalString;
        Assert.Matches("^/BinsRun/Packout/[0-9]+$", reviewLocation ?? "");
        var reviewResponse = await client.GetAsync(reviewLocation);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewHtml = await reviewResponse.Content.ReadAsStringAsync();
        Assert.Contains("Reconstructed benchmark score", reviewHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Projection accuracy<", reviewHtml, StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var run = await db.PackoutRuns.Include(x => x.Lines).Include(x => x.Sources).SingleAsync();
            Assert.Equal(18, run.Lines.Count);
            Assert.Equal(4616m, run.Lines.Sum(x => x.Quantity));
            Assert.Equal("DelimitedText", Assert.Single(run.Sources).ParserName);
            Assert.Equal(18, run.Lines.Count(x => x.RequiresReview));
            Assert.All(run.Lines, line =>
            {
                Assert.Null(line.PackCodeDefinitionId);
                Assert.Null(line.NetWeightPounds);
            });
            Assert.Equal(0, await db.PackCodeDefinitions.CountAsync());
            Assert.Equal(adjustmentCountBefore, await db.RoomInventoryAdjustments.CountAsync());
            Assert.Equal(adjustmentQuantityBefore, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        }

        using var duplicateUpload = GrowerSummaryUpload(token);
        var duplicateResponse = await client.PostAsync($"/BinsRun/ActualRuns/{actualRunId}/Packout", duplicateUpload);
        Assert.Equal(HttpStatusCode.Redirect, duplicateResponse.StatusCode);
        Assert.Equal(reviewLocation, duplicateResponse.Headers.Location?.OriginalString);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(1, await db.PackoutRuns.CountAsync());
            Assert.Equal(adjustmentCountBefore, await db.RoomInventoryAdjustments.CountAsync());
            Assert.Equal(adjustmentQuantityBefore, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        }
    }

    [Fact]
    public async Task ActualRunGroup_LinkLoadsDetailAndSupportingDocumentArea()
    {
        await using var factory = new ActualRunWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var now = DateTimeOffset.Parse("2026-07-31T09:00:00-07:00");
        db.Users.Add(new User
        {
            Email = ApplicationAreas.OwnerEmail,
            DisplayName = "Integration Test Owner",
            IsActive = true,
            CreatedAt = now
        });
        db.ActualRuns.Add(new ActualRun
        {
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 1,
            RunAt = now,
            Notes = "Legacy run with no expectation and no uploaded supporting document.",
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);

        var groupResponse = await client.GetAsync("/BinsRun?Section=Actual");
        var groupHtml = await groupResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);

        var link = Regex.Match(
            groupHtml,
            """href="(?<href>/BinsRun/ActualRuns/\d+)">Open Actual Run, Run Expectation, and Packout Result</a>""",
            RegexOptions.CultureInvariant);
        Assert.True(link.Success, "The rendered Actual Run group did not contain the expected detail link.");

        var detailResponse = await client.GetAsync(WebUtility.HtmlDecode(link.Groups["href"].Value));
        var detailHtml = await detailResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Contains("Actual Run", detailHtml, StringComparison.Ordinal);
        Assert.Contains("This legacy Actual Run does not have a frozen Run Expectation", detailHtml, StringComparison.Ordinal);
        Assert.Contains("Packout Result and supporting documents", detailHtml, StringComparison.Ordinal);
        Assert.Contains("Packout report files", detailHtml, StringComparison.Ordinal);
    }

    private sealed class ActualRunWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"actual-run-http-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:EnsureCreatedOnStartup"] = "true",
                    ["Database:SeedMasterDataOnStartup"] = "false",
                    ["Backups:Enabled"] = "false",
                    ["EbsDailyBinsEmail:Enabled"] = "false",
                    ["RENDER_EXTERNAL_HOSTNAME"] = "integration-test.local"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<CropQcDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
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

    private static MultipartFormDataContent GrowerSummaryUpload(string token)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("2026-07-28"), "PackingDate");
        content.Add(new StringContent("1"), "RunNumber");
        content.Add(new StringContent("10"), "DumpedBins");
        var report = new ByteArrayContent(Encoding.UTF8.GetBytes(GrowerSummaryFixture.Text));
        report.Headers.ContentType = new("text/plain");
        content.Add(report, "Files", "grower-summary.txt");
        return content;
    }

    private static async Task<long> SeedPackoutActualRunAsync(CropQcDbContext db, bool historicalReconstruction = false)
    {
        var now = DateTimeOffset.Parse("2026-07-28T16:00:00Z");
        var user = new User
        {
            Email = ApplicationAreas.OwnerEmail,
            DisplayName = "Packout HTTP Test Owner",
            IsActive = true,
            CreatedAt = now
        };
        var warehouse = new Warehouse { Id = 9610, Code = "HTTPWP", Name = "HTTP WP", IsActive = true };
        var room = new Room
        {
            Id = 9611,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Code = "HTTP-WP-1",
            Name = "HTTP WP 1",
            IsActive = true
        };
        var actualRun = new ActualRun
        {
            Id = 9620,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 1,
            RunAt = now,
            CreatedAt = now,
            CreatedByUser = user
        };
        var revision = new ActualRunRevision
        {
            Id = 9621,
            ActualRun = actualRun,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "http-packout-create",
            IsCurrent = true,
            CreatedAt = now
        };
        var adjustment = new RoomInventoryAdjustment
        {
            Id = 9630,
            ActualRun = actualRun,
            ActualRunRevision = revision,
            Warehouse = warehouse,
            Room = room,
            CropYear = 2026,
            GrowerName = "Grower 1084",
            LotNumber = "LOT:1084",
            VarietyCode = "BART",
            OldBinCount = 20,
            ChangeAmount = -10,
            NewBinCount = 10,
            AdjustmentType = BinsRunService.AdjustmentType,
            AdjustmentAt = now,
            CreatedAt = now,
            InventoryInvariantVersion = 1,
            InventoryOperationKey = "http-packout-depletion"
        };
        var entry = new BinsRunEntry
        {
            Id = 9640,
            ActualRun = actualRun,
            ActualRunRevision = revision,
            InventoryAdjustment = adjustment,
            Warehouse = warehouse,
            Room = room,
            CropYear = 2026,
            GrowerName = "Grower 1084",
            LotNumber = "LOT:1084",
            VarietyCode = "BART",
            PreviousAvailableBins = 20,
            BinsRun = 10,
            NewAvailableBins = 10,
            RunAt = now,
            CreatedAt = now,
            TransactionType = ActualRunTransactionTypes.Depletion
        };
        var expectation = new RunExpectation
        {
            Id = 9650,
            ActualRun = actualRun,
            ActualRunRevision = revision,
            RevisionNumber = 1,
            FacilityWarehouseId = warehouse.Id,
            FacilitySnapshot = "WP",
            RunAtSnapshot = now,
            TotalBins = 10,
            GrossPounds = 9200m,
            ExpectedPackoutPercent = 80m,
            ExpectedPackedPounds = 7360m,
            ExpectedPackedBoxes = 184m,
            ExpectedWholeBoxes = 184,
            ExpectedCullPounds = 1840m,
            ExpectedJuicePounds = 736m,
            ExpectedPeelerPounds = 644m,
            ExpectedWastePounds = 460m,
            ConfidencePercent = 90m,
            SizeDistributionSnapshotJson = "{}",
            GradeDistributionSnapshotJson = "{}",
            ConfigurationSnapshotJson = "{}",
            CalculationVersion = RunExpectationCalculationVersions.Current,
            CalculatedAt = now
        };
        if (historicalReconstruction)
        {
            RunExpectationMetadata.MarkHistoricalReconstruction(
                expectation,
                now.AddDays(11),
                actualRun.RunAt,
                "http-test-reconstruction");
        }
        expectation.Sources.Add(new RunExpectationSource
        {
            Id = 9651,
            RunExpectation = expectation,
            BinsRunEntry = entry,
            WarehouseId = warehouse.Id,
            RoomId = room.Id,
            FacilitySnapshot = "WP",
            RoomSnapshot = room.Code,
            CropYearSnapshot = 2026,
            GrowerSnapshot = "Grower 1084",
            LotSnapshot = "LOT:1084",
            VarietySnapshot = "Bartlett",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            BinsContributed = 10,
            ContributionPercent = 100m,
            QcFruitCountSnapshot = 25,
            QcMeasurementSnapshotJson = "{}",
            SizeDistributionSnapshotJson = "{}",
            GradeDistributionSnapshotJson = "{}",
            GrossPounds = 9200m,
            ExpectedPackedPounds = 7360m,
            ExpectedWholeBoxes = 184,
            ExpectedCullPounds = 1840m,
            ConfidencePercent = 90m
        });
        db.AddRange(user, warehouse, room, actualRun, revision, adjustment, entry, expectation);
        await db.SaveChangesAsync();
        return actualRun.Id;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ActualRunIntegration";

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
