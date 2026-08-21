using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Shared.Storage;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

[Route("[controller]")]
public sealed class ReceiptsController(
    IDashboardDataService dataService,
    IReceiptPurgeService receiptPurgeService,
    IReceiptInventoryOverrideService receiptInventoryOverrideService,
    IReceivingTreatmentService receivingTreatmentService,
    ITreatmentReportAttachmentService treatmentReportAttachmentService,
    IUserAccessService userAccessService,
    IAdminManagementService adminManagementService,
    IAdminAuthorizationService adminAuthorizationService,
    CropQcDbContext dbContext,
    IReceivingExportService exportService,
    IFileStorageService fileStorageService,
    FileStorageOptions fileStorageOptions,
    ILogger<ReceiptsController> logger) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsView)]
    public async Task<IActionResult> Index([FromQuery] ReceiptSearchForm search, CancellationToken cancellationToken)
    {
        var model = await dataService.SearchReceiptsAsync(search, cancellationToken);
        model.CanQuickAddVariety = await userAccessService.HasAccessAsync(
            User, ApplicationAreas.Varieties, PageAccessLevel.Create, cancellationToken);
        return View(model);
    }

    [HttpPost("Varieties/QuickAdd")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAddVariety(MasterDataEditForm form, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(User, ApplicationAreas.Varieties, PageAccessLevel.Create, cancellationToken))
            return Forbid();
        form.Type = "fruit-profiles";
        form.Id = null;
        form.IsActive = true;
        var error = await adminManagementService.SaveMasterDataAsync(
            form, adminAuthorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null) return BadRequest(new { error });
        var code = form.Code.Trim();
        var profile = await dbContext.FruitProfiles.AsNoTracking()
            .SingleAsync(x => x.IsActive && x.VarietyCode.ToUpper() == code.ToUpper(), cancellationToken);
        return Json(new
        {
            id = profile.Id,
            label = FruitProfileIdentity(profile.VarietyCode, profile.Name, profile.ProductionType, profile.IsOrganic)
        });
    }

    private static string FruitProfileIdentity(string code, string name, string productionType, bool isOrganic)
    {
        var organic = isOrganic ? "Organic" : "Conventional";
        var identity = string.Equals(productionType?.Trim(), organic, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(productionType) ? organic : $"{productionType.Trim()} - {organic}";
        return $"{code} - {name} - {identity}";
    }

    [HttpGet("Varieties/Search")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    public async Task<IActionResult> SearchVarieties([FromQuery] string? query, CancellationToken cancellationToken)
    {
        var normalized = query?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length == 0) return Json(Array.Empty<object>());
        var profiles = await dbContext.FruitProfiles.AsNoTracking()
            .Where(x => x.IsActive
                && (x.VarietyCode.ToUpper().Contains(normalized)
                    || x.Name.ToUpper().Contains(normalized)))
            .OrderByDescending(x => x.VarietyCode.ToUpper() == normalized)
            .ThenBy(x => x.VarietyCode)
            .Take(25)
            .Select(x => new { x.Id, x.VarietyCode, x.Name, x.ProductionType, x.IsOrganic })
            .ToListAsync(cancellationToken);
        return Json(profiles.Select(x => new
        {
            id = x.Id,
            code = x.VarietyCode,
            label = FruitProfileIdentity(x.VarietyCode, x.Name, x.ProductionType, x.IsOrganic),
            exactCode = string.Equals(x.VarietyCode, normalized, StringComparison.OrdinalIgnoreCase)
        }));
    }

    [HttpGet("Export")]
    [Authorize(Policy = AccessPolicyNames.ExportToolsAdmin)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var content = await exportService.ExportReceivingDataAsync(cancellationToken);
        var fileName = $"crop-qc-receiving-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpPost("Create")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    public async Task<IActionResult> Create(
        CreateReceiptForm form,
        List<StagedReceiptPhotoForm> stagedPhotos,
        CancellationToken cancellationToken)
    {
        var result = await dataService.CreateReceiptAsync(form, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        if (stagedPhotos.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var failures = 0;
        foreach (var stagedPhoto in stagedPhotos)
        {
            if (stagedPhoto.PhotoFile is null
                || !IsAllowedPhotoType(stagedPhoto.PhotoType, "BinTruck", "TopOfTruck", "Other"))
            {
                failures++;
                continue;
            }

            var photoForm = new AddPhotoMetadataForm
            {
                ReceiptId = result.ReceiptId,
                QcSampleId = null,
                PhotoFile = stagedPhoto.PhotoFile,
                PhotoType = stagedPhoto.PhotoType,
                PhotoSource = string.IsNullOrWhiteSpace(stagedPhoto.PhotoSource) ? "Upload File" : stagedPhoto.PhotoSource,
                FileName = stagedPhoto.PhotoFile.FileName,
                ContentType = stagedPhoto.PhotoFile.ContentType,
                FileSizeBytes = stagedPhoto.PhotoFile.Length
            };
            if (await dataService.AddPhotoMetadataAsync(photoForm, cancellationToken) is not null)
            {
                failures++;
            }
        }

        if (failures > 0)
        {
            TempData["Warning"] = $"Receipt {result.ReceiptNumber} was saved, but {failures} of {stagedPhotos.Count} photos could not be uploaded. You can add the missing photo from Receipt Photos.";
        }
        else
        {
            TempData["Success"] = $"Receipt {result.ReceiptNumber} and {stagedPhotos.Count} photo{(stagedPhotos.Count == 1 ? "" : "s")} saved.";
        }

        return RedirectToAction(nameof(Details), new { id = result.ReceiptId });
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsView)]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetReceiptDetailAsync(id, cancellationToken));

    [HttpGet("{id:long}/Treatments/Apply")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    public async Task<IActionResult> ApplyTreatment(long id, CancellationToken cancellationToken) =>
        View("ApplyTreatment", await receivingTreatmentService.GetReceiptApplyPageAsync(new ReceiptTreatmentApplyForm
        {
            ReceiptId = id,
            AppliedAt = DateTimeOffset.UtcNow
        }, false, cancellationToken));

    [HttpPost("{id:long}/Treatments/Review")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewTreatment(long id, ReceiptTreatmentApplyForm form, CancellationToken cancellationToken)
    {
        form.ReceiptId = id;
        return View("ApplyTreatment", await receivingTreatmentService.GetReceiptApplyPageAsync(form, true, cancellationToken));
    }

    [HttpPost("{id:long}/Treatments")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTreatment(
        long id,
        ReceiptTreatmentApplyForm form,
        TreatmentReportUploadForm treatmentReport,
        CancellationToken cancellationToken)
    {
        form.ReceiptId = id;
        var result = await receivingTreatmentService.ApplyReceiptAsync(form, cancellationToken);
        if (result.Error is not null)
        {
            var model = await receivingTreatmentService.GetReceiptApplyPageAsync(form, true, cancellationToken);
            model.Error = result.Error;
            return View("ApplyTreatment", model);
        }
        var attachmentResult = await treatmentReportAttachmentService.UploadAsync(result.ApplicationId!.Value, treatmentReport, User, cancellationToken);
        if (attachmentResult.Failures.Count > 0)
        {
            TempData["Warning"] = $"Receiving treatment was recorded, but {attachmentResult.Failures.Count} of {treatmentReport.Files.Count} report files could not be uploaded. "
                + string.Join(" ", attachmentResult.Failures);
        }
        else
        {
            TempData["Success"] = attachmentResult.Uploaded == 0
                ? "Receiving treatment recorded for the exact Receipt bins without changing inventory quantity."
                : $"Receiving treatment and {attachmentResult.Uploaded} report attachment{(attachmentResult.Uploaded == 1 ? "" : "s")} recorded without changing inventory quantity.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/Treatments/{applicationId:long}/Reports")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTreatmentReport(long id, long applicationId, TreatmentReportUploadForm form, CancellationToken cancellationToken)
    {
        var belongsToReceipt = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .AnyAsync(x => x.Id == applicationId && x.ReceiptId == id && x.ApplicationLevel == TreatmentApplicationLevels.Receiving, cancellationToken);
        if (!belongsToReceipt) return NotFound();
        var result = await treatmentReportAttachmentService.UploadAsync(applicationId, form, User, cancellationToken);
        TempData[result.Failures.Count == 0 ? "Success" : "Error"] = result.Failures.Count == 0
            ? $"{result.Uploaded} treatment report attachment{(result.Uploaded == 1 ? "" : "s")} added."
            : string.Join(" ", result.Failures);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/Treatments/{applicationId:long}/Reports/{attachmentId:long}/Remove")]
    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTreatmentReport(
        long id,
        long applicationId,
        long attachmentId,
        RemoveTreatmentReportForm form,
        CancellationToken cancellationToken)
    {
        var belongsToReceipt = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .AnyAsync(x => x.Id == applicationId && x.ReceiptId == id && x.ApplicationLevel == TreatmentApplicationLevels.Receiving, cancellationToken);
        if (!belongsToReceipt) return NotFound();
        var error = await treatmentReportAttachmentService.RemoveAsync(applicationId, attachmentId, form.Reason, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Treatment report attachment removed; treatment evidence was retained.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/Treatments/{applicationId:long}/Reverse")]
    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReverseTreatment(long id, long applicationId, ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken)
    {
        var belongsToReceipt = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .AnyAsync(x => x.Id == applicationId && x.ReceiptId == id && x.ApplicationLevel == TreatmentApplicationLevels.Receiving, cancellationToken);
        if (!belongsToReceipt) return NotFound();
        form.Id = applicationId;
        var error = await receivingTreatmentService.ReverseReceiptAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Receiving treatment reversed; original evidence and report attachments were retained.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:long}/photos/{photoId:long}/content")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsView)]
    public async Task<IActionResult> PhotoContent(long id, long photoId, CancellationToken cancellationToken)
    {
        var receiptExists = await dbContext.Receipts.AsNoTracking()
            .AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (!receiptExists)
        {
            return NotFound();
        }

        var photo = await dbContext.QcPhotos.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == photoId
                    && x.ReceiptId == id
                    && !x.IsDeleted,
                cancellationToken);
        var key = photo?.FileId ?? photo?.SharePointItemId;
        if (photo is null
            || string.IsNullOrWhiteSpace(key)
            || !photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        try
        {
            var content = await fileStorageService.OpenReadAsync(key, cancellationToken);
            if (content is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, max-age=300, must-revalidate";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(content, photo.ContentType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Receipt photo content could not be loaded. ReceiptId: {ReceiptId}; PhotoId: {PhotoId}; StorageProvider: {StorageProvider}.",
                id,
                photoId,
                photo.StorageProvider);
            return NotFound();
        }
    }

    [Authorize(Policy = AccessPolicyNames.ReceiptEditEdit)]
    [HttpGet("{id:long}/Edit")]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var model = await dataService.GetReceiptEditAsync(id, cancellationToken);
        model.CanAdminOverride = await userAccessService.HasAccessAsync(
            User,
            ApplicationAreas.Receipts,
            PageAccessLevel.Admin,
            cancellationToken);
        if (model.CanAdminOverride)
        {
            model.AdminOverridePreview = await receiptInventoryOverrideService.GetPreviewAsync(id, cancellationToken);
        }
        return View(model);
    }

    [Authorize(Policy = AccessPolicyNames.ReceiptEditEdit)]
    [HttpPost("{id:long}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(long id, UpdateReceiptForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await dataService.UpdateReceiptAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Receipt updated.";
        return error is null
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(nameof(Edit), new { id });
    }

    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [HttpPost("{id:long}/AdminInventoryOverride")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminInventoryOverride(
        long id,
        AdminReceiptInventoryOverrideForm form,
        CancellationToken cancellationToken)
    {
        form.Id = id;
        var result = await receiptInventoryOverrideService.ApplyEditAsync(form, User, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return result.IsConflict
                ? Conflict(new { error = result.Error })
                : RedirectToAction(nameof(Edit), new { id });
        }
        TempData["Success"] = result.WasIdempotent
            ? $"Receipt inventory override {result.OverrideId:D} was already applied."
            : $"Receipt inventory override {result.OverrideId:D} was applied.";
        return RedirectToAction(nameof(OverrideDetails), new { overrideId = result.OverrideId });
    }

    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [HttpGet("{id:long}/Delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var model = await receiptPurgeService.GetDeletionConfirmationAsync(id, cancellationToken);
        var preview = model is null ? null : await receiptInventoryOverrideService.GetPreviewAsync(id, cancellationToken);
        if (model is not null && preview is not null)
        {
            model.CurrentInventory = preview.CurrentInventory;
            model.ConsumedBins = preview.ConsumedBins;
            model.ConcurrencyVersion = preview.ConcurrencyVersion;
            model.CurrentBalances = preview.Balances;
            model.Form.ExpectedConcurrencyVersion = preview.ConcurrencyVersion;
        }
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [HttpPost("{id:long}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, DeleteReceiptForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var result = await receiptInventoryOverrideService.VoidAsync(form, User, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Receipt voided through administrator override {result.OverrideId:D}."
            : result.Error;
        return result.Succeeded
            ? RedirectToAction(nameof(OverrideDetails), new { overrideId = result.OverrideId })
            : RedirectToAction(nameof(Delete), new { id });
    }

    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [HttpGet("Admin/Voided")]
    public async Task<IActionResult> AdminVoided(CancellationToken cancellationToken) =>
        View(await receiptInventoryOverrideService.GetVoidedReceiptsAsync(cancellationToken));

    [Authorize(Policy = AccessPolicyNames.ReceiptDeleteAdmin)]
    [HttpGet("Admin/Overrides/{overrideId:guid}")]
    public async Task<IActionResult> OverrideDetails(Guid overrideId, CancellationToken cancellationToken)
    {
        var model = await receiptInventoryOverrideService.GetAuditDetailAsync(overrideId, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("{id:long}/samples")]
    [Authorize(Policy = AccessPolicyNames.DailyQcEdit)]
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
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
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

    [HttpPost("{id:long}/photos/{photoId:long}/remove")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    public async Task<IActionResult> RemovePhoto(long id, long photoId, CancellationToken cancellationToken)
    {
        var error = await dataService.RemoveReceiptPhotoAsync(id, photoId, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Receipt photo removed.";
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
