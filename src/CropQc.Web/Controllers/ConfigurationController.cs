using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/Configuration")]
[Authorize(Policy = "RequireAdmin")]
public sealed class ConfigurationController(IAdminManagementService adminService, IAdminAuthorizationService authorizationService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await adminService.GetConfigurationAsync(true, cancellationToken));
    }

    [HttpPost("Save")]
    public async Task<IActionResult> Save(ConfigurationEditForm form, CancellationToken cancellationToken)
    {
        var error = await adminService.SaveConfigurationAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Configuration saved.";
        return RedirectToAction(nameof(Index));
    }
}
