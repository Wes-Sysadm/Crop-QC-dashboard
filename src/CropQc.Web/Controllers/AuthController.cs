using CropQc.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

public sealed class AuthController(GoogleAuthenticationOptions googleOptions) : Controller
{
    [AllowAnonymous]
    [HttpGet("/Login")]
    public IActionResult Login([FromQuery] string? error)
    {
        ViewData["Error"] = error;
        ViewData["GoogleConfigured"] = googleOptions.IsGoogleConfigured;
        ViewData["AllowedDomains"] = string.Join(", ", googleOptions.AllowedDomains.OrderBy(x => x));
        return View();
    }

    [AllowAnonymous]
    [HttpPost("/Login/Google")]
    public IActionResult GoogleLogin()
    {
        if (!googleOptions.IsGoogleConfigured)
        {
            return RedirectToAction(nameof(Login), new { error = "Google OAuth is not configured for this environment." });
        }

        return Challenge(new AuthenticationProperties { RedirectUri = "/" }, "Google");
    }

    [HttpPost("/Logout")]
    [Authorize]
    public IActionResult Logout() =>
        SignOut(new AuthenticationProperties { RedirectUri = "/Login" }, CookieAuthenticationDefaults.AuthenticationScheme);
}
