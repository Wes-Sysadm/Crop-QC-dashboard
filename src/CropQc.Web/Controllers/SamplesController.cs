using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class SamplesController(
    IDashboardDataService dataService,
    FileStorageOptions fileStorageOptions,
    ILogger<SamplesController> logger) : Controller
{
    [HttpGet("{id:long}")]
    [Authorize(Policy = AccessPolicyNames.DailyQcView)]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetSampleDetailAsync(id, cancellationToken));

    [HttpGet("{id:long}/refresh")]
    [Authorize(Policy = AccessPolicyNames.DailyQcView)]
    public async Task<IActionResult> Refresh(long id, CancellationToken cancellationToken)
    {
        var model = await dataService.GetSampleRefreshAsync(id, cancellationToken);
        return model is null ? NotFound() : Json(model);
    }

    [HttpGet("{id:long}/Delete")]
    [Authorize(Policy = AccessPolicyNames.DailyQcAdmin)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetDeleteSampleConfirmationAsync(id, cancellationToken));

    [HttpPost("{id:long}/Delete")]
    [Authorize(Policy = AccessPolicyNames.DailyQcAdmin)]
    public async Task<IActionResult> ConfirmDelete(long id, DeleteSampleConfirmationViewModel form, CancellationToken cancellationToken)
    {
        var (receiptId, error) = await dataService.SoftDeleteSampleAsync(id, form.Reason, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC sample deleted.";
        return receiptId is null
            ? RedirectToAction("Index", "Receipts")
            : RedirectToAction("Details", "Receipts", new { id = receiptId.Value });
    }

    [HttpPost("{id:long}/rows")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> SaveRows(long id, SaveFruitReadingsForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.SaveFruitReadingsAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Fruit readings saved. In-progress rows can be completed later.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/autosave")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Autosave(long id, [FromBody] FieldSampleAutosaveRequest request, CancellationToken cancellationToken)
    {
        var result = await dataService.AutosaveFruitReadingsAsync(id, request, cancellationToken);
        if (result.Conflicts.Count > 0) return Conflict(result);
        if (result.ValidationErrors.Count > 0 || result.Error is not null) return BadRequest(result);
        return Json(result);
    }

    [HttpGet("{id:long}/Report")]
    [Authorize(Policy = AccessPolicyNames.DailyQcView)]
    public async Task<IActionResult> Report(long id, CancellationToken cancellationToken)
    {
        var model = await dataService.GetQcReportPreviewAsync(id, cancellationToken);
        return model.SampleId == 0 ? NotFound() : View("ReportPreview", model);
    }

    [HttpPost("{id:long}/sample-type")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> UpdateSampleType(long id, UpdateSampleTypeForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.UpdateSampleTypeAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Sample type updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:long}/Starch")]
    [Authorize(Policy = AccessPolicyNames.DailyQcView)]
    public async Task<IActionResult> Starch(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetStarchTestAsync(id, cancellationToken));

    [HttpPost("{id:long}/Starch")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> SaveStarch(long id, SaveStarchTestForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.SaveStarchTestAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Starch test saved.";
        return RedirectToAction(nameof(Starch), new { id });
    }

    [HttpPost("{id:long}/Starch/photos")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> AddStarchPhoto(long id, AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        LogPhotoUploadStart("starch", id, form);
        form.QcSampleId = id;
        form.ReceiptId = null;
        var error = IsAllowedPhotoType(form.PhotoType, "FruitAfterStarch", "Other")
            ? await dataService.AddPhotoMetadataAsync(form, cancellationToken)
            : "Only after-starch photos can be added from the Starch Input page.";
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo uploaded successfully.";
        return RedirectToAction(nameof(Starch), new { id });
    }

    [HttpGet("{id:long}/OverrideSend")]
    [Authorize(Policy = AccessPolicyNames.DailyQcAdmin)]
    public async Task<IActionResult> OverrideSend(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetOverrideSendAsync(id, cancellationToken));

    [HttpPost("{id:long}/Send")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> SendQcSummary(long id, CancellationToken cancellationToken)
    {
        var error = await dataService.SendQcSummaryAsync(id, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? $"Email sent from {User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/OverrideSend")]
    [Authorize(Policy = AccessPolicyNames.DailyQcAdmin)]
    public async Task<IActionResult> LogOverrideSend(long id, OverrideSendForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.LogOverrideSendAsync(form, cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(OverrideSend), new { id });
        }

        TempData["Success"] = $"Email sent from {User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/photos")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> AddPhoto(long id, AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        LogPhotoUploadStart("sample", id, form);
        var error = await dataService.AddSamplePhotoMetadataAsync(id, form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo uploaded successfully.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/photos/{photoId:long}/remove")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
    public async Task<IActionResult> RemovePhoto(long id, long photoId, CancellationToken cancellationToken)
    {
        var error = await dataService.RemoveSamplePhotoAsync(id, photoId, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo removed from sample.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static bool IsAllowedPhotoType(string photoType, params string[] allowedTypes) =>
        allowedTypes.Any(x => string.Equals(x, photoType, StringComparison.OrdinalIgnoreCase));

    private void LogPhotoUploadStart(string scope, long id, AddPhotoMetadataForm form)
    {
        var file = form.PhotoFile;
        logger.LogInformation(
            "{Scope} photo upload start. Id: {Id}. Uploaded file present: {HasFile}. FileName: {FileName}. ContentType: {ContentType}. Size: {Size}. PhotoType: {PhotoType}. Selected storage provider: {StorageProvider}.",
            scope,
            id,
            file is not null,
            file?.FileName ?? "(none)",
            file?.ContentType ?? "(none)",
            file?.Length ?? 0,
            form.PhotoType,
            fileStorageOptions.Provider);
    }
}
