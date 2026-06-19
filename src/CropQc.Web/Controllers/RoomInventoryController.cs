using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

[Route("Admin/RoomInventory")]
[Authorize(Policy = "RequireManagerOrAdmin")]
public sealed class RoomInventoryController(IRoomInventoryImportService roomInventoryImportService, IAdminAuthorizationService authorizationService, ILogger<RoomInventoryController> logger) : Controller
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

        try
        {
            var preview = await roomInventoryImportService.PreviewAsync(form, cancellationToken);
            var model = await roomInventoryImportService.GetPageAsync(form, cancellationToken);
            model.ImportPreview = preview;
            return View("Index", model);
        }
        catch (Exception ex)
        {
            return await ImportFailureAsync(form, ex, "preview", cancellationToken);
        }
    }

    [HttpPost("ImportEbsStartingInventory")]
    public async Task<IActionResult> ImportEbsStartingInventory(CancellationToken cancellationToken)
    {
        if (!CanApplyEbsCorrectionSeed())
        {
            return Forbid();
        }

        var form = new RoomInventoryImportForm { UseBuiltInSeed = true, Facility = "EBS" };
        try
        {
            var preview = await roomInventoryImportService.PreviewAsync(form, cancellationToken);
            var model = await roomInventoryImportService.GetPageAsync(form, cancellationToken);
            model.ImportPreview = preview;
            return View("Index", model);
        }
        catch (Exception ex)
        {
            return await ImportFailureAsync(form, ex, "built-in baseline preview", cancellationToken);
        }
    }

    [HttpPost("Apply")]
    public async Task<IActionResult> Apply(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        if (!CanApplyEbsCorrectionSeed())
        {
            return Forbid();
        }

        try
        {
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
        catch (Exception ex)
        {
            return await ImportFailureAsync(form, ex, "apply", cancellationToken);
        }
    }

    private async Task<IActionResult> ImportFailureAsync(RoomInventoryImportForm form, Exception exception, string stage, CancellationToken cancellationToken)
    {
        var referenceId = Guid.NewGuid().ToString("N")[..10];
        logger.LogError(exception, "Current Inventory Baseline import {Stage} failed. Reference {ReferenceId}.", stage, referenceId);
        TempData["Error"] = $"Current Inventory Baseline import failed during {stage}. Reference {referenceId}. The full exception was logged for troubleshooting.";
        var model = await roomInventoryImportService.GetPageAsync(form, cancellationToken);
        model.ImportPreview = RoomInventoryImportService.ServerFailurePreview(referenceId, "The full server error was logged without exposing secrets.");
        return View("Index", model);
    }

    private bool CanApplyEbsCorrectionSeed() =>
        User.IsInRole("Admin")
        || string.Equals(User.FindFirstValue(ClaimTypes.Email), "wes@fruitandland.com", StringComparison.OrdinalIgnoreCase);
}
