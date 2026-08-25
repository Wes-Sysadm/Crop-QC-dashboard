using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class FacilityNavigationTests
{
    [Fact]
    public async Task FacilityContext_UsesStableWarehouseRecordsForWpAndEbs()
    {
        await using var db = CreateDb();
        db.Warehouses.AddRange(
            new Warehouse { Code = "WP", Name = "Windy Point", IsActive = true },
            new Warehouse { Code = "EBS", Name = "Earl Brown Storage", IsActive = true });
        await db.SaveChangesAsync();
        var service = new FacilityContextService(db);

        var wp = await service.GetWarehouseIdsAsync("WP", CancellationToken.None);
        var ebs = await service.GetWarehouseIdsAsync("EBS", CancellationToken.None);

        Assert.Single(wp);
        Assert.Single(ebs);
        Assert.Empty(wp.Intersect(ebs));
        Assert.Equal("All", service.Normalize("unexpected"));
    }

    [Fact]
    public void Dashboard_FiltersOperationalRollupsAndPreservesFacilityInLinks()
    {
        var service = Read("src", "CropQc.Web", "Services", "DashboardDataService.cs");
        var dashboard = Read("src", "CropQc.Web", "Views", "Home", "Index.cshtml");
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains("FacilityContext.Matches(x.Warehouse", service);
        Assert.Contains("FacilityContext.Matches(x.Facility", service);
        Assert.Contains("FacilityContext.GetWarehouseIdsAsync(search.Facility", service);
        Assert.Contains("Facility={encodedFacility}", service);
        Assert.Contains("?Facility=@room.Facility", dashboard);
        Assert.Contains("facility-context-bar", layout);
        Assert.Contains("IFacilityContextService FacilityContext", layout);
    }

    [Fact]
    public void MainNavigation_UsesRouteGenerationAndNeverEmitsTheRazorVariableName()
    {
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");
        var navigation = Read("src", "CropQc.Web", "Services", "SiteNavigationService.cs");

        Assert.DoesNotContain("facilityQuery", layout);
        Assert.DoesNotContain("/BinsRun@", layout);
        Assert.Contains("asp-controller=\"Home\" asp-action=\"Index\" asp-route-facility=\"@facilityRouteValue\"", layout);
        Assert.Contains("\"run-planner\"", navigation);
        Assert.Contains("\"receipt-qc\"", navigation);
        Assert.Contains("\"receipts\"", navigation);
        Assert.Contains("\"room-overview\"", navigation);
        Assert.Contains("\"grower-lots\"", navigation);
        Assert.Contains("\"current-room-inventory\"", navigation);
        Assert.Contains("siteNavigation.Categories", layout);
        Assert.Contains("string.IsNullOrWhiteSpace(requestedFacility) ? null : activeFacility", layout);
    }

    [Fact]
    public void FacilitySelector_ReplacesExistingFacilityAndPreservesOtherEncodedFilters()
    {
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains("!x.Key.Equals(\"facility\", StringComparison.OrdinalIgnoreCase)", layout);
        Assert.Contains("QueryHelpers.AddQueryString", layout);
        Assert.Contains("new KeyValuePair<string, string?>(\"facility\", facility)", layout);
        Assert.DoesNotContain("href=\"@route?Facility=", layout);
    }

    [Fact]
    public void LiteralMalformedBinsRunPath_HasANarrowCompatibilityRedirect()
    {
        var controller = Read("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        Assert.Contains("[HttpGet(\"/BinsRun@facilityQuery\")]", controller);
        Assert.Contains("RedirectMalformedFacilityLink", controller);
        Assert.Contains("RedirectToAction(nameof(Index))", controller);
        Assert.DoesNotContain("catch-all", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiteralMalformedBinsRunPath_RedirectsToTheNormalIndexAction()
    {
        var controller = new BinsRunController(
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<BinsRunController>.Instance);

        var redirect = Assert.IsType<RedirectToActionResult>(controller.RedirectMalformedFacilityLink());

        Assert.Equal(nameof(BinsRunController.Index), redirect.ActionName);
        Assert.Null(redirect.ControllerName);
        Assert.False(redirect.Permanent);
    }

    [Fact]
    public void NavigationCleanup_MovesMasterDataAndRecipientDiscoveryToTheCentralCatalog()
    {
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");
        var masterData = Read("src", "CropQc.Web", "Views", "MasterData", "Index.cshtml");
        var growerLots = Read("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml");
        var navigation = Read("src", "CropQc.Web", "Services", "SiteNavigationService.cs");

        Assert.Contains("\"variety-colors\"", navigation);
        Assert.Contains("QC Recipients", navigation);
        Assert.Contains("Manager Import", navigation);
        Assert.DoesNotContain("EBS Historical Cleanup", layout);
        Assert.Contains("Fruit Profiles", masterData);
        Assert.Contains("Variety Codes", masterData);
        Assert.DoesNotContain("_MasterDataNavigation", growerLots);
        Assert.Contains("/Admin/OrchardRecipients", navigation);
        Assert.Contains("/Admin/OrchardRecipientImports", navigation);
        Assert.Contains("Unmatched Identities", navigation);
    }

    private static CropQcDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase($"facility-navigation-{Guid.NewGuid()}")
            .Options;
        return new CropQcDbContext(options);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
