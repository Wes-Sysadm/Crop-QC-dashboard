using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("MasterData")]
public sealed class MasterDataController(
    IAdminManagementService adminService,
    IAdminAuthorizationService authorizationService,
    IUserAccessService accessService) : Controller
{
    [HttpGet("")]
    [HttpGet("{type}")]
    public async Task<IActionResult> Index(string? type, CancellationToken cancellationToken)
    {
        type ??= "index";
        if (!await CanTypeAsync(type, PageAccessLevel.View, cancellationToken)) return Forbid();
        return View(await adminService.GetMasterDataAsync(type, await CanTypeAsync(type, PageAccessLevel.Admin, cancellationToken), cancellationToken));
    }

    [HttpGet("{type}/Edit/{id:int}")]
    public async Task<IActionResult> Edit(string type, int id, CancellationToken cancellationToken)
    {
        if (!await CanTypeAsync(type, PageAccessLevel.Create, cancellationToken)) return Forbid();
        var form = await adminService.GetEditFormAsync(type, id, cancellationToken);
        if (form is null) return NotFound();
        return View(form);
    }

    [HttpPost("{type}/Save")]
    public async Task<IActionResult> Save(string type, CropQc.Web.Models.MasterDataEditForm form, CancellationToken cancellationToken)
    {
        if (!await CanTypeAsync(type, PageAccessLevel.Create, cancellationToken)) return Forbid();
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
        if (!await CanTypeAsync(type, PageAccessLevel.Admin, cancellationToken)) return Forbid();
        var error = await adminService.DeactivateAsync(type, id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Record deactivated.";
        return RedirectToAction(nameof(Index), new { type });
    }

    [HttpGet("canonical-growers/Map")]
    public async Task<IActionResult> MapGrower([FromQuery] CropQc.Web.Models.GrowerMappingForm form, CancellationToken cancellationToken) =>
        await CanTypeAsync("canonical-growers", PageAccessLevel.Create, cancellationToken)
            ? View("MapGrower", await adminService.GetGrowerMappingAsync(form, cancellationToken))
            : Forbid();

    [HttpPost("canonical-growers/Map")]
    public async Task<IActionResult> SaveGrowerMapping(CropQc.Web.Models.GrowerMappingForm form, CancellationToken cancellationToken)
    {
        if (!await CanTypeAsync("canonical-growers", PageAccessLevel.Create, cancellationToken)) return Forbid();
        var error = await adminService.SaveGrowerMappingAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            return View("MapGrower", await adminService.GetGrowerMappingAsync(form, cancellationToken));
        }

        TempData["Success"] = "Grower mapping saved.";
        return LocalRedirect(SafeReturnUrl(form.ReturnUrl));
    }

    [HttpPost("grower-lots/ImportPreview")]
    public async Task<IActionResult> PreviewGrowerLotImport(CropQc.Web.Models.GrowerLotImportForm form, CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(User, ApplicationAreas.ImportTools, PageAccessLevel.Admin, cancellationToken)) return Forbid();
        var preview = await adminService.PreviewGrowerLotImportAsync(form, cancellationToken);
        var model = await adminService.GetMasterDataAsync("grower-lots", await CanTypeAsync("grower-lots", PageAccessLevel.Admin, cancellationToken), cancellationToken);
        return View("Index", model with { ImportPreview = preview });
    }

    [HttpPost("grower-lots/ImportApply")]
    public async Task<IActionResult> ApplyGrowerLotImport(CropQc.Web.Models.GrowerLotImportForm form, CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(User, ApplicationAreas.ImportTools, PageAccessLevel.Admin, cancellationToken)) return Forbid();
        form.ConfirmImport = true;
        var (preview, error) = await adminService.ApplyGrowerLotImportAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            var model = await adminService.GetMasterDataAsync("grower-lots", await CanTypeAsync("grower-lots", PageAccessLevel.Admin, cancellationToken), cancellationToken);
            return View("Index", model with { ImportPreview = preview });
        }

        TempData["Success"] = $"Grower lots imported. Added {preview.AddCount}, updated {preview.UpdateCount}, unchanged {preview.UnchangedCount}.";
        return RedirectToAction(nameof(Index), new { type = "grower-lots" });
    }

    private Task<bool> CanTypeAsync(string type, PageAccessLevel level, CancellationToken cancellationToken) =>
        accessService.HasAccessAsync(
            User,
            AreaForType(type),
            type.Equals("processors", StringComparison.OrdinalIgnoreCase) && level > PageAccessLevel.View ? PageAccessLevel.Admin : level,
            cancellationToken);

    private static string AreaForType(string type) => type.Trim().ToLowerInvariant() switch
    {
        "warehouses" or "facilities" => ApplicationAreas.Facilities,
        "fruit-profiles" or "varieties" => ApplicationAreas.Varieties,
        "grades" => ApplicationAreas.Grades,
        "defect-types" or "defects" => ApplicationAreas.Defects,
        "fruit-size-thresholds" or "size-configuration" => ApplicationAreas.SizeConfiguration,
        "audit-logs" => ApplicationAreas.AuditHistory,
        _ => ApplicationAreas.MasterData
    };

    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith("/", StringComparison.Ordinal) && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/CropYearReview";
}
