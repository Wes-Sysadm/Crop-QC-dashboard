using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class FieldSamplesController(IFieldSampleService fieldSampleService) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesView)]
    public async Task<IActionResult> Index([FromQuery] FieldSampleSearchForm form, CancellationToken cancellationToken) =>
        View(await fieldSampleService.GetIndexAsync(form, User, cancellationToken));

    [HttpGet("Create")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(await fieldSampleService.GetCreateAsync(null, cancellationToken));

    [HttpPost("Create")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> Create(FieldSampleCreateForm form, CancellationToken cancellationToken)
    {
        var result = await fieldSampleService.CreateAsync(form, User, cancellationToken);
        if (result.Error is not null)
        {
            TempData["Error"] = result.Error;
            return View(await fieldSampleService.GetCreateAsync(form, cancellationToken));
        }

        TempData["Success"] = "Field Sample created.";
        return RedirectToAction(nameof(Details), new { id = result.SampleId });
    }

    [HttpGet("Suggestions")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesView)]
    public async Task<IActionResult> Suggestions(string orchardName, string blockName, CancellationToken cancellationToken) =>
        Json(await fieldSampleService.GetBlockSuggestionsAsync(orchardName, blockName, cancellationToken));

    [HttpGet("{id:long}")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesView)]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await fieldSampleService.GetDetailAsync(id, User, cancellationToken));

    [HttpGet("{id:long}/Edit")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var model = await fieldSampleService.GetDetailAsync(id, User, cancellationToken);
        model.IsEditingMetadata = true;
        return View("Details", model);
    }

    [HttpPost("{id:long}/metadata")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> SaveMetadata(long id, FieldSampleMetadataForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await fieldSampleService.UpdateMetadataAsync(id, form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Field Sample details saved.";
        return RedirectToAction(error is null ? nameof(Details) : nameof(Edit), new { id });
    }

    [HttpPost("{id:long}/rows")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> SaveRows(long id, SaveFruitReadingsForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await fieldSampleService.SaveRowsAsync(id, form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Field Sample rows saved.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
