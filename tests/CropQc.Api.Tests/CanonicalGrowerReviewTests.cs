using CropQc.Data;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class CanonicalGrowerReviewTests
{
    [Theory]
    [InlineData("Vantage Orchard")]
    [InlineData("vantage orchard")]
    [InlineData(" Vantage   Orchard ")]
    [InlineData("Vantage-Orchard")]
    [InlineData("Vantage Orchard Non Chilean")]
    [InlineData("Vantage Orchard non-chilean")]
    public void VantageAliases_NormalizeToOneCanonicalGrower(string value)
    {
        Assert.True(CanonicalGrowerService.TryGetKnownCanonicalAlias(value, out var alias));
        Assert.Equal("Vantage Orchard", alias.CanonicalName);
    }

    [Theory]
    [InlineData("Stayman")]
    [InlineData("stayman flats")]
    [InlineData(" Stayman   Flats ")]
    [InlineData("Stayman-Flats")]
    [InlineData("Stayman Flats Non Chilean")]
    public void StaymanAliases_NormalizeToOneCanonicalGrower(string value)
    {
        Assert.True(CanonicalGrowerService.TryGetKnownCanonicalAlias(value, out var alias));
        Assert.Equal("Stayman Flats", alias.CanonicalName);
    }

    [Fact]
    public void UnrelatedSimilarGrowers_RemainUnmapped()
    {
        Assert.False(CanonicalGrowerService.TryGetKnownCanonicalAlias("Stayman Hills", out _));
        Assert.False(CanonicalGrowerService.TryGetKnownCanonicalAlias("Vantage Ridge", out _));
    }

    [Fact]
    public async Task SeededMappings_CreateOneCanonicalRecordPerKnownGrower()
    {
        await using var db = CreateDbContext();
        var service = new CanonicalGrowerService(db);

        await service.EnsureSeedMappingsAsync(CancellationToken.None);

        var growers = await db.CanonicalGrowers.Include(x => x.Aliases).OrderBy(x => x.DisplayName).ToListAsync();
        var stayman = Assert.Single(growers, x => x.DisplayName == "Stayman Flats");
        var vantage = Assert.Single(growers, x => x.DisplayName == "Vantage Orchard");
        Assert.Contains(stayman.Aliases, x => x.AliasName == "Stayman");
        Assert.Contains(stayman.Aliases, x => x.AliasName == "Stayman Flats Non Chilean");
        Assert.Contains(vantage.Aliases, x => x.AliasName == "Vantage Orchard Non Chilean");
    }

    [Fact]
    public async Task Resolver_CombinesAliasesAndLeavesOriginalNamesAvailable()
    {
        await using var db = CreateDbContext();
        var service = new CanonicalGrowerService(db);
        var resolver = await service.LoadResolutionSetAsync(CancellationToken.None);

        var vantage = resolver.Resolve("Vantage Orchard Non Chilean", null);
        var stayman = resolver.Resolve("Stayman", null);
        var unmapped = resolver.Resolve("Stayman Hills", "991");

        Assert.Equal("Vantage Orchard", vantage.DisplayName);
        Assert.Equal("Stayman Flats", stayman.DisplayName);
        Assert.False(unmapped.IsMapped);
        Assert.Contains("mapping needed", unmapped.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CropYearReview_IsCanonicalGrowerCardFirstAndPreservesSourceDetail()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "CropYearReview.cshtml"));
        var masterData = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));

        Assert.Contains("CropYearReviewGrowerViewModel", model);
        Assert.Contains("CanonicalGrowerName", model);
        Assert.Contains("SourceGrowerNames", model);
        Assert.Contains("GrowerNumbers", model);
        Assert.Contains("growerResolver.Resolve(sample.Receipt.GrowerName, sample.Receipt.GrowerNumber)", service);
        Assert.Contains("DistinctBy(x => x.Id)", service);
        Assert.Contains("GrowerOptions", view);
        Assert.Contains("Source names", view);
        Assert.Contains("View supporting receipts, lots, and samples", view);
        Assert.Contains("\"canonical-growers\" => await CanonicalGrowersPage", masterData);
        Assert.Contains("SaveCanonicalGrower", masterData);
    }

    [Fact]
    public void Schema_AddsCanonicalGrowerMappingTablesWithoutChangingReceipts()
    {
        var entities = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "MasterDataModels.cs"));
        var db = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "CropQcDbContext.cs"));
        var receipt = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "QcModels.cs"));
        var migration = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Migrations", "20260711213000_AddCanonicalGrowers.cs"));

        Assert.Contains("public sealed class CanonicalGrower", entities);
        Assert.Contains("public sealed class CanonicalGrowerAlias", entities);
        Assert.Contains("public sealed class CanonicalGrowerNumber", entities);
        Assert.Contains("DbSet<CanonicalGrower>", db);
        Assert.Contains("Vantage Orchard Non Chilean", migration);
        Assert.Contains("Stayman Flats Non Chilean", migration);
        Assert.DoesNotContain("CanonicalGrowerId", receipt);
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
