using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Shared.Storage;
using CropQc.Data;
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
    IUserAccessService userAccessService,
    IAdminManagementService adminManagementService,
    IAdminAuthorizationService adminAuthorizationService,
    CropQcDbContext dbContext,
    IReceivingExportService exportService,
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
    [Authorize(Policy = AccessPolicyNames.ReceiptsView)]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetReceiptDetailAsync(id, cancellationToken));

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
