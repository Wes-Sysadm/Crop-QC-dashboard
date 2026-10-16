using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class ReceiptLocationHttpTests
{
    [InventoryPostgresFact]
    public async Task Admin_edit_antiforgery_correction_and_audit_readback_use_real_postgres_queries()
    {
        await using var f = await ReceiptLocationCorrectionTests.Hundred();
        await using (var seed = f.CreateDbContext())
        {
            (await seed.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
            await seed.SaveChangesAsync();
        }
        await using var host = new ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var unauthorized = host.CreateClient(new() { AllowAutoRedirect = false });
        unauthorized.DefaultRequestHeaders.Add("X-Receipt-Test-User", "no-receipts-admin@example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthorized.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        var page = await client.GetAsync("/Receipts/100000/Edit");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Correct current inventory location", html); Assert.Contains("100 bins", html);
        Assert.Contains("CorrectionSourceRoomId", html); Assert.DoesNotContain("could not be translated", html);
        var hidden = ReceiptCorrectionRenderedForm.Read(html);
        hidden["RoomId"] = "9008"; hidden["CorrectionSourceRoomId"] = "9002";
        hidden["Reason"] = "HTTP receipt location correction"; hidden["ConfirmInventoryChange"] = "true";
        var rejected = new Dictionary<string, string>(hidden); rejected.Remove("__RequestVerificationToken");
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(rejected))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        var response = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(hidden));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Receipts/100000", response.Headers.Location!.ToString());
        var audit = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        await using var check = f.CreateDbContext();
        Assert.Equal("HTTP receipt location correction", await check.ReceiptInventoryOverrides.Select(x => x.Reason).SingleAsync());
        Assert.Equal(0, await f.Physical()); Assert.Equal(100, await f.Physical(9008));
        var detail = await client.GetAsync("/Receipts/100000");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.DoesNotContain("could not be translated", await detail.Content.ReadAsStringAsync());
    }

    internal sealed class ReceiptHost(string connection, bool truckCustody = false) : WebApplicationFactory<ReceiptsController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:CropQc"] = connection,
                ["Database:EnsureCreatedOnStartup"] = "false",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["Email:Provider"] = "None"
                ,
                ["TruckReceiptReconciliation:Enabled"] = truckCustody.ToString()
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<CanonicalInventoryMode>(); services.AddSingleton(new CanonicalInventoryMode(true));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "ReceiptLocal"; options.DefaultChallengeScheme = "ReceiptLocal"; })
                    .AddScheme<AuthenticationSchemeOptions, LocalAuth>("ReceiptLocal", _ => { });
            });
        }
    }
    private sealed class LocalAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, Request.Headers["X-Receipt-Test-User"].FirstOrDefault() ?? ApplicationAreas.OwnerEmail)], "ReceiptLocal")), "ReceiptLocal")));
    }
}
