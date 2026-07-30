using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/RoomInventory")]
public sealed class RoomInventoryController(
    IRoomInventoryImportService roomInventoryImportService,
    IRoomInventoryReconciliationService reconciliationService,
    IAdminAuthorizationService authorizationService,
    IUserAccessService userAccessService,
    ILogger<RoomInventoryController> logger) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = AccessPolicyNames.CurrentLotsView)]
    public async Task<IActionResult> Index([FromQuery] RoomInventoryImportForm filter, CancellationToken cancellationToken)
    {
        try
        {
            return View(await roomInventoryImportService.GetPageAsync(filter, cancellationToken));
        }
        catch (Exception ex)
        {
            return CurrentLotsFailure(filter, ex, "load");
        }
    }

    [HttpGet("Reconciliation")]
    [Authorize(Policy = AccessPolicyNames.CurrentLotsAdmin)]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] RoomInventoryReconciliationFilter filter,
        CancellationToken cancellationToken) =>
        View(await reconciliationService.GetPageAsync(filter, cancellationToken));

    [HttpGet("Template")]
    [Authorize(Policy = AccessPolicyNames.CurrentLotsView)]
    public IActionResult Template() =>
        File(System.Text.Encoding.UTF8.GetBytes(roomInventoryImportService.GetCsvTemplate()), "text/csv", "current-inventory-baseline-template.csv");

    [HttpPost("Preview")]
    [Authorize(Policy = AccessPolicyNames.CurrentLotsAdmin)]
    public async Task<IActionResult> Preview(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        if (!await CanApplyEbsCorrectionSeedAsync(cancellationToken))
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
    [Authorize(Policy = AccessPolicyNames.CurrentLotsAdmin)]
    public async Task<IActionResult> ImportEbsStartingInventory(CancellationToken cancellationToken)
    {
        if (!await CanApplyEbsCorrectionSeedAsync(cancellationToken))
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
    [Authorize(Policy = AccessPolicyNames.CurrentLotsAdmin)]
    public async Task<IActionResult> Apply(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        if (!await CanApplyEbsCorrectionSeedAsync(cancellationToken))
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

    private IActionResult CurrentLotsFailure(RoomInventoryImportForm form, Exception exception, string stage)
    {
        var referenceId = Guid.NewGuid().ToString("N")[..10];
        logger.LogError(exception, "Current Lots {Stage} failed. Reference {ReferenceId}.", stage, referenceId);
        return View("Index", new RoomInventoryImportPageViewModel
        {
            Form = form,
            CsvTemplateHeader = roomInventoryImportService.GetCsvTemplate().Trim(),
            CsvExample = roomInventoryImportService.GetCsvExample(),
            CurrentLotWarning = $"Current Lots could not fully load during {stage}. Reference {referenceId}. The full exception was logged without exposing secrets.",
            CurrentLotBreakdown =
            [
                new CurrentInventorySourceRowViewModel
                {
                    SourceType = "Server",
                    IsIncluded = false,
                    DecisionReason = $"Current Lots failed before row details could be loaded. Reference {referenceId}. Check the server log for failed room code, grower/lot/variety, room mapping, duplicate/current balance conflict, or null/format details."
                }
            ]
        });
    }

    private async Task<bool> CanApplyEbsCorrectionSeedAsync(CancellationToken cancellationToken) =>
        await userAccessService.HasAccessAsync(User, ApplicationAreas.CurrentLots, PageAccessLevel.Admin, cancellationToken);
}
