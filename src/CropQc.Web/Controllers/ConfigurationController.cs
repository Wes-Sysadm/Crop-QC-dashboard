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
    IQcEmailRecipientResolver qcEmailRecipientResolver,
    IEbsDailyBinsEmailService ebsDailyBinsEmailService,
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

    [HttpPost("EbsDailyBins/SendNow")]
    public async Task<IActionResult> SendEbsDailyBinsNow(CancellationToken cancellationToken)
    {
        var result = await ebsDailyBinsEmailService.SendAsync(authorizationService.GetEmail(User), isTest: false, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("EbsDailyBins/Test")]
    public async Task<IActionResult> SendEbsDailyBinsTest(CancellationToken cancellationToken)
    {
        var result = await ebsDailyBinsEmailService.SendAsync(authorizationService.GetEmail(User), isTest: true, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    private async Task<EmailStatusViewModel> BuildEmailStatusAsync(CancellationToken cancellationToken)
    {
        var currentEmail = User.FindFirstValue(ClaimTypes.Email);
        var domain = GoogleAuthenticationOptions.GetEmailDomain(currentEmail);
        var recipientResolution = await qcEmailRecipientResolver.ResolveAsync(cancellationToken);
        var status = new EmailStatusViewModel
        {
            Provider = emailOptions.Provider,
            GmailUserEnabled = string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase),
            DefaultQcRecipientsConfigured = recipientResolution.IsConfigured,
            DefaultQcRecipientsSource = recipientResolution.Source,
            DefaultQcRecipients = recipientResolution.Recipients,
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
