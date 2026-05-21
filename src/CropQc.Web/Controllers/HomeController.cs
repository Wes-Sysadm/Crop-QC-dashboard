using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

public sealed class HomeController(IDashboardDataService dataService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await dataService.GetHomeDashboardAsync(cancellationToken));

    public IActionResult Error() => View();
}
