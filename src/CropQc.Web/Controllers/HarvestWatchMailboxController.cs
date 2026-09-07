using System.Security.Claims;
using CropQc.Web.Auth;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

/// <summary>Authorizes Gmail read access for the single mailbox that receives HarvestWatch replies.</summary>
[Authorize(Policy = AccessPolicyNames.EmailConfigurationAdmin)]
public sealed class HarvestWatchMailboxController : Controller
{
    [HttpGet("/Admin/Configuration/HarvestWatchMailbox/Connect")]
    public IActionResult Connect()
    {
        var email = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (!string.Equals(email, HarvestWatchConstants.VerificationRecipient, StringComparison.OrdinalIgnoreCase)) return Forbid();
        var properties = new AuthenticationProperties { RedirectUri = "/Admin/Configuration" };
        return Challenge(properties, HarvestWatchConstants.MailboxAuthenticationScheme);
    }
}
