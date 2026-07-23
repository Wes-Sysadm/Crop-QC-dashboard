using CropQc.Data;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class FieldSamplesController(
    IFieldSampleService fieldSampleService,
    IFieldSampleDeletionService fieldSampleDeletionService,
    IFieldSampleReportService fieldSampleReportService,
    IDashboardDataService dashboardDataService,
    CropQcDbContext dbContext,
    IFileStorageService fileStorageService) : Controller
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

    [HttpGet("{id:long}/Delete")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesAdmin)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var model = await fieldSampleDeletionService.GetConfirmationAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("{id:long}/Delete")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, DeleteFieldSampleForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await fieldSampleDeletionService.DeleteAsync(form, User, cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Delete), new { id });
        }

        TempData["Success"] = $"Field Sample {id} was soft-deleted. Its operational and audit history was retained.";
        return RedirectToAction(nameof(Index), new { DeletionStatus = "Deleted" });
    }

    [HttpGet("{id:long}/refresh")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesView)]
    public async Task<IActionResult> Refresh(long id, CancellationToken cancellationToken)
    {
        var model = await fieldSampleService.GetRefreshAsync(id, User, cancellationToken);
        return model is null ? NotFound() : Json(model);
    }

    [HttpPost("{id:long}/autosave")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Autosave(long id, [FromBody] FieldSampleAutosaveRequest request, CancellationToken cancellationToken)
    {
        var result = await fieldSampleService.AutosaveAsync(id, request, User, cancellationToken);
        if (result.Conflicts.Count > 0)
        {
            return Conflict(result);
        }
        if (result.ValidationErrors.Count > 0 || result.Error is not null)
        {
            return BadRequest(result);
        }
        return Json(result);
    }

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
        if (error is not null)
        {
            var model = await fieldSampleService.GetDetailAsync(id, User, cancellationToken);
            model.IsEditingMetadata = true;
            model.MetadataForm = form;
            model.DataWarning = error;
            return View("Details", model);
        }

        TempData["Success"] = "Field Sample details saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/rows")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> SaveRows(long id, SaveFruitReadingsForm form, CancellationToken cancellationToken)
    {
        form.SampleId = id;
        var error = await fieldSampleService.SaveRowsAsync(id, form, User, cancellationToken);
        if (error is not null)
        {
            var model = await fieldSampleService.GetDetailAsync(id, User, cancellationToken);
            ApplySubmittedRows(model, form);
            model.DataWarning = error;
            return View("Details", model);
        }

        TempData["Success"] = "Field Sample rows saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/complete")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> MarkComplete(long id, CancellationToken cancellationToken)
    {
        var error = await fieldSampleService.MarkCompleteAsync(id, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Field Sample marked complete. Review the report before sending.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:long}/report")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesView)]
    public async Task<IActionResult> PreviewReport(long id, CancellationToken cancellationToken)
    {
        var result = await fieldSampleReportService.PreviewAsync(id, User, cancellationToken);
        if (result.Error is not null || result.Preview is null)
        {
            TempData["Error"] = result.Error ?? "Field Sample report could not be prepared.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View("ReportPreview", result.Preview);
    }

    [HttpPost("{id:long}/report/send")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> SendReport(long id, bool confirmSend, CancellationToken cancellationToken)
    {
        if (!confirmSend)
        {
            TempData["Error"] = "Confirm the recipients and report before sending.";
            return RedirectToAction(nameof(PreviewReport), new { id });
        }

        var error = await fieldSampleReportService.SendAsync(id, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Field Sample report sent and recorded.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/photos")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> AddPhoto(long id, AddPhotoMetadataForm form, List<IFormFile>? photoFiles, CancellationToken cancellationToken)
    {
        var uploads = photoFiles?.Where(x => x.Length > 0).ToList() ?? [];
        if (uploads.Count == 0 && form.PhotoFile is not null)
        {
            uploads.Add(form.PhotoFile);
        }

        var errors = new List<string>();
        var saved = 0;
        foreach (var upload in uploads)
        {
            form.PhotoFile = upload;
            var error = await dashboardDataService.AddSamplePhotoMetadataAsync(id, form, cancellationToken);
            if (error is null) saved++;
            else errors.Add($"{upload.FileName}: {error}");
        }
        if (uploads.Count == 0) errors.Add("Choose or capture at least one photo.");
        TempData[errors.Count == 0 ? "Success" : "Error"] = errors.Count == 0
            ? $"{saved} Field Sample photo{(saved == 1 ? "" : "s")} uploaded successfully."
            : $"{saved} photo(s) saved. {string.Join(" ", errors)}";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:long}/photos/{photoId:long}/content")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesView)]
    public async Task<IActionResult> PhotoContent(long id, long photoId, CancellationToken cancellationToken)
    {
        var photo = await dbContext.QcPhotos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == photoId && x.QcSampleId == id && !x.IsDeleted, cancellationToken);
        var key = photo?.FileId ?? photo?.SharePointItemId;
        if (photo is null || string.IsNullOrWhiteSpace(key))
        {
            return NotFound();
        }

        var content = await fileStorageService.OpenReadAsync(key, cancellationToken);
        return content is null ? NotFound() : File(content, photo.ContentType);
    }

    [HttpPost("{id:long}/photos/{photoId:long}/remove")]
    [Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]
    public async Task<IActionResult> RemovePhoto(long id, long photoId, CancellationToken cancellationToken)
    {
        var error = await dashboardDataService.RemoveSamplePhotoAsync(id, photoId, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Field Sample photo removed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static void ApplySubmittedRows(FieldSampleDetailViewModel model, SaveFruitReadingsForm form)
    {
        var byRow = model.FruitRows.ToDictionary(x => x.RowNumber);
        foreach (var submitted in form.Rows)
        {
            if (!byRow.TryGetValue(submitted.RowNumber, out var row))
            {
                row = new FruitReadingRowViewModel { RowNumber = submitted.RowNumber, SizeStatus = "NotCalculated" };
                byRow[row.RowNumber] = row;
            }
            row.Pressure1Lbs = submitted.Pressure1Lbs;
            row.Pressure2Lbs = submitted.Pressure2Lbs;
            row.PressureAverageLbs = submitted.Pressure1Lbs is null && submitted.Pressure2Lbs is null
                ? null
                : submitted.Pressure1Lbs is null ? submitted.Pressure2Lbs
                : submitted.Pressure2Lbs is null ? submitted.Pressure1Lbs
                : decimal.Round((submitted.Pressure1Lbs.Value + submitted.Pressure2Lbs.Value) / 2m, 2);
            row.WeightGrams = submitted.WeightGrams;
            row.StarchScaleValueId = submitted.StarchScaleValueId;
            row.GradeId = submitted.GradeId;
            row.DefectTypeIds = submitted.DefectTypeIds;
            row.OtherDefectNotes = submitted.OtherDefectNotes;
            row.DefectsInspected = submitted.DefectsInspected;
        }
        model.TargetSampleSize = Math.Clamp(Math.Max(form.TargetSampleSize, byRow.Keys.DefaultIfEmpty(10).Max()), 10, 50);
        model.FruitRows = Enumerable.Range(1, model.TargetSampleSize)
            .Select(number => byRow.TryGetValue(number, out var row) ? row : new FruitReadingRowViewModel { RowNumber = number, SizeStatus = "NotCalculated" })
            .ToList();
        model.FruitReadingForm = form;
    }
}
