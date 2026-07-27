using CropQc.Web.Services;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CropQc.Web.Controllers;

[Route("Admin/Backups")]
public sealed class BackupsController(
    IBackupService backupService,
    IBackupNotificationService notificationService) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = AccessPolicyNames.BackupHistoryView)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await backupService.GetStatusAsync(cancellationToken));

    [HttpPost("RunNow")]
    [Authorize(Policy = AccessPolicyNames.BackupHistoryAdmin)]
    public async Task<IActionResult> RunNow(CancellationToken cancellationToken)
    {
        var result = await backupService.RunBackupNowAsync(User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Settings")]
    [Authorize(Policy = AccessPolicyNames.BackupHistoryAdmin)]
    public async Task<IActionResult> SaveSettings(BackupSettingsForm form, CancellationToken cancellationToken)
    {
        var error = await backupService.SaveSettingsAsync(form, User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Backup settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("TestAccess")]
    [Authorize(Policy = AccessPolicyNames.BackupHistoryAdmin)]
    public async Task<IActionResult> TestAccess(CancellationToken cancellationToken)
    {
        var result = await backupService.TestGoogleDriveAccessAsync(User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Notifications/{id:long}/Retry")]
    [Authorize(Policy = AccessPolicyNames.BackupHistoryAdmin)]
    public async Task<IActionResult> RetryNotification(long id, CancellationToken cancellationToken)
    {
        var error = await notificationService.RetryAsync(id, User.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Backup notification queued for retry.";
        return RedirectToAction(nameof(Index));
    }
}
