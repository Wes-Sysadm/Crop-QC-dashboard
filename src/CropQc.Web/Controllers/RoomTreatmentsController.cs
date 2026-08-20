using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Authorize]
public sealed class RoomTreatmentsController(
    IRoomTreatmentService treatmentService,
    ITreatmentReportAttachmentService attachmentService,
    IBusinessTimeService businessTime) : Controller
{
    [HttpGet("/Rooms/{roomId:int}/Treatments/Apply")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    public async Task<IActionResult> Apply(int roomId, CancellationToken cancellationToken)
    {
        var form = new RoomTreatmentApplyForm { RoomId = roomId, AppliedAt = businessTime.NowPacific };
        return View(await treatmentService.GetApplyPageAsync(form, false, cancellationToken));
    }

    [HttpPost("/Rooms/{roomId:int}/Treatments/Review")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int roomId, RoomTreatmentApplyForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        form.ConfirmedReview = false;
        return View("Apply", await treatmentService.GetApplyPageAsync(form, true, cancellationToken));
    }

    [HttpPost("/Rooms/{roomId:int}/Treatments")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        int roomId,
        RoomTreatmentApplyForm form,
        TreatmentReportUploadForm treatmentReport,
        CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var result = await treatmentService.ApplyAsync(form, cancellationToken);
        if (result.Error is not null)
        {
            var model = await treatmentService.GetApplyPageAsync(form, true, cancellationToken);
            model.Error = result.Error;
            return View("Apply", model);
        }
        var attachmentResult = await attachmentService.UploadAsync(result.ApplicationId!.Value, treatmentReport, User, cancellationToken);
        if (attachmentResult.Failures.Count > 0)
        {
            TempData["Warning"] = $"Treatment application was recorded, but {attachmentResult.Failures.Count} of {treatmentReport.Files.Count} report files could not be uploaded. "
                + string.Join(" ", attachmentResult.Failures);
        }
        else
        {
            TempData["Success"] = attachmentResult.Uploaded == 0
                ? "Treatment application recorded without changing inventory quantity."
                : $"Treatment application and {attachmentResult.Uploaded} report attachment{(attachmentResult.Uploaded == 1 ? "" : "s")} recorded without changing inventory quantity.";
        }
        return Redirect($"/Rooms/{roomId}#treatment-history");
    }

    [HttpPost("/Rooms/{roomId:int}/Treatments/{applicationId:long}/Reports")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReport(int roomId, long applicationId, TreatmentReportUploadForm form, CancellationToken cancellationToken)
    {
        var result = await attachmentService.UploadAsync(applicationId, form, User, cancellationToken);
        TempData[result.Failures.Count == 0 ? "Success" : "Error"] = result.Failures.Count == 0
            ? $"{result.Uploaded} treatment report attachment{(result.Uploaded == 1 ? "" : "s")} added."
            : string.Join(" ", result.Failures);
        return Redirect($"/Rooms/{roomId}#treatment-{applicationId}");
    }

    [HttpGet("/RoomTreatments/{applicationId:long}/Reports/{attachmentId:long}/Content")]
    [Authorize(Policy = AccessPolicyNames.RoomsView)]
    public async Task<IActionResult> ReportContent(long applicationId, long attachmentId, CancellationToken cancellationToken)
    {
        var result = await attachmentService.OpenReadAsync(applicationId, attachmentId, User, cancellationToken);
        if (result.Content is null || result.ContentType is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=300, must-revalidate";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
    }

    [HttpPost("/Rooms/{roomId:int}/Treatments/{applicationId:long}/Reports/{attachmentId:long}/Remove")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveReport(
        int roomId,
        long applicationId,
        long attachmentId,
        RemoveTreatmentReportForm form,
        CancellationToken cancellationToken)
    {
        var error = await attachmentService.RemoveAsync(applicationId, attachmentId, form.Reason, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Treatment report attachment removed; the treatment application and report metadata were retained.";
        return Redirect($"/Rooms/{roomId}#treatment-{applicationId}");
    }

    [HttpPost("/Rooms/{roomId:int}/Treatments/{applicationId:long}/Reverse")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(int roomId, long applicationId, ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken)
    {
        form.Id = applicationId;
        var error = await treatmentService.ReverseAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Treatment application reversed; original evidence was retained.";
        return Redirect($"/Rooms/{roomId}#treatment-history");
    }
}
