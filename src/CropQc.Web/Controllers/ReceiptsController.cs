using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class ReceiptsController(
    IDashboardDataService dataService,
    IReceivingExportService exportService,
    FileStorageOptions fileStorageOptions,
    ILogger<ReceiptsController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ReceiptSearchForm search, CancellationToken cancellationToken) =>
        View(await dataService.SearchReceiptsAsync(search, cancellationToken));

    [HttpGet("Export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var content = await exportService.ExportReceivingDataAsync(cancellationToken);
        var fileName = $"crop-qc-receiving-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpPost("Create")]
    public async Task<IActionResult> Create(CreateReceiptForm form, CancellationToken cancellationToken)
    {
        var error = await dataService.CreateReceiptAsync(form, cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetReceiptDetailAsync(id, cancellationToken));

    [Authorize(Policy = "RequireAdmin")]
    [HttpGet("{id:long}/Edit")]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetReceiptEditAsync(id, cancellationToken));

    [Authorize(Policy = "RequireAdmin")]
    [HttpPost("{id:long}/Edit")]
    public async Task<IActionResult> Edit(long id, UpdateReceiptForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await dataService.UpdateReceiptAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Receipt updated.";
        return error is null
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(nameof(Edit), new { id });
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpPost("{id:long}/Delete")]
    public async Task<IActionResult> Delete(long id, DeleteReceiptForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await dataService.SoftDeleteReceiptAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Receipt deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:long}/samples")]
    public async Task<IActionResult> CreateSample(long id, CreateReceiptSampleForm form, CancellationToken cancellationToken)
    {
        var result = await dataService.CreateSampleAsync(id, form.SampleTypeId, cancellationToken);
        if (result.Error is not null)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (result.Warning is not null)
        {
            TempData["Warning"] = result.Warning;
        }

        return RedirectToAction("Details", "Samples", new { id = result.SampleId });
    }

    [HttpPost("{id:long}/photos")]
    public async Task<IActionResult> AddPhoto(long id, AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        LogPhotoUploadStart("receipt", id, form);
        form.ReceiptId = id;
        form.QcSampleId = null;
        var error = IsAllowedPhotoType(form.PhotoType, "BinTruck", "TopOfTruck", "Other")
            ? await dataService.AddPhotoMetadataAsync(form, cancellationToken)
            : "Only truck, top-of-truck, or other photos can be added from the receipt detail page.";
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
