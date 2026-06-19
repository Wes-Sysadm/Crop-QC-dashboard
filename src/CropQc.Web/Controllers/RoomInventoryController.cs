using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

[Route("Admin/RoomInventory")]
[Authorize(Policy = "RequireManagerOrAdmin")]
public sealed class RoomInventoryController(IRoomInventoryImportService roomInventoryImportService, IAdminAuthorizationService authorizationService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] RoomInventoryImportForm filter, CancellationToken cancellationToken) =>
        View(await roomInventoryImportService.GetPageAsync(filter, cancellationToken));

    [HttpGet("Template")]
    public IActionResult Template() =>
        File(System.Text.Encoding.UTF8.GetBytes(roomInventoryImportService.GetCsvTemplate()), "text/csv", "current-inventory-baseline-template.csv");

    [HttpPost("Preview")]
    public async Task<IActionResult> Preview(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        if (!CanApplyEbsCorrectionSeed())
        {
            return Forbid();
        }

        var preview = await roomInventoryImportService.PreviewAsync(form, cancellationToken);
        var model = await roomInventoryImportService.GetPageAsync(form, cancellationToken);
        model.ImportPreview = preview;
        return View("Index", model);
    }

    [HttpPost("ImportEbsStartingInventory")]
    public async Task<IActionResult> ImportEbsStartingInventory(CancellationToken cancellationToken)
    {
        if (!CanApplyEbsCorrectionSeed())
        {
            return Forbid();
        }

        var form = new RoomInventoryImportForm { UseBuiltInSeed = true, Facility = "EBS" };
        var preview = await roomInventoryImportService.PreviewAsync(form, cancellationToken);
        var model = await roomInventoryImportService.GetPageAsync(form, cancellationToken);
        model.ImportPreview = preview;
        return View("Index", model);
    }

    [HttpPost("Apply")]
    public async Task<IActionResult> Apply(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        if (!CanApplyEbsCorrectionSeed())
        {
            return Forbid();
        }

        form.ConfirmImport = true;
        var (preview, error) = await roomInventoryImportService.ApplyAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            var model = await roomInventoryImportService.GetPageAsync(form, cancellationToken);
            model.ImportPreview = preview;
            return View("Index", model);
        }

        TempData["Success"] = $"Room inventory imported. Added {preview.AddCount}, updated {preview.UpdateCount}, unchanged {preview.UnchangedCount}, warnings {preview.WarningCount}.";
        return RedirectToAction(nameof(Index), new { Facility = "EBS" });
    }

    private bool CanApplyEbsCorrectionSeed() =>
        User.IsInRole("Admin")
        || string.Equals(User.FindFirstValue(ClaimTypes.Email), "wes@fruitandland.com", StringComparison.OrdinalIgnoreCase);
}
