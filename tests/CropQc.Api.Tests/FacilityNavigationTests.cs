using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

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
    public void NavigationCleanup_KeepsMasterDataAndMovesRecipientDiscoveryToGrowerLots()
    {
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");
        var masterData = Read("src", "CropQc.Web", "Views", "MasterData", "Index.cshtml");
        var growerLots = Read("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml");

        Assert.DoesNotContain(">Variety Colors</a>", layout);
        Assert.DoesNotContain(">Orchard QC Recipients</a>", layout);
        Assert.DoesNotContain(">Orchard Manager Import</a>", layout);
        Assert.Contains("Fruit Profiles", masterData);
        Assert.Contains("Variety Codes", masterData);
        Assert.Contains("/Admin/OrchardRecipients", growerLots);
        Assert.Contains("/Admin/OrchardRecipientImports", growerLots);
        Assert.Contains("Unmatched Identities", growerLots);
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
