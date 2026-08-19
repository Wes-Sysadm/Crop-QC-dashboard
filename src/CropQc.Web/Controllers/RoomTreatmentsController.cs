using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Authorize]
public sealed class RoomTreatmentsController(
    IRoomTreatmentService treatmentService,
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
    public async Task<IActionResult> Save(int roomId, RoomTreatmentApplyForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var result = await treatmentService.ApplyAsync(form, cancellationToken);
        if (result.Error is not null)
        {
            var model = await treatmentService.GetApplyPageAsync(form, true, cancellationToken);
            model.Error = result.Error;
            return View("Apply", model);
        }
        TempData["Success"] = "Treatment application recorded without changing inventory quantity.";
        return Redirect($"/Rooms/{roomId}#treatment-history");
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
