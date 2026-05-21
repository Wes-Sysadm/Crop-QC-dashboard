using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

public sealed class ReceiptsController(IDashboardDataService dataService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReceiptSearchForm search, CancellationToken cancellationToken) =>
        View(await dataService.SearchReceiptsAsync(search, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateReceiptForm form, CancellationToken cancellationToken)
    {
        var error = await dataService.CreateReceiptAsync(form, cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken) =>
        View(await dataService.GetReceiptDetailAsync(id, cancellationToken));
}
