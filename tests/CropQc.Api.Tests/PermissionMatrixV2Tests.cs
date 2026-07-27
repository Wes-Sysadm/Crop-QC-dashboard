using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class PermissionMatrixV2Tests
{
    [Fact]
    public async Task CreateIncludesViewButDoesNotGrantAdminActions()
    {
        await using var db = CreateDb();
        var user = User("planner@example.com");
        user.PageAccesses.Add(Access(ApplicationAreas.ProjectionPlanner, "Create"));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.Create, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default));
        Assert.True(await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default) >= PageAccessLevel.View);
        Assert.False(await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default) >= PageAccessLevel.Admin);
    }

    [Fact]
    public async Task MasterDataAdminActsAsSiteAdminForCurrentAndFutureAreas()
    {
        await using var db = CreateDb();
        var user = User("master-admin@example.com");
        user.PageAccesses.Add(Access(ApplicationAreas.MasterData, "Admin"));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.PermissionMatrix, default));
        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(user.Email, "future-admin-section", default));
    }

    [Fact]
    public void PublicMatrixHasExactlyViewCreateAdminLevels()
    {
        var selectable = Enum.GetNames<PageAccessLevel>()
            .Where(x => x != nameof(PageAccessLevel.None) && x != nameof(PageAccessLevel.Edit))
            .ToList();

        Assert.Equal(["View", "Create", "Admin"], selectable);
        Assert.Equal(PageAccessLevel.Create, UserAccessService.ParseLevel("Edit"));
    }

    [Fact]
    public void MigrationCopiesLegacyLevelsWithoutPromotingCreateToAdmin()
    {
        var migration = Directory.GetFiles(RepositoryRoot(), "*AddGrowerLotProjectionSnapshotsAndPermissionLevels.cs", SearchOption.AllDirectories)
            .Single(x => !x.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var text = File.ReadAllText(migration);

        Assert.Contains("source.[AccessLevel]", text);
        Assert.Contains("source.\"AccessLevel\"", text);
        Assert.Contains("SET [AccessLevel] = 'Create'", text);
        Assert.Contains("legacy.[AreaKey] = 'receipt-delete'", text);
        Assert.Contains("receipts.[AreaKey] = 'receipts'", text);
        Assert.DoesNotContain("SET source.[AccessLevel] = 'Admin'", text);
        Assert.DoesNotContain("DropTable", text);
        Assert.DoesNotContain("DeleteData", text);
    }

    private static CropQcDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserAccessService Service(CropQcDbContext db) =>
        new(db, new ConfigurationBuilder().Build());

    private static User User(string email) =>
        new() { Email = email, DisplayName = email, Domain = "example.com", CreatedAt = DateTimeOffset.UtcNow };

    private static UserPageAccess Access(string area, string level) =>
        new() { AreaKey = area, AccessLevel = level, UpdatedAt = DateTimeOffset.UtcNow };

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CropQc.sln")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
