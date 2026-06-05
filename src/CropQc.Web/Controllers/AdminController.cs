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
    IDataCleanupService dataCleanupService,
    IConfiguration configuration) : Controller
{
    private const string FtaDllInstallerFileName = "FTADLL.exe";
    private const string FtaDllInstallerUrl = "https://drive.google.com/file/d/1iYy1v1-D8T-S4SgfHJOeuwoeJfsbcvoS/view?usp=drive_link";
    private const string QcStationInstallerFileName = "CropQcStationSetup.msi";
    private const string FtaBorlandDependencyFileName = "borlndmm.dll";

    [HttpGet("Users")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        View(await userAdminService.GetUsersAsync(cancellationToken));

    [HttpGet("Downloads")]
    [Authorize(Policy = "RequireAdmin")]
    public IActionResult Downloads()
    {
        var masterFolderUrl = configuration["Downloads:MasterFolderUrl"];
        var masterFolderConfigured = !string.IsNullOrWhiteSpace(masterFolderUrl);
        var installerUrl = configuration["Downloads:QcStationInstallerUrl"];
        var installerConfigured = !string.IsNullOrWhiteSpace(installerUrl);
        var borlandDependencyUrl = configuration["Downloads:FtaBorlndmmUrl"];
        var borlandDependencyConfigured = !string.IsNullOrWhiteSpace(borlandDependencyUrl);
        var model = new AdminDownloadsViewModel
        {
            Downloads =
            [
                new(
                    "Hosted Files Folder",
                    "Google Drive folder",
                    "Open the shared Google Drive folder containing CropQcStationSetup.msi, FTADLL.exe, borlndmm.dll, and other hosted support files.",
                    masterFolderUrl ?? "",
                    masterFolderConfigured
                        ? "Opens the shared Google Drive folder. Use this as the master production download location for installer/support files."
                        : "Downloads folder link is not configured. Set Downloads__MasterFolderUrl in Render.",
                    IsAvailable: masterFolderConfigured,
                    OpensInNewTab: masterFolderConfigured,
                    ActionText: "Open Google Drive Folder"),
                new(
                    "FTA DLL Installer",
                    FtaDllInstallerFileName,
                    "Installer/runtime files needed for the GUSS FTA DLL integration on QC Station computers.",
                    FtaDllInstallerUrl,
                    "Opens the shared Google Drive download page. Use only on internal company QC Station computers. Install before running QC Station RealDll mode. After installation, run the Crop QC Station app.",
                    OpensInNewTab: true,
                    ActionText: "Open Google Drive Download"),
                new(
                    "Crop QC Station App Installer",
                    QcStationInstallerFileName,
                    "Installs the Crop QC Station WinForms app used for FTA pressure capture and station sync.",
                    installerUrl ?? "",
                    installerConfigured
                        ? "Opens the shared Google Drive download page. Install Crop QC Station first. Then download station config JSON from Admin QC Stations and import it inside the app with Import / Replace Station Config."
                        : "QC Station installer link is not configured. Upload CropQcStationSetup.msi to Google Drive and set Downloads__QcStationInstallerUrl in Render.",
                    IsAvailable: installerConfigured,
                    OpensInNewTab: installerConfigured,
                    ActionText: "Open Google Drive Download"),
                new(
                    "FTA borlndmm.dll Dependency",
                    FtaBorlandDependencyFileName,
                    "Known-good x86 borlndmm.dll used by the FTA DLL. Use only when diagnostics report the installed borlndmm.dll is 64-bit or invalid.",
                    borlandDependencyUrl ?? "",
                    borlandDependencyConfigured
                        ? "Opens the shared Google Drive download page. Back up the existing borlndmm.dll before replacing it, then rerun QC Station FTA Diagnostics."
                        : "FTA borlndmm.dll dependency link is not configured. Upload the known-good x86 borlndmm.dll to Google Drive and set Downloads__FtaBorlndmmUrl in Render.",
                    IsAvailable: borlandDependencyConfigured,
                    OpensInNewTab: borlandDependencyConfigured,
                    ActionText: "Open Google Drive Download")
            ]
        };

        return View(model);
    }

    [HttpGet("DataCleanup")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> DataCleanup([FromQuery] DataCleanupFilterForm filter, CancellationToken cancellationToken)
    {
        if (!IsDataCleanupAllowed())
        {
            return Forbid();
        }

        return View(await dataCleanupService.BuildPageAsync(filter, cancellationToken));
    }

    [HttpPost("DataCleanup/Execute")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> ExecuteDataCleanup(DataCleanupFilterForm filter, CancellationToken cancellationToken)
    {
        if (!IsDataCleanupAllowed())
        {
            return Forbid();
        }

        var (preview, error) = await dataCleanupService.ExecuteAsync(filter, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? $"Cleanup complete. Samples affected: {preview.SamplesAffected}. Receipts affected: {preview.ReceiptsAffected}.";
        return RedirectToAction(nameof(DataCleanup), filter);
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

    private bool IsDataCleanupAllowed()
    {
        var email = authorizationService.GetEmail(User);
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return GetDataCleanupAllowedEmails().Contains(email.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> GetDataCleanupAllowedEmails()
    {
        var configured = configuration["DataCleanup:AllowedEmails"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = "wes@fruitandland.com";
        }

        return configured
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

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
