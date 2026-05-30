using System.Reflection;
using CropQc.Web.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace CropQc.Api.Tests;

public sealed class RoleNavigationAuthorizationTests
{
    [Fact]
    public void Layout_ShowsManagementLinksByRole()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("href=\"/\"", layout);
        Assert.Contains("href=\"/DailyQc\"", layout);
        Assert.Contains("href=\"/Receipts\"", layout);
        Assert.Contains("AdminAuthorization.IsManagerOrAdmin(User)", layout);
        Assert.Contains("AdminAuthorization.IsAdmin(User)", layout);
        Assert.Contains("href=\"/MasterData\"", layout);
        Assert.Contains("href=\"/Admin/QcStations\"", layout);
        Assert.Contains("href=\"/Admin/Users\"", layout);
        Assert.Contains("href=\"/Admin/Downloads\"", layout);
        Assert.Contains("href=\"/Admin/Configuration\"", layout);
    }

    [Fact]
    public void AdminController_AuthorizationMatchesRoleNavigation()
    {
        AssertControllerPolicy<AdminController>("RequireAuthenticatedUser");
        AssertActionPolicy<AdminController>(nameof(AdminController.Users), "RequireAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.Downloads), "RequireAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.QcStations), "RequireManagerOrAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.CreateQcStation), "RequireManagerOrAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.RotateQcStationKey), "RequireManagerOrAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.DownloadExistingQcStationConfig), "RequireManagerOrAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.AddUser), "RequireAdmin");
        AssertActionPolicy<AdminController>(nameof(AdminController.UpdateUser), "RequireAdmin");
    }

    [Fact]
    public void MasterData_AllowsManagerOrAdmin()
    {
        AssertControllerPolicy<MasterDataController>("RequireManagerOrAdmin");
        AssertNoActionPolicy<MasterDataController>(nameof(MasterDataController.Edit), "RequireAdmin");
        AssertNoActionPolicy<MasterDataController>(nameof(MasterDataController.Save), "RequireAdmin");
        AssertNoActionPolicy<MasterDataController>(nameof(MasterDataController.Deactivate), "RequireAdmin");
    }

    [Fact]
    public void Configuration_RemainsAdminOnly()
    {
        AssertControllerPolicy<ConfigurationController>("RequireAdmin");
    }

    [Fact]
    public void RolePermissionMatrix_MatchesNavigationAccess()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAdminService.cs"));

        Assert.Contains("new(\"View dashboard\", \"Yes\", \"Yes\", \"Yes\", \"Yes\")", service);
        Assert.Contains("new(\"View Daily QC\", \"Yes\", \"Yes\", \"Yes\", \"Yes\")", service);
        Assert.Contains("new(\"View receipts/samples\", \"Yes\", \"Yes\", \"Yes\", \"Yes\")", service);
        Assert.Contains("new(\"View Master Data\", \"Yes\", \"Yes\", \"No\", \"No\")", service);
        Assert.Contains("new(\"Manage QC Stations\", \"Yes\", \"Yes\", \"No\", \"No\")", service);
        Assert.Contains("new(\"Manage users/roles\", \"Yes\", \"No\", \"No\", \"No\")", service);
        Assert.Contains("new(\"Open Admin Downloads\", \"Yes\", \"No\", \"No\", \"No\")", service);
        Assert.Contains("new(\"Edit configuration\", \"Yes\", \"No\", \"No\", \"No\")", service);
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
