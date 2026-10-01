using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CropQc.Data;
using CropQc.Shared.Inventory;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
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

public sealed class InventoryAvailabilityHostTests
{
    [Fact]
    public async Task Web_and_API_hosts_resolve_the_same_shared_contract_and_answer()
    {
        await using var web = new InventoryFactory<InventoryShadowController>();
        await using var api = new InventoryFactory<CropQc.Api.Controllers.ReceiptsController>();
        var webResult = await Resolve(web.Services);
        var apiResult = await Resolve(api.Services);
        Assert.Equal(JsonSerializer.Serialize(webResult), JsonSerializer.Serialize(apiResult));
        Assert.Equal(19, webResult.AvailableQuantity);
    }

    [Fact]
    public async Task Shadow_endpoint_requires_admin_and_serves_readonly_comparison()
    {
        await using var factory = new InventoryFactory<InventoryShadowController>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymous = await client.GetAsync("/Admin/InventoryShadow?roomId=9002");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Inventory-Test-Email", "operator@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/InventoryShadow?roomId=9002")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Inventory-Test-Email");
        client.DefaultRequestHeaders.Add("X-Inventory-Test-Email", ApplicationAreas.OwnerEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Admin/InventoryShadow")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Admin/InventoryShadow?custodyRecordId=1")).StatusCode);
        await Resolve(factory.Services);
        var response = await client.GetAsync("/Admin/InventoryShadow?roomId=9002");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(19, json.RootElement.GetProperty("positions")[0].GetProperty("canonical").GetProperty("availableQuantity").GetInt32());
        Assert.True(response.Headers.CacheControl?.NoStore);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        Assert.Equal(29, (await db.TreatmentLineageSegments.SingleAsync()).CurrentBins);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    private static async Task<InventoryAvailabilityResult> Resolve(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await InventoryAvailabilityDatabaseTests.SeedAsync(scope.ServiceProvider.GetRequiredService<CropQcDbContext>(), 1);
        var resolver = scope.ServiceProvider.GetRequiredService<IInventoryAvailability>();
        Assert.IsType<InventoryAvailabilityResolver>(resolver);
        return Assert.Single((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
    }

    private sealed class InventoryFactory<T> : WebApplicationFactory<T> where T : class
    {
        private readonly string database = Guid.NewGuid().ToString();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:EnsureCreatedOnStartup"] = "false",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["DataProtection:PersistKeysToFileSystem"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<CropQcDbContext>(x => x.UseInMemoryDatabase(database));
                services.AddAuthentication(x =>
                {
                    x.DefaultAuthenticateScheme = "InventoryTest";
                    x.DefaultChallengeScheme = "InventoryTest";
                    x.DefaultForbidScheme = "InventoryTest";
                }).AddScheme<AuthenticationSchemeOptions, InventoryAuthentication>("InventoryTest", _ => { });
            });
        }
    }
    private sealed class InventoryAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Headers["X-Inventory-Test-Email"].ToString();
            return Task.FromResult(email.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Email, email)], Scheme.Name)), Scheme.Name)));
        }
    }
}
