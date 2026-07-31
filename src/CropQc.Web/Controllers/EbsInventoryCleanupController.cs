using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/EbsInventoryCleanup")]
[Authorize(Policy = AccessPolicyNames.HistoricalInventoryCleanupAdmin)]
public sealed class EbsInventoryCleanupController(IEbsInventoryCleanupService service) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        View(await service.GetReviewAsync(page, pageSize, User, cancellationToken));

    [HttpPost("Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(ApproveEbsInventoryCleanupForm form, CancellationToken cancellationToken)
    {
        var error = await service.ApproveAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Historical room-lot cleanup was recorded as a linked Bins Run deduction.";
        return RedirectToAction(nameof(Index));
    }
}
