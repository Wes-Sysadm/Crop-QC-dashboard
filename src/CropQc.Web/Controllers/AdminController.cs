using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminController(IUserAdminService userAdminService, IAdminAuthorizationService authorizationService, IWebHostEnvironment environment) : Controller
{
    private const string FtaDllInstallerFileName = "FTADLL.exe";

    [HttpGet("Users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        View(await userAdminService.GetUsersAsync(cancellationToken));

    [HttpGet("Downloads")]
    public IActionResult Downloads()
    {
        var installerPath = GetWhitelistedDownloadPath(FtaDllInstallerFileName);
        var model = new AdminDownloadsViewModel
        {
            Downloads =
            [
                new(
                    "FTA DLL Installer",
                    FtaDllInstallerFileName,
                    "Installer/runtime files needed for the GUSS FTA DLL integration on QC Station computers.",
                    "Install on each FTA-connected Windows computer. QC Station RealDll mode also requires the WinForms x86 station app. Use only for internal company computers.",
                    System.IO.File.Exists(installerPath))
            ]
        };

        return View(model);
    }

    [HttpGet("Downloads/{fileName}")]
    public IActionResult Download(string fileName)
    {
        if (!string.Equals(fileName, FtaDllInstallerFileName, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var installerPath = GetWhitelistedDownloadPath(FtaDllInstallerFileName);
        if (!System.IO.File.Exists(installerPath))
        {
            TempData["Error"] = "FTADLL.exe is not deployed on this server yet. Place it in App_Data/Downloads on the web server, then redeploy or restart.";
            return RedirectToAction(nameof(Downloads));
        }

        return PhysicalFile(installerPath, "application/vnd.microsoft.portable-executable", FtaDllInstallerFileName);
    }

    [HttpPost("Users/Add")]
    public async Task<IActionResult> AddUser(AddUserForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.AddUserAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User added.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Update")]
    public async Task<IActionResult> UpdateUser(UpdateUserAccessForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.UpdateUserAccessAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User access updated.";
        return RedirectToAction(nameof(Users));
    }

    private string GetWhitelistedDownloadPath(string fileName) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "Downloads", fileName);
}
