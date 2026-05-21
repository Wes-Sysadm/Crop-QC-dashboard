using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("MasterData")]
public sealed class MasterDataController(IDashboardDataService dataService) : Controller
{
    [HttpGet("")]
    [HttpGet("{type}")]
    public async Task<IActionResult> Index(string? type, CancellationToken cancellationToken) =>
        View(await dataService.GetMasterDataPageAsync(type ?? "index", cancellationToken));
}
