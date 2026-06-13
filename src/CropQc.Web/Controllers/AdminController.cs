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
        var model = new AdminDownloadsViewModel
        {
            Downloads =
            [
                new(
                    "Hosted Files Folder",
                    "Google Drive folder",
                    "Open the shared Google Drive folder for installers and support files.",
                    masterFolderUrl ?? "",
                    masterFolderConfigured
                        ? "Use this folder for QC Station installers, FTADLL files, and support utilities."
                        : "Hosted files folder is not configured. Set it under Admin -> Configuration or Render environment settings.",
                    IsAvailable: masterFolderConfigured,
                    OpensInNewTab: masterFolderConfigured,
                    ActionText: "Open Google Drive Folder")
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
