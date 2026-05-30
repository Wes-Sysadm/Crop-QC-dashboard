using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin")]
[Authorize(Policy = "RequireAuthenticatedUser")]
public sealed class AdminController(
    IUserAdminService userAdminService,
    IAdminAuthorizationService authorizationService,
    IQcStationAdminService qcStationAdminService,
    IWebHostEnvironment environment,
    IConfiguration configuration) : Controller
{
    private const string FtaDllInstallerFileName = "FTADLL.exe";
    private const string FtaDllInstallerUrl = "https://drive.google.com/file/d/1iYy1v1-D8T-S4SgfHJOeuwoeJfsbcvoS/view?usp=drive_link";
    private const string QcStationInstallerFileName = "CropQcStationSetup.msi";

    [HttpGet("Users")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        View(await userAdminService.GetUsersAsync(cancellationToken));

    [HttpGet("Downloads")]
    [Authorize(Policy = "RequireAdmin")]
    public IActionResult Downloads()
    {
        var installerPath = GetQcStationInstallerPath();
        var installerExists = System.IO.File.Exists(installerPath);
        var model = new AdminDownloadsViewModel
        {
            Downloads =
            [
                new(
                    "FTA DLL Installer",
                    FtaDllInstallerFileName,
                    "Installer/runtime files needed for the GUSS FTA DLL integration on QC Station computers.",
                    FtaDllInstallerUrl,
                    "Opens the shared Google Drive download page. Use only on internal company QC Station computers. Install before running QC Station RealDll mode. After installation, run the Crop QC Station app.",
                    OpensInNewTab: true,
                    ActionText: "Open Google Drive Download"),
                new(
                    "QC Station App Installer",
                    QcStationInstallerFileName,
                    "Signed MSI installer for the Crop QC Station WinForms app. Installs the app, Start Menu shortcut, and cropqcstation:// browser link handler. It does not contain station API keys.",
                    installerExists ? "/Admin/Downloads/QcStationInstaller" : "",
                    installerExists
                        ? "Run this installer on each QC computer, then import that computer's station config JSON from Admin QC Stations."
                        : "QC Station installer has not been deployed yet. Build/sign CropQcStationSetup.msi and place it in App_Data/Downloads or the configured installer download path.",
                    IsAvailable: installerExists,
                    ActionText: "Download MSI"),
                new(
                    "QC Station Configs",
                    "qcstation.settings.json",
                    "Station-specific config JSON generated from Admin QC Stations. Contains the station code/key and must be imported into the installed QC Station app.",
                    "/Admin/QcStations",
                    "Each QC computer needs its own station record and API key. The MSI is shared across stations; the config JSON is the secret per-station file.",
                    ActionText: "Manage QC Stations")
            ]
        };

        return View(model);
    }

    [HttpGet("Downloads/QcStationInstaller")]
    [Authorize(Policy = "RequireAdmin")]
    public IActionResult DownloadQcStationInstaller()
    {
        var installerPath = GetQcStationInstallerPath();
        if (!System.IO.File.Exists(installerPath))
        {
            TempData["Error"] = "QC Station installer has not been deployed yet. Build/sign CropQcStationSetup.msi and place it in the configured installer download location.";
            return RedirectToAction(nameof(Downloads));
        }

        if (!string.Equals(Path.GetFileName(installerPath), QcStationInstallerFileName, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        return PhysicalFile(installerPath, "application/octet-stream", QcStationInstallerFileName);
    }

    [HttpGet("QcStations")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> QcStations([FromQuery] string? search, [FromQuery] string? warehouseCode, [FromQuery] string activeFilter = "Active", CancellationToken cancellationToken = default) =>
        View(await qcStationAdminService.GetStationsAsync(search, warehouseCode, activeFilter, cancellationToken));

    [HttpPost("QcStations/Create")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> CreateQcStation(QcStationForm form, CancellationToken cancellationToken = default)
    {
        var (error, download) = await qcStationAdminService.CreateAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null || download is null)
        {
            TempData["Error"] = error ?? "Station config could not be generated.";
            return RedirectToAction(nameof(QcStations));
        }

        return DownloadQcStationConfig(download);
    }

    [HttpPost("QcStations/Update")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> UpdateQcStation(QcStationForm form, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.UpdateAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station updated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/Deactivate")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> DeactivateQcStation(int id, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.SetActiveAsync(id, false, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station deactivated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/Reactivate")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> ReactivateQcStation(int id, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.SetActiveAsync(id, true, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station reactivated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/RotateKey")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public async Task<IActionResult> RotateQcStationKey(int id, CancellationToken cancellationToken = default)
    {
        var (error, download) = await qcStationAdminService.RotateKeyAsync(id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null || download is null)
        {
            TempData["Error"] = error ?? "Station config could not be generated.";
            return RedirectToAction(nameof(QcStations));
        }

        return DownloadQcStationConfig(download);
    }

    [HttpPost("QcStations/DownloadConfig")]
    [Authorize(Policy = "RequireManagerOrAdmin")]
    public IActionResult DownloadExistingQcStationConfig()
    {
        TempData["Error"] = "Rotate key to generate a new downloadable station config. Raw station keys are not stored after creation or rotation.";
        return RedirectToAction(nameof(QcStations));
    }

    private FileContentResult DownloadQcStationConfig(QcStationConfigDownload download) =>
        File(System.Text.Encoding.UTF8.GetBytes(download.Json), "application/json", download.FileName);

    private string GetQcStationInstallerPath() =>
        configuration["QcStation:InstallerPath"]
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "Downloads", QcStationInstallerFileName);

    [HttpPost("Users/Add")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> AddUser(AddUserForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.AddUserAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User added.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Update")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> UpdateUser(UpdateUserAccessForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.UpdateUserAccessAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User access updated.";
        return RedirectToAction(nameof(Users));
    }

}
