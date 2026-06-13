using CropQc.Web.Services;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

public sealed class HomeController(IDashboardDataService dataService) : Controller
{
    public async Task<IActionResult> Index([FromQuery] RoomSummaryFilterForm roomSummaryFilter, CancellationToken cancellationToken) =>
        View(await dataService.GetHomeDashboardAsync(roomSummaryFilter, cancellationToken));

    [HttpGet("/GrowerLots/Current")]
    public async Task<IActionResult> CurrentGrowerLots([FromQuery] CurrentGrowerLotsFilterForm filter, CancellationToken cancellationToken) =>
        View("GrowerLots", await dataService.GetCurrentGrowerLotsAsync(filter, cancellationToken));

    [HttpGet("/CropYearReview")]
    [Authorize]
    public async Task<IActionResult> CropYearReview([FromQuery] CropYearReviewFilterForm filter, CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!string.Equals(email, "wes@fruitandland.com", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        return View(await dataService.GetCropYearReviewAsync(filter, cancellationToken));
    }

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

    [HttpPost("/Dashboard/Rooms/{roomId:int}/InventoryTrueUp")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> InventoryTrueUp(int roomId, RoomInventoryTrueUpForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var error = await dataService.CreateRoomInventoryTrueUpAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room inventory true-up recorded.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpGet("/AccessDenied")]
    public IActionResult AccessDenied() => View();

    public IActionResult Error() => View();
}
