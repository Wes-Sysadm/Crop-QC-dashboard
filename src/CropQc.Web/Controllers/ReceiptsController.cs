using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class ReceiptsController(IDashboardDataService dataService, IReceivingExportService exportService) : Controller
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

    [HttpPost("{id:long}/samples")]
    public async Task<IActionResult> CreateSample(long id, CancellationToken cancellationToken)
    {
        var result = await dataService.CreateReceivingSampleAsync(id, cancellationToken);
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
        form.ReceiptId = id;
        form.QcSampleId = null;
        var error = IsAllowedPhotoType(form.PhotoType, "BinTruck", "Other")
            ? await dataService.AddPhotoMetadataAsync(form, cancellationToken)
            : "Only bin/truck photos can be added from the receipt detail page.";
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo metadata added.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static bool IsAllowedPhotoType(string photoType, params string[] allowedTypes) =>
        allowedTypes.Any(x => string.Equals(x, photoType, StringComparison.OrdinalIgnoreCase));
}
