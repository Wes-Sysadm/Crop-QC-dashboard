using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Authorize(Roles = $"{BuiltInRoleNames.Manager},{BuiltInRoleNames.Admin}")]
public sealed class RoomSealingController(IRoomSealingService roomSealingService) : Controller
{
    [HttpGet("/Rooms/{roomId:int}/Seal")]
    public async Task<IActionResult> Confirm(int roomId, CancellationToken cancellationToken)
    {
        var model = await roomSealingService.GetConfirmationAsync(roomId, User, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("/Rooms/{roomId:int}/Seal")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Change(int roomId, RoomSealForm form, bool seal, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var error = await roomSealingService.ChangeStateAsync(form, seal, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room seal status updated.";
        return error is null
            ? RedirectToAction("Room", "Home", new { roomId })
            : RedirectToAction(nameof(Confirm), new { roomId });
    }
}
