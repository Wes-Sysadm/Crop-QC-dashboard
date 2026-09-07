using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Authorize(Roles = $"{BuiltInRoleNames.Manager},{BuiltInRoleNames.Admin}")]
public sealed class HarvestWatchController(IHarvestWatchService harvestWatchService) : Controller
{
    [HttpPost("/Rooms/{roomId:int}/HarvestWatch/Deploy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deploy(int roomId, HarvestWatchDeployForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var result = await harvestWatchService.DeployAsync(form, User, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "HarvestWatch deployment recorded. Verification email status is shown below."
            : result.Error;
        return RedirectToAction("Room", "Home", new { roomId });
    }

    [HttpPost("/Rooms/{roomId:int}/HarvestWatch/{deploymentId:long}/Retire")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retire(int roomId, long deploymentId, HarvestWatchRetireForm form, CancellationToken cancellationToken)
    {
        var error = await harvestWatchService.RetireAsync(roomId, deploymentId, form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "HarvestWatch deployment retired. Its history is retained.";
        return RedirectToAction("Room", "Home", new { roomId });
    }
}
