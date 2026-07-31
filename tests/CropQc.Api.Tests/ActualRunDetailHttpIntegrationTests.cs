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
