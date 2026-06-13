using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("MasterData")]
[Authorize(Policy = "RequireManagerOrAdmin")]
public sealed class MasterDataController(IAdminManagementService adminService, IAdminAuthorizationService authorizationService) : Controller
{
    [HttpGet("")]
    [HttpGet("{type}")]
    public async Task<IActionResult> Index(string? type, CancellationToken cancellationToken) =>
        View(await adminService.GetMasterDataAsync(type ?? "index", authorizationService.IsManagerOrAdmin(User), cancellationToken));

    [HttpGet("{type}/Edit/{id:int}")]
    public async Task<IActionResult> Edit(string type, int id, CancellationToken cancellationToken)
    {
        var form = await adminService.GetEditFormAsync(type, id, cancellationToken);
        if (form is null) return NotFound();
        return View(form);
    }

    [HttpPost("{type}/Save")]
    public async Task<IActionResult> Save(string type, CropQc.Web.Models.MasterDataEditForm form, CancellationToken cancellationToken)
    {
        form.Type = type;
        var error = await adminService.SaveMasterDataAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            return form.Id is null ? RedirectToAction(nameof(Index), new { type }) : RedirectToAction(nameof(Edit), new { type, id = form.Id });
        }
        TempData["Success"] = "Master data saved.";
        return RedirectToAction(nameof(Index), new { type });
    }

    [HttpPost("{type}/Deactivate/{id:int}")]
    public async Task<IActionResult> Deactivate(string type, int id, CancellationToken cancellationToken)
    {
        var error = await adminService.DeactivateAsync(type, id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Record deactivated.";
        return RedirectToAction(nameof(Index), new { type });
    }

    [HttpPost("grower-lots/ImportPreview")]
    public async Task<IActionResult> PreviewGrowerLotImport(CropQc.Web.Models.GrowerLotImportForm form, CancellationToken cancellationToken)
    {
        var preview = await adminService.PreviewGrowerLotImportAsync(form, cancellationToken);
        var model = await adminService.GetMasterDataAsync("grower-lots", authorizationService.IsManagerOrAdmin(User), cancellationToken);
        return View("Index", model with { ImportPreview = preview });
    }

    [HttpPost("grower-lots/ImportApply")]
    public async Task<IActionResult> ApplyGrowerLotImport(CropQc.Web.Models.GrowerLotImportForm form, CancellationToken cancellationToken)
    {
        form.ConfirmImport = true;
        var (preview, error) = await adminService.ApplyGrowerLotImportAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            var model = await adminService.GetMasterDataAsync("grower-lots", authorizationService.IsManagerOrAdmin(User), cancellationToken);
            return View("Index", model with { ImportPreview = preview });
        }

        TempData["Success"] = $"Grower lots imported. Added {preview.AddCount}, updated {preview.UpdateCount}, unchanged {preview.UnchangedCount}.";
        return RedirectToAction(nameof(Index), new { type = "grower-lots" });
    }
}
