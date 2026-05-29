using System.Security.Claims;
using CropQc.Web.Auth;

namespace CropQc.Web.Services;

public interface IAdminAuthorizationService
{
    bool IsAdmin(ClaimsPrincipal user);
    string? GetEmail(ClaimsPrincipal user);
}

public sealed class AdminAuthorizationService(GoogleAuthenticationOptions options) : IAdminAuthorizationService
{
    public bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || options.IsBootstrapAdminEmail(GetEmail(user));

    public string? GetEmail(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
}
