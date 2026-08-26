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
        var navigation = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "SiteNavigationService.cs"));

        Assert.Contains("asp-controller=\"Home\" asp-action=\"Index\"", layout);
        Assert.Contains("siteNavigation.Categories", layout);
        Assert.Contains("\"receiving\"", navigation);
        Assert.Contains("\"runs\"", navigation);
        Assert.Contains("\"transfers\"", navigation);
        Assert.Contains("\"rooms\"", navigation);
        Assert.Contains("\"growers-reports\"", navigation);
        Assert.Contains("Current Room Inventory", navigation);
        Assert.Contains("Inventory Reconciliation", navigation);
        Assert.DoesNotContain("facilityQuery", layout);
        Assert.Contains("ISiteNavigationService SiteNavigation", layout);
        Assert.Contains("userAccess.HasAccessAsync", navigation);
        Assert.Contains("site-nav-category", layout);
        Assert.Contains("category.Key == \"admin\"", layout);
        Assert.Contains("/MasterData", navigation);
        Assert.Contains("/Admin/QcStations", navigation);
        Assert.Contains("/Admin/Users", navigation);
        Assert.Contains("/Admin/Downloads", navigation);
        Assert.Contains("/Admin/Configuration", navigation);
        Assert.Contains("/Admin/VarietyColors", navigation);
        Assert.Contains("/Admin/Backups", navigation);
        Assert.Contains("/Admin/DataCleanup", navigation);
        Assert.Contains("Access & Devices", navigation);
        Assert.Contains("Data Maintenance", navigation);
        Assert.DoesNotContain("EBS Historical Cleanup", navigation);
        Assert.DoesNotContain("IAdminAuthorizationService", layout);
    }

    [Fact]
    public void NavigationDropdown_UsesClickKeyboardAndResponsiveInlineFallback()
    {
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));
        var script = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "site-navigation.js"));

        Assert.Contains("site-nav-panel", css);
        Assert.Contains("position: absolute", css);
        Assert.Contains("data-nav-category", layout);
        Assert.Contains("closeCategories(category)", script);
        Assert.Contains("event.key !== \"Escape\"", script);
        Assert.Contains("document.addEventListener(\"click\"", script);
        Assert.Contains("navigationInitialized", script);
        Assert.Contains("@media (max-width: 1180px)", css);
        Assert.Contains("position: static", css);
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
        var navigation = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "SiteNavigationService.cs"));

        Assert.Contains("[HttpGet(\"Growers\")]", controller);
        Assert.DoesNotContain("[HttpPost", controller);
        Assert.DoesNotContain("method=\"post\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/RunReporting/Growers?Facility=All", navigation);
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
