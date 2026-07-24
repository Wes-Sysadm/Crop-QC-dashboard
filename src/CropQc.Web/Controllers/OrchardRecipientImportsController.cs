using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/OrchardRecipientImports")]
[Authorize(Policy = AccessPolicyNames.ConfigurationAdmin)]
public sealed class OrchardRecipientImportsController(
    IOrchardContactImportService importService,
    IAdminAuthorizationService authorizationService,
    ILogger<OrchardRecipientImportsController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await importService.GetIndexAsync(null, cancellationToken));

    [HttpPost("Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(OrchardContactWorkbookUploadForm form, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await importService.PreviewAsync(form.Workbook!, cancellationToken);
            return View("Index", await importService.GetIndexAsync(preview, cancellationToken));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            logger.LogWarning(exception, "Orchard manager workbook dry run failed validation.");
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("ExportDryRun")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportDryRun(OrchardContactWorkbookUploadForm form, CancellationToken cancellationToken)
    {
        try
        {
            var csv = await importService.ExportDryRunCsvAsync(form.Workbook!, cancellationToken);
            return File(csv, "text/csv", "orchard-manager-dry-run.csv");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            logger.LogWarning(exception, "Orchard manager workbook dry-run export failed validation.");
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("Stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stage(OrchardContactWorkbookUploadForm form, CancellationToken cancellationToken)
    {
        try
        {
            var (batchId, error) = await importService.StageAsync(
                form.Workbook!,
                authorizationService.GetEmail(User) ?? "",
                cancellationToken);
            if (error is not null) TempData["Error"] = error;
            else TempData["Success"] = "Workbook staged for administrator review. No aliases or recipients were changed.";
            return batchId is null ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id = batchId });
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            logger.LogWarning(exception, "Orchard manager workbook staging failed validation.");
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var model = await importService.GetBatchAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("{id:long}/Review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(long id, OrchardContactImportDecisionForm form, CancellationToken cancellationToken)
    {
        form.BatchId = id;
        var error = await importService.ReviewAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Review decision saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(long id, OrchardContactImportApplyForm form, CancellationToken cancellationToken)
    {
        form.BatchId = id;
        var result = await importService.ApplyAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? result.WasAlreadyApplied
                ? "This reviewed import was already applied; no duplicate changes were made."
                : $"Import applied transactionally. Contacts {result.ContactsCreated}, assignments {result.AssignmentsCreated}, recipients {result.RecipientsCreated}, aliases {result.AliasesCreated}, duplicates skipped {result.DuplicatesSkipped}, conflicts retained {result.ConflictsRetained}."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
