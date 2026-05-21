using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class SamplesController(IDashboardDataService dataService) : Controller
{
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetSampleDetailAsync(id, cancellationToken));

    [HttpPost("{id:long}/rows")]
    public async Task<IActionResult> SaveRows(long id, SaveFruitReadingsForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await dataService.SaveFruitReadingsAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Fruit readings saved.";
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

    [HttpPost("{id:long}/photos")]
    public async Task<IActionResult> AddPhoto(long id, AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        form.QcSampleId = id;
        form.ReceiptId = null;
        var error = await dataService.AddPhotoMetadataAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Photo metadata added.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
