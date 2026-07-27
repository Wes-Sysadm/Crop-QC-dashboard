using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/CommercialPacks")]
[Authorize(Policy = AccessPolicyNames.MasterDataAdmin)]
public sealed class CommercialPacksController(ICommercialPackAdminService service) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? editPlanId, int? editPackId, CancellationToken cancellationToken) =>
        View(await service.GetPageAsync(editPlanId, editPackId, cancellationToken));

    [HttpPost("Plans/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan(CommercialPackPlanForm form, CancellationToken cancellationToken)
    {
        var error = await service.SavePlanAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Commercial pack plan saved.";
        return RedirectToAction(nameof(Index), new { editPlanId = error is null ? null : form.Id });
    }

    [HttpPost("Packs/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePack(CommercialPackDefinitionForm form, CancellationToken cancellationToken)
    {
        var error = await service.SavePackAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Commercial pack definition saved.";
        return RedirectToAction(nameof(Index), new { editPackId = error is null ? null : form.Id });
    }

    [HttpPost("PlanItems/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlanItem(CommercialPackPlanItemForm form, CancellationToken cancellationToken)
    {
        var error = await service.SavePlanItemAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Pack assigned to plan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("PlanItems/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePlanItem(int planId, int packId, CancellationToken cancellationToken)
    {
        var error = await service.RemovePlanItemAsync(planId, packId, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Pack removed from plan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Plans/{id:int}/Deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivatePlan(int id, CancellationToken cancellationToken)
    {
        var error = await service.DeactivatePlanAsync(id, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Commercial pack plan deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Packs/{id:int}/Deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivatePack(int id, CancellationToken cancellationToken)
    {
        var error = await service.DeactivatePackAsync(id, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Commercial pack definition deactivated.";
        return RedirectToAction(nameof(Index));
    }
}
