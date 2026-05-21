using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class ReceiptsController(IDashboardDataService dataService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ReceiptSearchForm search, CancellationToken cancellationToken) =>
        View(await dataService.SearchReceiptsAsync(search, cancellationToken));

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
        var error = await dataService.AddPhotoMetadataAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo metadata added.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
