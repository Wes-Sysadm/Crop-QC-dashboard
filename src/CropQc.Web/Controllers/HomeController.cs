using CropQc.Web.Services;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

public sealed class HomeController(IDashboardDataService dataService) : Controller
{
    public async Task<IActionResult> Index([FromQuery] RoomSummaryFilterForm roomSummaryFilter, CancellationToken cancellationToken) =>
        View(await dataService.GetHomeDashboardAsync(roomSummaryFilter, cancellationToken));

    [HttpGet("/Dashboard/Rooms/{roomId:int}")]
    public async Task<IActionResult> Room(int roomId, CancellationToken cancellationToken) =>
        View(await dataService.GetRoomDetailAsync(roomId, cancellationToken));

    [HttpPost("/Dashboard/Rooms/{roomId:int}/Deplete")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> DepleteRoom(int roomId, RoomDepletionForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var error = await dataService.CreateRoomDepletionAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room depletion recorded.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpPost("/Dashboard/Rooms/{roomId:int}/Depletions/{depletionId:long}/Void")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> VoidRoomDepletion(int roomId, long depletionId, VoidRoomDepletionForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        form.DepletionId = depletionId;
        var error = await dataService.VoidRoomDepletionAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room depletion voided.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpGet("/AccessDenied")]
    public IActionResult AccessDenied() => View();

    public IActionResult Error() => View();
}
