using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/OrchardRecipients")]
[Authorize(Policy = AccessPolicyNames.ConfigurationAdmin)]
public sealed class OrchardRecipientsController(
    IOrchardRecipientAdminService orchardRecipientAdminService,
    IAdminAuthorizationService authorizationService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken) =>
        View(await orchardRecipientAdminService.GetMatrixAsync(search, cancellationToken));

    [HttpPost("Save")]
    public async Task<IActionResult> Save(OrchardRecipientEditForm form, CancellationToken cancellationToken)
    {
        var result = await orchardRecipientAdminService.UpsertAsync(
            new OrchardRecipientUpsertRequest(form.Id, form.CanonicalOrchardId, null, form.EmailAddress, form.IsActive),
            authorizationService.GetEmail(User) ?? "",
            cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "Orchard recipient saved." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/Enabled")]
    public async Task<IActionResult> SetEnabled(int id, bool enabled, CancellationToken cancellationToken)
    {
        var error = await orchardRecipientAdminService.SetEnabledAsync(id, enabled, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? (enabled ? "Orchard recipient enabled." : "Orchard recipient disabled.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/Delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var error = await orchardRecipientAdminService.DeleteAsync(id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Orchard recipient removed.";
        return RedirectToAction(nameof(Index));
    }
}
