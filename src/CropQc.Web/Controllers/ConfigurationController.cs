using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Data;
using CropQc.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

[Route("Admin/Configuration")]
[Authorize(Policy = "RequireAdmin")]
public sealed class ConfigurationController(
    IAdminManagementService adminService,
    IAdminAuthorizationService authorizationService,
    EmailOptions emailOptions,
    GoogleAuthenticationOptions googleAuthOptions,
    IGoogleCredentialStore googleCredentialStore,
    CropQcDbContext dbContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await adminService.GetConfigurationAsync(true, cancellationToken);
        model.EmailStatus = await BuildEmailStatusAsync(cancellationToken);
        return View(model);
    }

    [HttpPost("Save")]
    public async Task<IActionResult> Save(ConfigurationEditForm form, CancellationToken cancellationToken)
    {
        var error = await adminService.SaveConfigurationAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Configuration saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<EmailStatusViewModel> BuildEmailStatusAsync(CancellationToken cancellationToken)
    {
        var currentEmail = User.FindFirstValue(ClaimTypes.Email);
        var domain = GoogleAuthenticationOptions.GetEmailDomain(currentEmail);
        var status = new EmailStatusViewModel
        {
            Provider = emailOptions.Provider,
            GmailUserEnabled = string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase),
            DefaultQcRecipientsConfigured = emailOptions.QcRecipientList.Count > 0,
            CurrentUserEmail = currentEmail,
            CurrentUserDomain = domain,
            CurrentUserDomainAllowed = domain is not null && googleAuthOptions.AllowedDomains.Contains(domain)
        };

        if (!string.IsNullOrWhiteSpace(currentEmail))
        {
            var user = await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Email == currentEmail && x.IsActive, cancellationToken);
            if (user is not null)
            {
                var diagnostic = await googleCredentialStore.GetDiagnosticAsync(user, cancellationToken);
                status.GmailCredentialPresent = diagnostic.CredentialPresent;
                status.GmailSendPermissionGranted = diagnostic.GmailSendPermissionGranted;
            }
        }

        status.CurrentUserNeedsReconnect = status.GmailUserEnabled
            && (!status.GmailCredentialPresent || !status.GmailSendPermissionGranted);
        return status;
    }
}
