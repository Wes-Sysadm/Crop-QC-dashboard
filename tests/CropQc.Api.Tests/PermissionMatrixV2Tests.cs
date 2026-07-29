using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Data.Common;

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

    [Fact]
    public async Task RepeatedPermissionChecksLoadOneAccessSnapshotPerRequestScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = User("multi-area@example.com");
        user.PageAccesses.Add(Access(ApplicationAreas.Dashboard, "View"));
        user.PageAccesses.Add(Access(ApplicationAreas.FieldSamples, "Create"));
        user.PageAccesses.Add(Access(ApplicationAreas.ProjectionPlanner, "Admin"));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        counter.Reset();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.View, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.Dashboard, default));
        Assert.Equal(PageAccessLevel.Create, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.FieldSamples, default));
        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default));

        Assert.Equal(1, counter.ReaderCount);
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

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return ValueTask.FromResult(result);
        }

        public void Reset() => ReaderCount = 0;
    }
}
