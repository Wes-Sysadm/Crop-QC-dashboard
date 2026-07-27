using System.Reflection;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;

namespace CropQc.Api.Tests;

public sealed class RoleNavigationAuthorizationTests
{
    [Fact]
    public void Layout_ShowsManagementLinksByRole()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("asp-controller=\"Home\" asp-action=\"Index\"", layout);
        Assert.Contains("asp-controller=\"DailyQc\" asp-action=\"Index\"", layout);
        Assert.Contains("asp-controller=\"Receipts\" asp-action=\"Index\"", layout);
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
        AssertActionPolicy<AdminController>(nameof(AdminController.UpdateUserMatrix), AccessPolicyNames.PermissionMatrixAdmin);
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
    public void RolePermissionMatrix_MatchesNavigationAccess()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));

        Assert.Contains("ApplicationAreas.All", service);
        Assert.Contains("DefaultForRole", service);
        Assert.Contains("UserPageAccesses", service);
        Assert.Contains("user-page-access", service);
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
