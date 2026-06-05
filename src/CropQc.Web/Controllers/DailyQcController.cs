using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

public sealed class DailyQcController(IDashboardDataService dataService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int? warehouseId, [FromQuery] string? status, CancellationToken cancellationToken) =>
        View(await dataService.GetDailyQcDashboardAsync(warehouseId, status, cancellationToken));
}
