using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Security;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class BrowserAntiforgeryTests
{
    // Exact action allowlist, not an /api/* exemption. Both require station code
    // and a verified hashed station key; ordinary browser cookies are insufficient.
    private static readonly string[] MachineActions = ["Heartbeat", "UpdatePressures"];

    [Fact]
    public async Task Architecture_AllUnsafeRoutesAreProtectedAndExemptionsAreExact()
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        var options = factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;
        Assert.Single(options.Filters.OfType<AutoValidateAntiforgeryTokenAttribute>());
        Assert.Empty(options.Filters.OfType<IgnoreAntiforgeryTokenAttribute>());
        var actions = Actions(factory);
        Assert.NotEmpty(actions);
        var exempt = actions.Where(a => a.FilterDescriptors.Any(f => f.Filter is IgnoreAntiforgeryTokenAttribute)).ToArray();
        Assert.Equal(MachineActions, exempt.Select(a => a.ActionName).Distinct().Order().ToArray());
        Assert.All(exempt, a => Assert.Equal(typeof(QcStationController), a.ControllerTypeInfo.AsType()));
        Assert.All(actions, a => Assert.Equal(typeof(QcStationController).Assembly, a.ControllerTypeInfo.Assembly));
        // No unqualified method routing may silently open an unsafe route.
        Assert.All(actions.Where(a => a.ActionConstraints?.OfType<HttpMethodActionConstraint>().Any() != true),
            a => Assert.Contains(a.ActionName, new[] { "Index", "Error" }));
    }

    [Fact]
    public async Task EveryBrowserUnsafeRoute_RejectsMissingAndInvalidToken_WithZeroWrites()
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        var routes = Actions(factory).Where(a => a.ControllerTypeInfo.AsType() != typeof(QcStationController))
            .SelectMany(a => (a.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods) ?? [])
                .Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE")
                .Select(m => (Method: m, Route: a.AttributeRouteInfo!.Template!))).Distinct().ToArray();
        Assert.NotEmpty(routes);
        var before = factory.Writes.Count;
        foreach (var route in routes)
        {
            var url = "/" + Regex.Replace(route.Route, @"\{([^}:?]+)[^}]*\}", m => m.Groups[1].Value == "type" ? "fruit-profiles" : "999999");
            foreach (var token in new[] { "", "invalid-token" })
            {
                using var request = new HttpRequestMessage(new HttpMethod(route.Method), url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token })
                };
                using var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{route.Method} {url}: {response.StatusCode}");
            }
        }
        Assert.Equal(before, factory.Writes.Count);
    }

    [Theory]
    [InlineData("/MasterData/fruit-profiles")]
    [InlineData("/MasterData/grower-lots")]
    [InlineData("/Admin/RoomInventory")]
    [InlineData("/Login")]
    [InlineData("/Admin/Users")]
    [InlineData("/Receipts")]
    [InlineData("/BinsRun")]
    [InlineData("/Admin/Configuration")]
    public async Task RenderedBrowserPostForms_HaveExactlyOneToken(string url)
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var forms = Regex.Matches(html, "<form\\b[^>]*method=\"post\"[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.NotEmpty(forms);
        foreach (Match form in forms)
            Assert.Single(Regex.Matches(form.Value, "name=\"__RequestVerificationToken\""));
    }

    [Fact]
    public async Task ValidCookieAndToken_MasterDataSaveWorks_Batch1BStillBlocksIdentityEdit()
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        var token = await TokenAsync(client);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Type"] = "fruit-profiles",
            ["Name"] = "Test fruit",
            ["Code"] = "CSRFTEST",
            ["FruitType"] = "Apple",
            ["ProductionType"] = "Conventional",
            ["IsActive"] = "true"
        };
        using var save = await client.PostAsync("/MasterData/fruit-profiles/Save", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var id = await factory.WithDbAsync(async db =>
        {
            var profile = await db.FruitProfiles.SingleAsync(p => p.VarietyCode == "CSRFTEST");
            db.Receipts.Add(new Receipt { FruitProfileId = profile.Id, CompuTechReceiptId = "TEST-1", GrowerName = "Test", LotCode = "1" });
            await db.SaveChangesAsync();
            return profile.Id;
        });
        form["Id"] = id.ToString();
        form["ProductionType"] = "Organic";
        var before = factory.Writes.Count;
        using var blocked = await client.PostAsync("/MasterData/fruit-profiles/Save", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        var page = await client.GetStringAsync(blocked.Headers.Location);
        Assert.Contains("operational history", page);
        Assert.Equal(before, factory.Writes.Count);
        Assert.Equal("Conventional", await factory.WithDbAsync(async db => (await db.FruitProfiles.FindAsync(id))!.ProductionType));
    }

    [Theory]
    [InlineData("/Admin/RoomInventory/Preview")]
    [InlineData("/Admin/RoomInventory/Apply")]
    [InlineData("/MasterData/grower-lots/ImportPreview")]
    [InlineData("/MasterData/grower-lots/ImportApply")]
    [InlineData("/Receipts/999999/photos")]
    [InlineData("/Receipts/999999/photos/999999/remove")]
    [InlineData("/Samples/999999/photos")]
    [InlineData("/Samples/999999/photos/999999/remove")]
    public async Task ValidToken_ReachesExistingBusinessValidation(string url)
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        var token = await TokenAsync(client);
        // This success-path fixture starts with established resolver mappings.
        // Cold-start GET seeding is reproduced separately below and blocks Batch 1C.
        if (url.StartsWith("/Admin/RoomInventory", StringComparison.Ordinal))
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICanonicalGrowerService>().LoadResolutionSetAsync(default);
        }
        var before = factory.Writes.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["CsvText"] = "invalid-header" }) };
        request.Headers.Add("RequestVerificationToken", token); // Same supported header as JSON/AJAX clients.
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"{url}: {response.StatusCode}");
        Assert.Empty(factory.Writes.Skip(before).SelectMany(x => x));
    }

    [Fact]
    public async Task AuditFinding_CurrentInventoryGetPersistsCanonicalSeedMappings()
    {
        // Diagnostic reproduction of an unresolved pre-existing GET mutation,
        // NOT a declaration that the behavior is acceptable. See Batch 1C review.
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        var before = factory.Writes.Count;
        using var response = await client.GetAsync("/Admin/RoomInventory");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var properties = factory.Writes.Skip(before).SelectMany(x => x).ToArray();
        Assert.Contains("CanonicalGrower.DisplayName", properties);
        Assert.Contains("CanonicalGrowerAlias.AliasName", properties);
        Assert.All(properties, p => Assert.True(p.StartsWith("CanonicalGrower.", StringComparison.Ordinal)
            || p.StartsWith("CanonicalGrowerAlias.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ValidToken_DoesNotGrantMasterDataPermission()
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync("viewer@example.test");
        var token = await TokenAsync(client);
        var before = factory.Writes.Count;
        using var response = await client.PostAsync("/MasterData/fruit-profiles/Save", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden || response.Headers.Location?.OriginalString.Contains("AccessDenied") == true);
        Assert.Equal(before, factory.Writes.Count);
    }

    [Fact]
    public async Task StationReadsAreReadOnly_HeartbeatWritesOnlyTelemetry_AndPressureAliasesStillWork()
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        var sampleId = await factory.WithDbAsync(async db =>
        {
            db.QcStations.Add(new CropQc.Data.Entities.QcStation
            {
                Name = "Test station",
                StationCode = "TEST",
                ApiKeyHash = QcStationApiKeyValidator.HashApiKey("test-key"),
                IsActive = true,
                LastSeenAt = DateTimeOffset.UnixEpoch,
                LastSeenIp = "old-address"
            });
            var sample = new QcSample
            {
                SampleTypeId = await db.SampleTypes.Where(t => t.Name == "Field Sample").Select(t => t.Id).SingleAsync(),
                SampleTakenAt = DateTimeOffset.UtcNow,
                FieldSampleGrowerName = "Test",
                ActualSampleSize = 10,
                Status = "InProgress",
                StarchStatus = "Pending",
                PhotoStatus = "Pending",
                EmailStatus = "NotSent"
            };
            db.QcSamples.Add(sample);
            await db.SaveChangesAsync();
            return sample.Id;
        });
        client.DefaultRequestHeaders.Add(QcStationApiKeyValidator.StationCodeHeaderName, "TEST");
        client.DefaultRequestHeaders.Add(QcStationApiKeyValidator.HeaderName, "test-key");
        var before = factory.Writes.Count;
        foreach (var url in new[] { "/api/qc-station/samples/today", $"/api/qc-station/samples/{sampleId}", $"/api/qc-station/samples/{sampleId}/pressure" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(before, factory.Writes.Count);
        await factory.WithDbAsync(async db =>
        {
            var station = await db.QcStations.SingleAsync();
            Assert.Equal(DateTimeOffset.UnixEpoch, station.LastSeenAt);
            Assert.Equal("old-address", station.LastSeenIp);
            return true;
        });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/qc-station/heartbeat", null)).StatusCode);
        Assert.Equal(before + 1, factory.Writes.Count);
        Assert.All(factory.Writes.Last(), p => Assert.Contains(p, new[] { "QcStation.LastSeenAt", "QcStation.LastSeenIp" }));
        Assert.True(await factory.WithDbAsync(async db => (await db.QcStations.SingleAsync()).LastSeenAt > DateTimeOffset.UnixEpoch));
        foreach (var (method, suffix) in new[] { (HttpMethod.Put, "pressures"), (HttpMethod.Put, "pressure"), (HttpMethod.Post, "pressure") })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, $"/api/qc-station/samples/{sampleId}/{suffix}")
            { Content = JsonContent.Create(new { rows = new[] { new { rowNumber = 1, pressure1Lbs = 12m, pressure2Lbs = 13m } } }) });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("", true, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-key", true, HttpStatusCode.Unauthorized)]
    [InlineData("test-key", false, HttpStatusCode.Forbidden)]
    public async Task StationExemptions_RejectCookieAloneOrInvalidKey(string key, bool active, HttpStatusCode expected)
    {
        await using var factory = new Factory();
        using var client = await factory.BrowserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.QcStations.Add(new CropQc.Data.Entities.QcStation
            {
                Name = "Test station",
                StationCode = "TEST",
                IsActive = active,
                ApiKeyHash = QcStationApiKeyValidator.HashApiKey("test-key")
            });
            await db.SaveChangesAsync();
            return true;
        });
        client.DefaultRequestHeaders.Add(QcStationApiKeyValidator.StationCodeHeaderName, "TEST");
        if (key != "") client.DefaultRequestHeaders.Add(QcStationApiKeyValidator.HeaderName, key);
        var before = factory.Writes.Count;
        foreach (var (method, path) in new[] { (HttpMethod.Post, "heartbeat"), (HttpMethod.Post, "samples/1/pressure"), (HttpMethod.Put, "samples/1/pressure"), (HttpMethod.Put, "samples/1/pressures") })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, "/api/qc-station/" + path)
            { Content = JsonContent.Create(new { rows = Array.Empty<object>() }) });
            Assert.Equal(expected, response.StatusCode);
        }
        Assert.Equal(before, factory.Writes.Count);
    }

    private static ControllerActionDescriptor[] Actions(Factory factory) => factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
        .ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToArray();

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Login");
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();
        public List<string[]> Writes { get; } = [];
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:EnsureCreatedOnStartup"] = "true",
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
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddDbContext<CropQcDbContext>(o => o.UseInMemoryDatabase(database).AddInterceptors(new WriteObserver(Writes)));
            });
        }

        public async Task<HttpClient> BrowserAsync(string email = ApplicationAreas.OwnerEmail)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            var id = await WithDbAsync(async db =>
            {
                var user = new User { Email = email, DisplayName = "Test", Domain = "example.test", IsActive = true };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                return user.Id;
            });
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Name, "Test") }, CookieAuthenticationDefaults.AuthenticationScheme);
            if (email == ApplicationAreas.OwnerEmail) identity.AddClaim(new Claim(ClaimTypes.Role, BuiltInRoleNames.Admin));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties(), CookieAuthenticationDefaults.AuthenticationScheme);
            client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
            return client;
        }

        public async Task<T> WithDbAsync<T>(Func<CropQcDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<CropQcDbContext>());
        }
    }

    private sealed class WriteObserver(List<string[]> writes) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            eventData.Context!.ChangeTracker.DetectChanges();
            writes.Add(eventData.Context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .SelectMany(e => e.Properties.Where(p => p.IsModified || e.State != EntityState.Modified).Select(p => e.Metadata.ClrType.Name + "." + p.Metadata.Name)).ToArray());
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
