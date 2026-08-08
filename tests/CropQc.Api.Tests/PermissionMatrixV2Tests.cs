using System.Data.Common;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class PermissionMatrixV2Tests
{
    [Fact]
    public async Task AccessComesFromTheUsersSingleActiveRole()
    {
        await using var db = CreateDb();
        var role = Role("Planner", (ApplicationAreas.ProjectionPlanner, PageAccessLevel.Create));
        var user = User("planner@example.com", role);
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.Create, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default));
        Assert.True(await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default) >= PageAccessLevel.View);
        Assert.False(await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default) >= PageAccessLevel.Admin);
    }

    [Fact]
    public async Task UsersWithTheSameRoleReceiveTheSameAccess()
    {
        await using var db = CreateDb();
        var role = Role("Line team", (ApplicationAreas.Receipts, PageAccessLevel.Create));
        var first = User("first@example.com", role);
        var second = User("second@example.com", role);
        db.AddRange(role, first, second);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(
            await service.GetAccessLevelAsync(first.Email, ApplicationAreas.Receipts, default),
            await service.GetAccessLevelAsync(second.Email, ApplicationAreas.Receipts, default));
    }

    [Fact]
    public async Task LegacyPerUserRowsDoNotGrantOrOverrideAccess()
    {
        await using var db = CreateDb();
        var role = Role("Viewer custom", (ApplicationAreas.Receipts, PageAccessLevel.View));
        var user = User("legacy@example.com", role);
        user.PageAccesses.Add(new UserPageAccess
        {
            AreaKey = ApplicationAreas.Receipts,
            AccessLevel = nameof(PageAccessLevel.Admin),
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.AddRange(role, user);
        await db.SaveChangesAsync();

        Assert.Equal(PageAccessLevel.View, await Service(db).GetAccessLevelAsync(user.Email, ApplicationAreas.Receipts, default));
    }

    [Fact]
    public async Task MasterDataAdminDoesNotElevateUnrelatedOrFutureAreas()
    {
        await using var db = CreateDb();
        var role = Role("Master data editor", (ApplicationAreas.MasterData, PageAccessLevel.Admin));
        var user = User("master@example.com", role);
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.None, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.PermissionMatrix, default));
        Assert.Equal(PageAccessLevel.None, await service.GetAccessLevelAsync(user.Email, "future-admin-section", default));
    }

    [Fact]
    public async Task DataCleanupAndCropYearReviewFollowExplicitRoleCells()
    {
        await using var db = CreateDb();
        var role = Role("Cleanup reviewer",
            (ApplicationAreas.DataCleanup, PageAccessLevel.Admin),
            (ApplicationAreas.CropYearReview, PageAccessLevel.View));
        var user = User("not-owner@example.com", role);
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.DataCleanup, default));
        Assert.Equal(PageAccessLevel.View, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.CropYearReview, default));
    }

    [Fact]
    public async Task MissingMultipleOrInactiveRoleAssignmentsFailClosed()
    {
        await using var db = CreateDb();
        var active = Role("Active", (ApplicationAreas.Dashboard, PageAccessLevel.View));
        var inactive = Role("Inactive", (ApplicationAreas.Dashboard, PageAccessLevel.Admin));
        inactive.IsActive = false;
        var missing = BareUser("missing@example.com");
        var multiple = User("multiple@example.com", active);
        multiple.UserRoles.Add(new UserRole { Role = inactive });
        var disabledRole = User("inactive@example.com", inactive);
        db.AddRange(active, inactive, missing, multiple, disabledRole);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.None, await service.GetAccessLevelAsync(missing.Email, ApplicationAreas.Dashboard, default));
        Assert.Equal(PageAccessLevel.None, await service.GetAccessLevelAsync(multiple.Email, ApplicationAreas.Dashboard, default));
        Assert.Equal(PageAccessLevel.None, await service.GetAccessLevelAsync(disabledRole.Email, ApplicationAreas.Dashboard, default));
    }

    [Fact]
    public async Task AdminRoleAndOwnerBreakGlassHaveFullAccess()
    {
        await using var db = CreateDb();
        var adminRole = Role(BuiltInRoleNames.Admin);
        var admin = User("admin@example.com", adminRole);
        db.AddRange(adminRole, admin);
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(admin.Email, ApplicationAreas.DataCleanup, default));
        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(ApplicationAreas.OwnerEmail, "unregistered-break-glass-area", default));
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
    public async Task RepeatedPermissionChecksLoadOneRoleSnapshotPerRequestScope()
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
        var role = Role("Multi area",
            (ApplicationAreas.Dashboard, PageAccessLevel.View),
            (ApplicationAreas.FieldSamples, PageAccessLevel.Create),
            (ApplicationAreas.ProjectionPlanner, PageAccessLevel.Admin));
        var user = User("multi-area@example.com", role);
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        counter.Reset();
        var service = Service(db);

        Assert.Equal(PageAccessLevel.View, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.Dashboard, default));
        Assert.Equal(PageAccessLevel.Create, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.FieldSamples, default));
        Assert.Equal(PageAccessLevel.Admin, await service.GetAccessLevelAsync(user.Email, ApplicationAreas.ProjectionPlanner, default));
        Assert.Equal(1, counter.ReaderCount);
    }

    [Fact]
    public async Task RelationalDatabasePreventsMoreThanOneRolePerUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var firstRole = Role("First role");
        var secondRole = Role("Second role");
        var user = User("single-role@example.com", firstRole);
        db.AddRange(firstRole, secondRole, user);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole { UserId = user.Id, Role = secondRole });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static CropQcDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserAccessService Service(CropQcDbContext db) =>
        new(db, new ConfigurationBuilder().Build());

    private static User BareUser(string email) => new()
    {
        Email = email,
        DisplayName = email,
        Domain = "example.com",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static User User(string email, Role role)
    {
        var user = BareUser(email);
        user.UserRoles.Add(new UserRole { Role = role });
        return user;
    }

    private static Role Role(string name, params (string Area, PageAccessLevel Level)[] access)
    {
        var role = new Role
        {
            Name = name,
            NormalizedName = BuiltInRoleNames.Normalize(name),
            IsActive = true
        };
        foreach (var (area, level) in access)
        {
            role.PageAccesses.Add(new RolePageAccess
            {
                AreaKey = area,
                AccessLevel = UserAccessService.PersistedLevel(level),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        return role;
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
