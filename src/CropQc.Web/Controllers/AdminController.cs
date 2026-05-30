using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminController(
    IUserAdminService userAdminService,
    IAdminAuthorizationService authorizationService,
    IQcStationAdminService qcStationAdminService) : Controller
{
    private const string FtaDllInstallerFileName = "FTADLL.exe";
    private const string FtaDllInstallerUrl = "https://drive.google.com/file/d/1iYy1v1-D8T-S4SgfHJOeuwoeJfsbcvoS/view?usp=drive_link";

    [HttpGet("Users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        View(await userAdminService.GetUsersAsync(cancellationToken));

    [HttpGet("Downloads")]
    public IActionResult Downloads()
    {
        var model = new AdminDownloadsViewModel
        {
            Downloads =
            [
                new(
                    "FTA DLL Installer",
                    FtaDllInstallerFileName,
                    "Installer/runtime files needed for the GUSS FTA DLL integration on QC Station computers.",
                    FtaDllInstallerUrl,
                    "Opens the shared Google Drive download page. Use only on internal company QC Station computers. Install before running QC Station RealDll mode. After installation, run the WinForms x86 QC Station app."),
                new(
                    "QC Station Configs",
                    "setup package .zip",
                    "Per-station setup packages generated from Admin QC Stations. Full packages install the WinForms app, station config, and browser link handler.",
                    "/Admin/QcStations",
                    "Each QC computer needs its own station record and API key. FTADLL.exe is separate and still required for FTA-connected computers; do not use one shared API key for all computers.")
            ]
        };

        return View(model);
    }

    [HttpGet("QcStations")]
    public async Task<IActionResult> QcStations([FromQuery] string? search, [FromQuery] string? warehouseCode, [FromQuery] string activeFilter = "Active", CancellationToken cancellationToken = default) =>
        View(await qcStationAdminService.GetStationsAsync(search, warehouseCode, activeFilter, cancellationToken));

    [HttpPost("QcStations/Create")]
    public async Task<IActionResult> CreateQcStation(QcStationForm form, string downloadType = "package", CancellationToken cancellationToken = default)
    {
        if (RequestsSetupPackage(downloadType) && !qcStationAdminService.AppPayloadAvailable)
        {
            TempData["Error"] = "QC Station app payload is missing. Full setup packages cannot be generated. Deploy the WinForms payload before creating station setup packages.";
            return RedirectToAction(nameof(QcStations));
        }

        var (error, download) = await qcStationAdminService.CreateAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null || download is null)
        {
            TempData["Error"] = error ?? "Station config could not be generated.";
            return RedirectToAction(nameof(QcStations));
        }

        return DownloadQcStationConfig(download, downloadType);
    }

    [HttpPost("QcStations/Update")]
    public async Task<IActionResult> UpdateQcStation(QcStationForm form, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.UpdateAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station updated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/Deactivate")]
    public async Task<IActionResult> DeactivateQcStation(int id, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.SetActiveAsync(id, false, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station deactivated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/Reactivate")]
    public async Task<IActionResult> ReactivateQcStation(int id, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.SetActiveAsync(id, true, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station reactivated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/RotateKey")]
    public async Task<IActionResult> RotateQcStationKey(int id, string downloadType = "package", CancellationToken cancellationToken = default)
    {
        if (RequestsSetupPackage(downloadType) && !qcStationAdminService.AppPayloadAvailable)
        {
            TempData["Error"] = "QC Station app payload is missing. Full setup packages cannot be generated. Deploy the WinForms payload before rotating station keys for setup packages.";
            return RedirectToAction(nameof(QcStations));
        }

        var (error, download) = await qcStationAdminService.RotateKeyAsync(id, authorizationService.GetEmail(User) ?? "", cancellationToken);
        if (error is not null || download is null)
        {
            TempData["Error"] = error ?? "Station config could not be generated.";
            return RedirectToAction(nameof(QcStations));
        }

        return DownloadQcStationConfig(download, downloadType);
    }

    [HttpPost("QcStations/DownloadConfig")]
    public IActionResult DownloadExistingQcStationConfig()
    {
        TempData["Error"] = "Rotate key to generate a new downloadable config or setup package.";
        return RedirectToAction(nameof(QcStations));
    }

    private FileContentResult DownloadQcStationConfig(QcStationConfigDownload download, string downloadType)
    {
        if (string.Equals(downloadType, "json", StringComparison.OrdinalIgnoreCase))
        {
            return File(System.Text.Encoding.UTF8.GetBytes(download.Json), "application/json", download.FileName);
        }

        return File(download.PackageBytes, "application/zip", download.PackageFileName);
    }

    private static bool RequestsSetupPackage(string downloadType) =>
        string.Equals(downloadType, "package", StringComparison.OrdinalIgnoreCase);

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

}
