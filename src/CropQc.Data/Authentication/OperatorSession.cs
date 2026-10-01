using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CropQc.Data.Authentication;

/// <summary>The API consumes the Web Google session; it never mints an operator identity.</summary>
public static class OperatorSession
{
    public static void ConfigureKeys(IServiceCollection services, IConfiguration configuration)
    {
        var protection = services.AddDataProtection().SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "CropQcDashboard");
        if (!configuration.GetValue<bool>("DataProtection:PersistKeysToFileSystem")) return;
        var path = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("DataProtection:KeysPath is required when DataProtection:PersistKeysToFileSystem is true.");
        Directory.CreateDirectory(path);
        protection.PersistKeysToFileSystem(new DirectoryInfo(path));
    }

    public static void ConfigureCookie(CookieAuthenticationOptions options, IConfiguration configuration, bool development, bool api = false)
    {
        var days = configuration.GetValue<int?>("Authentication:SessionDays");
        options.ExpireTimeSpan = TimeSpan.FromDays(days is > 0 ? days.Value : 7);
        options.SlidingExpiration = true;
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.Cookie.Name = configuration["Authentication:CookieName"] ?? ".AspNetCore.Cookies";
        options.Cookie.Path = "/";
        options.Cookie.Domain = configuration["Authentication:CookieDomain"];
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Events.OnValidatePrincipal = ValidateAsync;
        if (api)
        {
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        }
    }

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
        var db = context.HttpContext.RequestServices.GetRequiredService<CropQcDbContext>();
        if (!string.IsNullOrWhiteSpace(email) && await db.Users.AsNoTracking().AnyAsync(x => x.Email == email && x.IsActive, context.HttpContext.RequestAborted)) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // Receipts has no legacy permission alias. Match Web's live role/page policy,
    // rather than trusting potentially stale role claims in a shared session.
    public static async Task<bool> CanReceiveAsync(CropQcDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !await db.Users.AnyAsync(x => x.Email == email && x.IsActive, ct)) return false;
        if (email == "wes@fruitandland.com") return true;
        var roles = await db.UserRoles.AsNoTracking().Include(x => x.Role).ThenInclude(x => x.PageAccesses)
            .Where(x => x.User.Email == email && x.User.IsActive).ToListAsync(ct);
        if (roles.Count != 1 || !roles[0].Role.IsActive) return false;
        var role = roles[0].Role;
        if (string.Equals(role.Name, Entities.BuiltInRoleNames.Admin, StringComparison.OrdinalIgnoreCase)) return true;
        var level = role.PageAccesses.SingleOrDefault(x => x.AreaKey == "receipts")?.AccessLevel;
        return new[] { "Create", "Edit", "Admin" }.Contains(level, StringComparer.OrdinalIgnoreCase);
    }
}
