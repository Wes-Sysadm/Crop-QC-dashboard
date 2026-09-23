using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using CropQc.Data;
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

public sealed class TruckReceiptHttpTests
{
    [Theory]
    [InlineData("MatchTransfer")]
    [InlineData("TransferVarieties")]
    [InlineData("CompleteTransfer")]
    [InlineData("ReopenTransfer")]
    [InlineData("EditTransit")]
    public async Task Authenticated_mutations_require_antiforgery(string action)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        await using var factory = new Factory(f);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Test-Email", f.Actor.Email);
        var path = action == "EditTransit" ? "/BinsRun/InterCrewTransfers/1/EditTransit" : $"/Receipts/1/{action}";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task Receipt_screen_requires_manual_selection_and_completes_with_real_antiforgery_token()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        await using var factory = new Factory(f);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add("Test-Email", f.Actor.Email);
        var page = await client.GetAsync($"/Receipts/{receipt.Id}/MatchTransfer");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains($"Select Transfer #{transfer.Id}", html);
        Assert.DoesNotContain("Complete Receipt", html);
        var current = await f.FormAsync(receipt.Id, transfer.Id);
        var post = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(html),
            ["TransferId"] = transfer.Id.ToString(),
            ["TransferVersion"] = current.TransferVersion.ToString(),
            ["ReceiptVersion"] = current.ReceiptVersion.ToString()
        };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync($"/Receipts/{receipt.Id}/MatchTransfer", new FormUrlEncodedContent(post))).StatusCode);
        page = await client.GetAsync($"/Receipts/{receipt.Id}/MatchTransfer");
        html = await page.Content.ReadAsStringAsync();
        Assert.Contains("ready to complete", html);
        f.Db.ChangeTracker.Clear();
        current = await f.FormAsync(receipt.Id, transfer.Id);
        post["__RequestVerificationToken"] = Token(html);
        post["TransferVersion"] = current.TransferVersion.ToString(); post["ReceiptVersion"] = current.ReceiptVersion.ToString();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync($"/Receipts/{receipt.Id}/CompleteTransfer", new FormUrlEncodedContent(post))).StatusCode);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
        Assert.Contains("Completed", await client.GetStringAsync($"/Receipts/{receipt.Id}/MatchTransfer"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/BinsRun/InterCrewTransfers/{transfer.Id}/Reconciliation")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_receipt_and_transfer_contexts_require_authentication()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        await using var factory = new Factory(f);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Receipts/1/MatchTransfer")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/BinsRun/InterCrewTransfers/1/Reconciliation")).StatusCode);
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed class Factory(TruckReceiptReconciliationTests.Fixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:EnsureCreatedOnStartup"] = "false",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["RENDER_EXTERNAL_HOSTNAME"] = "integration-test.local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>(); services.RemoveAll<IHostedService>();
                services.AddSingleton(fixture.Options); services.AddScoped<CropQcDbContext>();
                services.RemoveAll<IUserAccessService>(); services.AddSingleton<IUserAccessService>(fixture.Access);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "TruckTest"; options.DefaultChallengeScheme = "TruckTest"; })
                    .AddScheme<AuthenticationSchemeOptions, Authentication>("TruckTest", _ => { });
            });
        }
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Headers["Test-Email"].ToString();
            return Task.FromResult(string.IsNullOrWhiteSpace(email) ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "TruckTest")), "TruckTest")));
        }
    }
}
