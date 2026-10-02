using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CropQc.Api.Services;
using CropQc.Data;
using CropQc.Data.Authentication;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalOperatorSessionTests
{
    internal static async Task GrantReceivingAsync(CropQcDbContext db)
    {
        var role = new Role { Name = "Local receiver", NormalizedName = "LOCAL RECEIVER" };
        role.PageAccesses.Add(new() { AreaKey = "receipts", AccessLevel = "Create" });
        db.UserRoles.Add(new() { UserId = 8000, Role = role });
        await db.SaveChangesAsync();
    }

    [InventoryPostgresFact]
    public async Task Web_session_authenticates_API_receiving_with_CSRF_and_live_permissions_and_audited_actor()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        await GrantReceivingAsync(db);
        var keys = new EphemeralDataProtectionProvider();
        using var web = SessionIssuer(keys);
        using var server = ApiServer(factory, keys);
        using var client = server.CreateClient();
        var request = new CropQc.Api.Dtos.CreateReceiptRequest(2026, DateTimeOffset.UtcNow, "LOCAL-HTTP-AUTH", 9001, 9003,
            9004, "Local grower", "100000", 11, Guid.NewGuid().ToString("N"), 100000);
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/receipts", request)).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", Cookie(web));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/receipts", request)).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        var session = await client.GetAsync("/api/operator-session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var csrf = await session.Content.ReadFromJsonAsync<Token>();
        var cookies = session.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", Cookie(web) + "; " + string.Join("; ", cookies));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.RequestToken);
        var response = await client.PostAsJsonAsync("/api/receipts", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Assert.Equal(11, await f.Physical(9003));
        Assert.Equal(8000, (await db.InventoryCommands.SingleAsync()).ActorId);
        var saved = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/receipts", request)).StatusCode);
        Assert.Equal(saved, await f.Snapshot());

        var role = await db.Roles.SingleAsync(x => x.NormalizedName == "LOCAL RECEIVER");
        role.IsActive = false; await db.SaveChangesAsync();
        saved = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/receipts", request)).StatusCode);
        Assert.Equal(saved, await f.Snapshot());
        var user = await db.Users.SingleAsync(x => x.Id == 8000); user.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/operator-session")).StatusCode);
    }

    [InventoryPostgresFact]
    public async Task Foreign_key_expired_session_and_forged_role_cannot_authorize_receiving()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        var keys = new EphemeralDataProtectionProvider();
        using var web = SessionIssuer(keys);
        using var foreign = SessionIssuer(new EphemeralDataProtectionProvider());
        using var server = ApiServer(factory, keys);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", Cookie(foreign));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/operator-session")).StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", Cookie(web, expired: true));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/operator-session")).StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie");
        // Even a signed stale Admin claim cannot replace the live receiving permission.
        client.DefaultRequestHeaders.Add("Cookie", Cookie(web));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/operator-session")).StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.InventoryCommands.ToListAsync());
    }

    private static ServiceProvider SessionIssuer(IDataProtectionProvider keys)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(keys);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => OperatorSession.ConfigureCookie(options, new ConfigurationBuilder().Build(), true));
        return services.BuildServiceProvider();
    }

    private static string Cookie(IServiceProvider web, bool expired = false)
    {
        var options = web.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Email, "canonical-test@example.invalid"),
            new(ClaimTypes.NameIdentifier, "local-google-subject"), new(ClaimTypes.Role, "Admin")], "Google"));
        var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow.AddHours(-2), ExpiresUtc = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1) };
        return options.Cookie.Name + "=" + options.TicketDataFormat.Protect(new(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static TestServer ApiServer(IDbContextFactory<CropQcDbContext> factory, IDataProtectionProvider keys) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddLogging(); services.AddSingleton(keys); services.AddSingleton(factory);
            services.AddScoped(_ => factory.CreateDbContext());
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => OperatorSession.ConfigureCookie(options, new ConfigurationBuilder().Build(), true, api: true));
            services.AddAuthorization(); services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
            services.AddHttpContextAccessor(); services.AddScoped<CanonicalReceivingAuthorizationFilter>();
            services.AddScoped<IInventoryCommandExecutor, InventoryCommandExecutor>(); services.AddScoped<CanonicalReceivingService>();
            services.AddScoped<IAuditService, AuditService>(); services.AddScoped<IReceiptService, ReceiptService>();
            services.AddControllers().AddApplicationPart(typeof(CropQc.Api.Controllers.ReceiptsController).Assembly);
        })
        .Configure(app =>
        {
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/api/operator-session", async context =>
                {
                    var db = context.RequestServices.GetRequiredService<CropQcDbContext>();
                    if (!await OperatorSession.CanReceiveAsync(db, context.User, context.RequestAborted)) { context.Response.StatusCode = 403; return; }
                    var token = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                    await context.Response.WriteAsJsonAsync(new Token(token.RequestToken!));
                }).RequireAuthorization();
            });
        }));

    private sealed record Token(string RequestToken);
}
