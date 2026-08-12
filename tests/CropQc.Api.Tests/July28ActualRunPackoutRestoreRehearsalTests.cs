using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
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
using Xunit.Abstractions;

namespace CropQc.Api.Tests;

public sealed class July28ActualRunPackoutRestoreRehearsalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Authenticated_real_pdf_upload_reaches_review_on_explicit_run62_restore()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RUN62_JULY28_REHEARSAL_CONNECTION");
        var pdfPath = Environment.GetEnvironmentVariable("CROPQC_REAL_GROWER_SUMMARY_PDF");
        var apply = Environment.GetEnvironmentVariable("CROPQC_RUN62_JULY28_REHEARSAL_APPLY");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(pdfPath) || apply != "YES") return;
        Assert.True(File.Exists(pdfPath), $"Configured representative Grower Summary PDF was not found: {pdfPath}");

        await using var factory = new RestoreWebApplicationFactory(connectionString);
        int adjustmentCount;
        int adjustmentQuantity;
        int entryCount;
        int entryQuantity;
        int actualRunCount;
        int revisionCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(0, await db.PackCodeDefinitions.CountAsync());
            Assert.Equal(0, await db.PackoutRuns.CountAsync(x => x.ActualRunId == 1));
            var reconstructed = await db.RunExpectations.Include(x => x.Sources)
                .Where(x => x.ActualRunId == 1 || x.RunAtSnapshot == DateTimeOffset.Parse("2026-07-28T05:11:00Z"))
                .ToListAsync();
            Assert.Equal(2, reconstructed.Count);
            var july27 = reconstructed.Single(x => x.ActualRunId != 1);
            var july28 = reconstructed.Single(x => x.ActualRunId == 1);
            Assert.NotEqual(july27.Id, july28.Id);
            Assert.Equal(184, july27.TotalBins);
            Assert.Equal(new long[] { 28, 29, 30 }, july27.Sources.OrderBy(x => x.BinsRunEntryId).Select(x => x.BinsRunEntryId));
            Assert.Equal(184, july28.TotalBins);
            Assert.Equal(31, Assert.Single(july28.Sources).BinsRunEntryId);
            Assert.True(RunExpectationMetadata.TryGetHistoricalReconstruction(july27.ConfigurationSnapshotJson, out var july27Marker));
            Assert.True(RunExpectationMetadata.TryGetHistoricalReconstruction(july28.ConfigurationSnapshotJson, out var july28Marker));
            Assert.NotEqual(july27Marker!.CorrectionPackageIdentifier, july28Marker!.CorrectionPackageIdentifier);
            adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();
            adjustmentQuantity = await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount);
            entryCount = await db.BinsRunEntries.CountAsync();
            entryQuantity = await db.BinsRunEntries.SumAsync(x => x.BinsRun);
            actualRunCount = await db.ActualRuns.CountAsync();
            revisionCount = await db.ActualRunRevisions.CountAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var detail = await client.GetAsync("/BinsRun/ActualRuns/1");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailHtml = await detail.Content.ReadAsStringAsync();
        Assert.DoesNotContain("does not have a frozen Run Expectation", detailHtml, StringComparison.Ordinal);
        Assert.Contains("Historical Reconstructed Benchmark", detailHtml, StringComparison.Ordinal);
        Assert.Contains("This benchmark was reconstructed after the physical run", detailHtml, StringComparison.Ordinal);
        var tokenMatch = Regex.Match(detailHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(tokenMatch.Success, "Actual Run detail did not render an antiforgery token for Packout upload.");
        var token = WebUtility.HtmlDecode(tokenMatch.Groups["token"].Value);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("2026-07-28"), "PackingDate");
        form.Add(new StringContent("1"), "RunNumber");
        form.Add(new StringContent("184"), "DumpedBins");
        var file = new FileInfo(pdfPath);
        var report = new StreamContent(file.OpenRead());
        report.Headers.ContentType = new("application/pdf");
        form.Add(report, "Files", file.Name);
        var response = await client.PostAsync("/BinsRun/ActualRuns/1/Packout", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var reviewLocation = response.Headers.Location?.OriginalString;
        Assert.Matches("^/BinsRun/Packout/[0-9]+$", reviewLocation ?? "");
        var reviewResponse = await client.GetAsync(reviewLocation);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewHtml = await reviewResponse.Content.ReadAsStringAsync();
        Assert.Contains("Reconstructed benchmark score", reviewHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Projection accuracy<", reviewHtml, StringComparison.Ordinal);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var packout = await db.PackoutRuns.Include(x => x.Sources).Include(x => x.Lines)
                .SingleAsync(x => x.ActualRunId == 1);
            Assert.Equal(PackoutRunStatuses.Review, packout.Status);
            Assert.Equal(2026, packout.CropYearSnapshot);
            Assert.Equal(18, packout.Lines.Count);
            Assert.Equal(4616m, packout.Lines.Sum(x => x.Quantity));
            Assert.Equal("PopplerText", Assert.Single(packout.Sources).ParserName);
            Assert.Equal(18, packout.Lines.Count(x => x.RequiresReview));
            Assert.All(packout.Lines, line =>
            {
                Assert.Null(line.PackCodeDefinitionId);
                Assert.Null(line.NetWeightPounds);
            });
            Assert.Equal(0, await db.PackCodeDefinitions.CountAsync());
            Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
            Assert.Equal(adjustmentQuantity, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
            Assert.Equal(entryCount, await db.BinsRunEntries.CountAsync());
            Assert.Equal(entryQuantity, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
            Assert.Equal(actualRunCount, await db.ActualRuns.CountAsync());
            Assert.Equal(revisionCount, await db.ActualRunRevisions.CountAsync());
            foreach (var code in packout.Lines.GroupBy(x => x.RawPackCode ?? "")
                         .OrderBy(x => x.Key))
            {
                output.WriteLine($"PACK CODE {code.Key}: {string.Join(" | ", code.Select(x => x.RawText).Distinct())}");
            }
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
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Logging:LogLevel:Microsoft"] = "Error",
                    ["RENDER_EXTERNAL_HOSTNAME"] = "run62-july28-rehearsal.local"
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
        public const string SchemeName = "Run62July28Rehearsal";

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
