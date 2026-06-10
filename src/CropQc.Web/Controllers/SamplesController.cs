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
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetSampleDetailAsync(id, cancellationToken));

    [HttpGet("{id:long}/refresh")]
    public async Task<IActionResult> Refresh(long id, CancellationToken cancellationToken)
    {
        var model = await dataService.GetSampleRefreshAsync(id, cancellationToken);
        return model is null ? NotFound() : Json(model);
    }

    [HttpGet("{id:long}/Delete")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetDeleteSampleConfirmationAsync(id, cancellationToken));

    [HttpPost("{id:long}/Delete")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> ConfirmDelete(long id, DeleteSampleConfirmationViewModel form, CancellationToken cancellationToken)
    {
        var (receiptId, error) = await dataService.SoftDeleteSampleAsync(id, form.Reason, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC sample deleted.";
        return receiptId is null
            ? RedirectToAction("Index", "Receipts")
            : RedirectToAction("Details", "Receipts", new { id = receiptId.Value });
    }

    [HttpPost("{id:long}/rows")]
    public async Task<IActionResult> SaveRows(long id, SaveFruitReadingsForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.SaveFruitReadingsAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Fruit readings saved. In-progress rows can be completed later.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:long}/Starch")]
    public async Task<IActionResult> Starch(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetStarchTestAsync(id, cancellationToken));

    [HttpPost("{id:long}/Starch")]
    public async Task<IActionResult> SaveStarch(long id, SaveStarchTestForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.SaveStarchTestAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Starch test saved.";
        return RedirectToAction(nameof(Starch), new { id });
    }

    [HttpPost("{id:long}/Starch/photos")]
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
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> OverrideSend(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetOverrideSendAsync(id, cancellationToken));

    [HttpPost("{id:long}/Send")]
    [Authorize(Policy = "RequireQcUserOrHigher")]
    public async Task<IActionResult> SendQcSummary(long id, CancellationToken cancellationToken)
    {
        var error = await dataService.SendQcSummaryAsync(id, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? $"Email sent from {User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/OverrideSend")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
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
    public async Task<IActionResult> AddPhoto(long id, AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        LogPhotoUploadStart("sample", id, form);
        var error = await dataService.AddSamplePhotoMetadataAsync(id, form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo uploaded successfully.";
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
