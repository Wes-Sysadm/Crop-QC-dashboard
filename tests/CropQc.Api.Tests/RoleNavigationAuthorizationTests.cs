using System.Reflection;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Tests;

public sealed class RoleNavigationAuthorizationTests
{
    [Fact]
    public void Layout_ShowsManagementLinksByRole()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("asp-controller=\"Home\" asp-action=\"Index\"", layout);
        Assert.Contains("asp-controller=\"Receipts\" asp-action=\"Index\"", layout);
        Assert.DoesNotContain("asp-controller=\"DailyQc\" asp-action=\"Index\"", layout);
        Assert.Contains(">Receiving</a>", layout);
        Assert.Contains(">Runs &amp; Transfers</a>", layout);
        Assert.Contains("<summary>Rooms</summary>", layout);
        Assert.Contains("<summary>Growers</summary>", layout);
        Assert.Contains("Current Room Inventory", layout);
        Assert.Contains("Inventory Reconciliation", layout);
        Assert.DoesNotContain("facilityQuery", layout);
        Assert.Contains("IUserAccessService UserAccess", layout);
        Assert.Contains("UserAccess.HasAccessAsync", layout);
        Assert.Contains("class=\"nav-dropdown\"", layout);
        Assert.Contains("<summary>Admin</summary>", layout);
        Assert.Contains("showAdminMenu", layout);
        Assert.Contains("href=\"/MasterData\"", layout);
        Assert.Contains("href=\"/Admin/QcStations\"", layout);
        Assert.Contains("href=\"/Admin/Users\"", layout);
        Assert.Contains("href=\"/Admin/Downloads\"", layout);
        Assert.Contains("href=\"/Admin/Configuration\"", layout);
        Assert.DoesNotContain(">Variety Colors</a>", layout);
        Assert.Contains("href=\"/Admin/Backups\"", layout);
        Assert.Contains("href=\"/Admin/DataCleanup\"", layout);
        Assert.Contains("canAccessDataCleanup", layout);
        Assert.Contains("Access &amp; Devices", layout);
        Assert.Contains("Data Maintenance", layout);
        Assert.DoesNotContain("EBS Historical Cleanup", layout);
        Assert.DoesNotContain("IAdminAuthorizationService", layout);
    }

    [Fact]
    public void AdminDropdown_OpensOnDesktopHoverAndKeepsTouchFallback()
    {
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("@media (hover: hover) and (pointer: fine)", css);
        Assert.Contains(".nav-dropdown:hover .nav-dropdown-menu", css);
        Assert.Contains(".nav-dropdown:focus-within .nav-dropdown-menu", css);
        Assert.Contains(".nav-dropdown:not([open]) .nav-dropdown-menu", css);
        Assert.Contains(".nav-dropdown-menu { position: absolute", css);
        Assert.Contains("mouseenter", layout);
        Assert.Contains("mouseleave", layout);
        Assert.Contains("setTimeout(() => { dropdown.open = false; }, 220)", layout);
        Assert.Contains("@media (max-width: 760px)", css);
        Assert.Contains(".nav-dropdown { position: static", css);
    }

    [Fact]
    public void AdminController_AuthorizationMatchesRoleNavigation()
    {
        AssertControllerPolicy<AdminController>("RequireAuthenticatedUser");
        AssertActionPolicy<AdminController>(nameof(AdminController.Users), AccessPolicyNames.UsersAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.Downloads), AccessPolicyNames.DownloadsView);
        AssertActionPolicy<AdminController>(nameof(AdminController.VarietyColors), AccessPolicyNames.VarietyColorsView);
        AssertActionPolicy<AdminController>(nameof(AdminController.SaveVarietyColor), AccessPolicyNames.VarietyColorsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.ResetVarietyColor), AccessPolicyNames.VarietyColorsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.QcStations), AccessPolicyNames.QcStationsView);
        AssertActionPolicy<AdminController>(nameof(AdminController.CreateQcStation), AccessPolicyNames.QcStationsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.UpdateQcStation), AccessPolicyNames.QcStationsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.DeactivateQcStation), AccessPolicyNames.QcStationsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.ReactivateQcStation), AccessPolicyNames.QcStationsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.RotateQcStationKey), AccessPolicyNames.QcStationsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.DownloadExistingQcStationConfig), AccessPolicyNames.QcStationsAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.AddUser), AccessPolicyNames.UsersAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.UpdateUser), AccessPolicyNames.UsersAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.CreateRole), AccessPolicyNames.PermissionMatrixAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.UpdateRole), AccessPolicyNames.PermissionMatrixAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.DeleteRole), AccessPolicyNames.PermissionMatrixAdmin);
        Assert.NotNull(typeof(AdminController).GetMethod(nameof(AdminController.DeleteRole))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        AssertActionPolicy<AdminController>(nameof(AdminController.UpdateRoleMatrix), AccessPolicyNames.PermissionMatrixAdmin);
        AssertActionPolicy<RoomInventoryController>(nameof(RoomInventoryController.DismissDiagnostic), AccessPolicyNames.CurrentLotsAdmin);
        AssertActionPolicy<RoomInventoryController>(nameof(RoomInventoryController.RestoreDiagnostic), AccessPolicyNames.CurrentLotsAdmin);
        Assert.NotNull(typeof(RoomInventoryController).GetMethod(nameof(RoomInventoryController.DismissDiagnostic))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(typeof(RoomInventoryController).GetMethod(nameof(RoomInventoryController.RestoreDiagnostic))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void MasterData_UsesMatrixPolicies()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "MasterDataController.cs"));
        Assert.Contains("CanTypeAsync(type, PageAccessLevel.View", controller);
        Assert.Contains("CanTypeAsync(type, PageAccessLevel.Create", controller);
        Assert.Contains("CanTypeAsync(type, PageAccessLevel.Admin", controller);
        Assert.Contains("ApplicationAreas.ImportTools, PageAccessLevel.Admin", controller);
    }

    [Fact]
    public void Configuration_RemainsAdminOnly()
    {
        AssertControllerPolicy<ConfigurationController>(AccessPolicyNames.EmailConfigurationAdmin);
    }

    [Fact]
    public void GrowerLotProgress_IsReadOnlyAndRequiresBinsRunView()
    {
        AssertControllerPolicy<RunReportingController>(AccessPolicyNames.BinsRunView);
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "RunReportingController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RunReporting", "Growers.cshtml"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("[HttpGet(\"Growers\")]", controller);
        Assert.DoesNotContain("[HttpPost", controller);
        Assert.DoesNotContain("method=\"post\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/RunReporting/Growers?Facility=All", layout);
    }

    [Fact]
    public void RolePermissionMatrix_MatchesNavigationAccess()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));

        Assert.Contains("ApplicationAreas.All", service);
        Assert.Contains("ThenInclude(x => x.PageAccesses)", service);
        Assert.Contains("assignments.Count != 1", service);
        Assert.DoesNotContain("dbContext.UserPageAccesses", service);
        Assert.DoesNotContain("ApplicationAreas.MasterData, out", service);
        Assert.Contains("wes@fruitandland.com", service);
    }

    private static void AssertControllerPolicy<TController>(string policy)
    {
        var attributes = typeof(TController).GetCustomAttributes<AuthorizeAttribute>();
        Assert.Contains(attributes, x => x.Policy == policy);
    }

    private static void AssertActionPolicy<TController>(string actionName, string policy)
    {
        var attributes = GetAction(actionName, typeof(TController)).GetCustomAttributes<AuthorizeAttribute>();
        Assert.Contains(attributes, x => x.Policy == policy);
    }

    private static void AssertNoActionPolicy<TController>(string actionName, string policy)
    {
        var attributes = GetAction(actionName, typeof(TController)).GetCustomAttributes<AuthorizeAttribute>();
        Assert.DoesNotContain(attributes, x => x.Policy == policy);
    }

    private static MethodInfo GetAction(string actionName, Type controllerType) =>
        controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x => x.Name == actionName);

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
