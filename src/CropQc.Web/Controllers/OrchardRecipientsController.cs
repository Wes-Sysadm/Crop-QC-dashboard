using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/OrchardRecipients")]
[Authorize(Policy = AccessPolicyNames.OrchardManagersView)]
public sealed class OrchardRecipientsController(
    IOrchardRecipientAdminService orchardRecipientAdminService,
    IGrowerRecipientAdminService growerRecipientAdminService,
    IAdminAuthorizationService authorizationService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var growers = await growerRecipientAdminService.GetMatrixAsync(search, cancellationToken);
        var orchards = await orchardRecipientAdminService.GetMatrixAsync(search, cancellationToken);
        return View(new QcRecipientMatrixViewModel
        {
            Search = search?.Trim() ?? "",
            GrowerNumbers = growers,
            Orchards = orchards
        });
    }

    [HttpPost("GrowerNumbers/Save")]
    [Authorize(Policy = AccessPolicyNames.OrchardManagersCreate)]
    public async Task<IActionResult> SaveGrowerNumber(GrowerRecipientEditForm form, CancellationToken cancellationToken)
    {
        var result = await growerRecipientAdminService.UpsertAsync(
            new GrowerRecipientUpsertRequest(form.Id, form.CanonicalGrowerNumberId, form.EmailAddress, form.IsActive),
            authorizationService.GetEmail(User) ?? "",
            cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "Grower Number recipient saved." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("GrowerNumbers/{id:int}/Enabled")]
    [Authorize(Policy = AccessPolicyNames.OrchardManagersAdmin)]
    public async Task<IActionResult> SetGrowerNumberEnabled(int id, bool enabled, CancellationToken cancellationToken)
    {
        var error = await growerRecipientAdminService.SetEnabledAsync(id, enabled, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? (enabled ? "Grower Number recipient enabled." : "Grower Number recipient disabled.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("GrowerNumbers/{id:int}/Delete")]
    [Authorize(Policy = AccessPolicyNames.OrchardManagersAdmin)]
    public async Task<IActionResult> DeleteGrowerNumber(int id, CancellationToken cancellationToken)
    {
        var error = await growerRecipientAdminService.DeleteAsync(id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Grower Number recipient removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Save")]
    [Authorize(Policy = AccessPolicyNames.OrchardManagersCreate)]
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
    [Authorize(Policy = AccessPolicyNames.OrchardManagersAdmin)]
    public async Task<IActionResult> SetEnabled(int id, bool enabled, CancellationToken cancellationToken)
    {
        var error = await orchardRecipientAdminService.SetEnabledAsync(id, enabled, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? (enabled ? "Orchard recipient enabled." : "Orchard recipient disabled.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/Delete")]
    [Authorize(Policy = AccessPolicyNames.OrchardManagersAdmin)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var error = await orchardRecipientAdminService.DeleteAsync(id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Orchard recipient removed.";
        return RedirectToAction(nameof(Index));
    }
}
