using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("MasterData/end-of-day-fill-groups")]
[Authorize(Policy = AccessPolicyNames.MasterDataAdmin)]
public sealed class EndOfDayFillAdminController(IEndOfDayFillAdminService service, IAdminAuthorizationService authorizationService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await service.GetPageAsync(cancellationToken));

    [HttpPost("group")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGroup(EndOfDayFillGroupForm form, CancellationToken cancellationToken)
    {
        var error = await service.SaveGroupAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "End of Day Fill report group saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("recipient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRecipient(EndOfDayFillRecipientForm form, CancellationToken cancellationToken)
    {
        var error = await service.SaveRecipientAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "End of Day Fill recipient saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("~/Admin/Users/EndOfDayFillGroups")]
    [Authorize(Policy = AccessPolicyNames.UsersAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUserAssignments(EndOfDayFillUserAssignmentsForm form, CancellationToken cancellationToken)
    {
        var error = await service.SaveUserAssignmentsAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "End of Day Fill report assignments updated.";
        return Redirect("/Admin/Users");
    }
}
