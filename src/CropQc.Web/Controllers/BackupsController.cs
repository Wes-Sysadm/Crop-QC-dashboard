using CropQc.Web.Services;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

[Route("Admin/Backups")]
[Authorize(Policy = AccessPolicyNames.BackupsAdmin)]
public sealed class BackupsController(IBackupService backupService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await backupService.GetStatusAsync(cancellationToken));

    [HttpPost("RunNow")]
    public async Task<IActionResult> RunNow(CancellationToken cancellationToken)
    {
        var result = await backupService.RunBackupNowAsync(User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Settings")]
    public async Task<IActionResult> SaveSettings(BackupSettingsForm form, CancellationToken cancellationToken)
    {
        var error = await backupService.SaveSettingsAsync(form, User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Backup settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("TestAccess")]
    public async Task<IActionResult> TestAccess(CancellationToken cancellationToken)
    {
        var result = await backupService.TestGoogleDriveAccessAsync(User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
