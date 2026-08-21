using CropQc.Web.Services;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

public sealed class HomeController(
    IDashboardDataService dataService,
    IRoomInventoryLossService roomInventoryLossService,
    IInventoryByVarietyService? inventoryByVarietyService = null) : Controller
{
    [Authorize(Policy = AccessPolicyNames.DashboardView)]
    public async Task<IActionResult> Index([FromQuery] RoomSummaryFilterForm roomSummaryFilter, CancellationToken cancellationToken) =>
        View(await dataService.GetHomeDashboardAsync(roomSummaryFilter, cancellationToken));

    [HttpGet("/GrowerLots/Current")]
    [Authorize(Policy = AccessPolicyNames.GrowerLotsView)]
    public async Task<IActionResult> CurrentGrowerLots([FromQuery] CurrentGrowerLotsFilterForm filter, CancellationToken cancellationToken) =>
        View("GrowerLots", await dataService.GetCurrentGrowerLotsAsync(filter, cancellationToken));

    [HttpGet("/Inventory/ByVariety")]
    [Authorize(Policy = AccessPolicyNames.DashboardView)]
    public async Task<IActionResult> InventoryByVariety(string? facility, CancellationToken cancellationToken) =>
        View(await InventoryByVariety().GetSummaryAsync(facility, cancellationToken));

    [HttpGet("/Inventory/ByVariety/{varietyKey}")]
    [Authorize(Policy = AccessPolicyNames.DashboardView)]
    public async Task<IActionResult> InventoryVarietyDetail(
        string varietyKey,
        string? facility,
        CancellationToken cancellationToken)
    {
        var model = await InventoryByVariety().GetDetailAsync(varietyKey, facility, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    private IInventoryByVarietyService InventoryByVariety() =>
        inventoryByVarietyService ?? throw new InvalidOperationException("Inventory by Variety is not configured.");

    [HttpGet("/Rooms")]
    [Authorize(Policy = AccessPolicyNames.RoomsView)]
    public async Task<IActionResult> Rooms([FromQuery] RoomSummaryFilterForm roomSummaryFilter, CancellationToken cancellationToken) =>
        View(await dataService.GetRoomsAsync(roomSummaryFilter, cancellationToken));

    [HttpGet("/CropYearReview")]
    [Authorize(Policy = AccessPolicyNames.CropYearReviewView)]
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
    [HttpGet("/Rooms/{roomId:int}")]
    [Authorize(Policy = AccessPolicyNames.RoomsView)]
    public async Task<IActionResult> Room(int roomId, CancellationToken cancellationToken) =>
        View(await dataService.GetRoomDetailAsync(roomId, cancellationToken));

    [HttpPost("/Dashboard/Rooms/{roomId:int}/Projection")]
    [HttpPost("/Rooms/{roomId:int}/Projection")]
    [Authorize(Policy = AccessPolicyNames.RoomsView)]
    public async Task<IActionResult> RoomProjection(int roomId, [FromBody] RoomProjectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await dataService.GetRoomProjectionAsync(roomId, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("/Rooms/{roomId:int}/CountBreakdown")]
    [Authorize(Policy = AccessPolicyNames.RoomsView)]
    public async Task<IActionResult> RoomCountBreakdown(int roomId, CancellationToken cancellationToken) =>
        View(await dataService.GetRoomCountBreakdownAsync(roomId, cancellationToken));

    [HttpPost("/Dashboard/Rooms/{roomId:int}/Deplete")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    public async Task<IActionResult> DepleteRoom(int roomId, RoomDepletionForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var error = await dataService.CreateRoomDepletionAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room depletion recorded.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpPost("/Dashboard/Rooms/{roomId:int}/Depletions/{depletionId:long}/Void")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsAdmin)]
    public async Task<IActionResult> VoidRoomDepletion(int roomId, long depletionId, VoidRoomDepletionForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        form.DepletionId = depletionId;
        var error = await dataService.VoidRoomDepletionAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room depletion voided.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpPost("/Dashboard/Rooms/{roomId:int}/InventoryTrueUp")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsAdmin)]
    public async Task<IActionResult> InventoryTrueUp(int roomId, RoomInventoryTrueUpForm form, CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var error = await dataService.CreateRoomInventoryTrueUpAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room inventory true-up recorded.";
        return Redirect($"/BinsRun?RoomId={roomId}&Section=TrueUp");
    }

    [HttpPost("/Dashboard/Rooms/{roomId:int}/Transfer")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    public async Task<IActionResult> TransferRoomBins(int roomId, RoomTransferForm form, CancellationToken cancellationToken)
    {
        form.FromRoomId = roomId;
        var error = await dataService.CreateRoomTransferAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room transfer recorded.";
        return Redirect($"/BinsRun?RoomId={roomId}&Section=Transfer&SourceKey={Uri.EscapeDataString(form.SourceLotKey ?? "")}");
    }

    [HttpPost("/Rooms/{roomId:int}/DroppedBins")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkBinsDropped(
        int roomId,
        RoomInventoryLossForm form,
        CancellationToken cancellationToken)
    {
        form.RoomId = roomId;
        var error = await roomInventoryLossService.CreateAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Dropped bins recorded. The receipt quantity was not changed.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpPost("/Rooms/{roomId:int}/DroppedBins/{lossId:long}/Reverse")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReverseDroppedBins(
        int roomId,
        long lossId,
        ReverseRoomInventoryLossForm form,
        CancellationToken cancellationToken)
    {
        form.Id = lossId;
        var error = await roomInventoryLossService.ReverseAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Dropped bins restored through an auditable reversal.";
        return RedirectToAction(nameof(Room), new { roomId });
    }

    [HttpGet("/AccessDenied")]
    public IActionResult AccessDenied() => View();

    public IActionResult Error() => View();
}
