using System.Security.Claims;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("EndOfDayFill")]
[Authorize(Policy = "RequireAuthenticatedUser")]
public sealed class EndOfDayFillController(IEndOfDayFillService service) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int? groupId, CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!await service.HasActiveAssignmentAsync(email, cancellationToken)) return Forbid();
        if (groupId is not null && !await service.HasGroupAssignmentAsync(email, groupId.Value, cancellationToken)) return Forbid();
        return View(await service.GetPreviewAsync(email, groupId, cancellationToken));
    }

    [HttpPost("Send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(EndOfDayFillSendForm form, CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!await service.HasActiveAssignmentAsync(email, cancellationToken)) return Forbid();
        if (!await service.HasGroupAssignmentAsync(email, form.GroupId, cancellationToken)) return Forbid();
        var result = await service.SendAsync(email, form, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return result.Success && result.SendId is not null
            ? RedirectToAction(nameof(HistoryDetail), new { id = result.SendId.Value })
            : RedirectToAction(nameof(Index), new { groupId = form.GroupId });
    }

    [HttpGet("History")]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!await service.HasActiveAssignmentAsync(email, cancellationToken)) return Forbid();
        return View(await service.GetHistoryAsync(email, cancellationToken));
    }

    [HttpGet("History/{id:long}")]
    public async Task<IActionResult> HistoryDetail(long id, CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!await service.HasActiveAssignmentAsync(email, cancellationToken)) return Forbid();
        var model = await service.GetHistoryDetailAsync(email, id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }
}
