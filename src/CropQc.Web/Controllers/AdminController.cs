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
    IVarietyColorService varietyColorService,
    IUserAccessService userAccessService,
    IConfiguration configuration) : Controller
{
    [HttpGet("Users")]
    [Authorize(Policy = AccessPolicyNames.UsersAdmin)]
    public async Task<IActionResult> Users([FromQuery] int? roleId, CancellationToken cancellationToken) =>
        View(await userAdminService.GetUsersAsync(roleId, cancellationToken));

    [HttpGet("Downloads")]
    [Authorize(Policy = AccessPolicyNames.DownloadsView)]
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

    [HttpGet("VarietyColors")]
    [Authorize(Policy = AccessPolicyNames.VarietyColorsView)]
    public IActionResult VarietyColors(CancellationToken cancellationToken)
    {
        _ = userAccessService;
        return Redirect("/MasterData/fruit-profiles");
    }

    [HttpPost("VarietyColors/Save")]
    [Authorize(Policy = AccessPolicyNames.VarietyColorsAdmin)]
    public async Task<IActionResult> SaveVarietyColor(VarietyColorForm form, CancellationToken cancellationToken)
    {
        var error = await varietyColorService.SaveAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Variety color saved.";
        return Redirect("/MasterData/fruit-profiles");
    }

    [HttpPost("VarietyColors/Reset")]
    [Authorize(Policy = AccessPolicyNames.VarietyColorsAdmin)]
    public async Task<IActionResult> ResetVarietyColor(VarietyColorForm form, CancellationToken cancellationToken)
    {
        var error = await varietyColorService.ResetAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Variety color reset to default.";
        return Redirect("/MasterData/fruit-profiles");
    }

    [HttpGet("DataCleanup")]
    [Authorize(Policy = AccessPolicyNames.DataCleanupAdmin)]
    public async Task<IActionResult> DataCleanup([FromQuery] DataCleanupFilterForm filter, CancellationToken cancellationToken)
        => View(await dataCleanupService.BuildPageAsync(filter, cancellationToken));

    [HttpPost("DataCleanup/Execute")]
    [Authorize(Policy = AccessPolicyNames.DataCleanupAdmin)]
    public async Task<IActionResult> ExecuteDataCleanup(DataCleanupFilterForm filter, CancellationToken cancellationToken)
    {
        var (preview, error) = await dataCleanupService.ExecuteAsync(filter, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? $"Cleanup complete. Samples affected: {preview.SamplesAffected}. Receipts affected: {preview.ReceiptsAffected}.";
        return RedirectToAction(nameof(DataCleanup), filter);
    }

    [HttpGet("QcStations")]
    [Authorize(Policy = AccessPolicyNames.QcStationsView)]
    public async Task<IActionResult> QcStations([FromQuery] string? search, [FromQuery] string? warehouseCode, [FromQuery] string activeFilter = "Active", CancellationToken cancellationToken = default) =>
        View(await qcStationAdminService.GetStationsAsync(search, warehouseCode, activeFilter, cancellationToken));

    [HttpPost("QcStations/Create")]
    [Authorize(Policy = AccessPolicyNames.QcStationsAdmin)]
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
    [Authorize(Policy = AccessPolicyNames.QcStationsAdmin)]
    public async Task<IActionResult> UpdateQcStation(QcStationForm form, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.UpdateAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station updated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/Deactivate")]
    [Authorize(Policy = AccessPolicyNames.QcStationsAdmin)]
    public async Task<IActionResult> DeactivateQcStation(int id, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.SetActiveAsync(id, false, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station deactivated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/Reactivate")]
    [Authorize(Policy = AccessPolicyNames.QcStationsAdmin)]
    public async Task<IActionResult> ReactivateQcStation(int id, CancellationToken cancellationToken)
    {
        var error = await qcStationAdminService.SetActiveAsync(id, true, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "QC Station reactivated.";
        return RedirectToAction(nameof(QcStations));
    }

    [HttpPost("QcStations/RotateKey")]
    [Authorize(Policy = AccessPolicyNames.QcStationsAdmin)]
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
    [Authorize(Policy = AccessPolicyNames.QcStationsAdmin)]
    public IActionResult DownloadExistingQcStationConfig()
    {
        TempData["Error"] = "Rotate key to generate a new downloadable station config. Raw station keys are not stored after creation or rotation.";
        return RedirectToAction(nameof(QcStations));
    }

    private FileContentResult DownloadQcStationConfig(QcStationConfigDownload download) =>
        File(System.Text.Encoding.UTF8.GetBytes(download.Json), "application/json", download.FileName);

    [HttpPost("Users/Add")]
    [Authorize(Policy = AccessPolicyNames.UsersAdmin)]
    public async Task<IActionResult> AddUser(AddUserForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.AddUserAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User added.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Update")]
    [Authorize(Policy = AccessPolicyNames.UsersAdmin)]
    public async Task<IActionResult> UpdateUser(UpdateUserAccessForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.UpdateUserAccessAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User access updated.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Employment")]
    [Authorize(Policy = AccessPolicyNames.UsersAdmin)]
    public async Task<IActionResult> UpdateUserEmployment(UpdateUserEmploymentForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.UpdateUserEmploymentAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "User employment updated.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Roles/Create")]
    [Authorize(Policy = AccessPolicyNames.PermissionMatrixAdmin)]
    public async Task<IActionResult> CreateRole(CreateRoleForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.CreateRoleAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Role created.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Roles/Update")]
    [Authorize(Policy = AccessPolicyNames.PermissionMatrixAdmin)]
    public async Task<IActionResult> UpdateRole(UpdateRoleForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.UpdateRoleAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Role updated.";
        return RedirectToAction(nameof(Users), new { roleId = form.RoleId });
    }

    [HttpPost("Users/Roles/Matrix")]
    [Authorize(Policy = AccessPolicyNames.PermissionMatrixAdmin)]
    public async Task<IActionResult> UpdateRoleMatrix(RoleAccessMatrixForm form, CancellationToken cancellationToken)
    {
        var error = await userAdminService.UpdateRoleMatrixAsync(form, authorizationService.GetEmail(User) ?? "", cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Role permission matrix updated.";
        return RedirectToAction(nameof(Users), new { roleId = form.RoleId });
    }

}
