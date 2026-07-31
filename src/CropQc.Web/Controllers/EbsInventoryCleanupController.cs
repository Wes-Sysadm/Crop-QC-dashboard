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
}
