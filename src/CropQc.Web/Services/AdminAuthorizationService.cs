using System.Security.Claims;

namespace CropQc.Web.Services;

public interface IAdminAuthorizationService
{
    string? GetEmail(ClaimsPrincipal user);
}

public sealed class AdminAuthorizationService : IAdminAuthorizationService
{
    public string? GetEmail(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
}
