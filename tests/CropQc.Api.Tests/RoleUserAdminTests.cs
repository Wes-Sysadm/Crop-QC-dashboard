using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class RoleUserAdminTests
{
    [Fact]
    public async Task FreshDatabaseContainsTheFiveBuiltInsWithCompleteConservativeMatrices()
    {
        await using var db = CreateDb();

        var roles = await db.Roles.Include(x => x.PageAccesses).Where(x => x.IsSystemRole).OrderBy(x => x.Id).ToListAsync();

        Assert.Equal(BuiltInRoleNames.All.Order(), roles.Select(x => x.Name).Order());
        Assert.All(roles, x => Assert.Equal(ApplicationAreas.All.Count, x.PageAccesses.Count));
        Assert.All(roles.Single(x => x.Name == BuiltInRoleNames.Admin).PageAccesses,
            x => Assert.Equal(nameof(PageAccessLevel.Admin), x.AccessLevel));
        Assert.Equal(nameof(PageAccessLevel.None), roles.Single(x => x.Name == BuiltInRoleNames.Manager)
            .PageAccesses.Single(x => x.AreaKey == ApplicationAreas.Users).AccessLevel);
        Assert.Equal(nameof(PageAccessLevel.Admin), roles.Single(x => x.Name == BuiltInRoleNames.QcAdmin)
            .PageAccesses.Single(x => x.AreaKey == ApplicationAreas.QcStations).AccessLevel);
        Assert.DoesNotContain(roles, x => string.Equals(x.Name, "QC User", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CustomRoleCopiesACompleteMatrixAndAuditsCreation()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var viewer = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer);
        await db.SaveChangesAsync();
        var invalidation = new TrackingAccessService();
        var service = Service(db, invalidation);

        var error = await service.CreateRoleAsync(new CreateRoleForm
        {
            Name = "Receiving Observer",
            Description = "Read-only receiving review.",
            CopyFromRoleId = viewer.Id
        }, administrator.Email, default);

        Assert.Null(error);
        var role = await db.Roles.Include(x => x.PageAccesses).SingleAsync(x => x.NormalizedName == "RECEIVING OBSERVER");
        Assert.False(role.IsSystemRole);
        Assert.True(role.IsActive);
        Assert.Equal(ApplicationAreas.All.Count, role.PageAccesses.Count);
        Assert.Equal("View", role.PageAccesses.Single(x => x.AreaKey == ApplicationAreas.Receipts).AccessLevel);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.EntityName == "roles" && x.Action == "create");
        Assert.Equal(1, invalidation.Count);
    }

    [Fact]
    public async Task MatrixUpdateRequiresEveryKnownAreaAndAuditsEachChangedCell()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var role = NewRole("Dispatch");
        foreach (var area in ApplicationAreas.All) role.PageAccesses.Add(Cell(area.Key));
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var invalidation = new TrackingAccessService();
        var service = Service(db, invalidation);
        var access = ApplicationAreas.All.ToDictionary(x => x.Key, _ => "None", StringComparer.OrdinalIgnoreCase);
        access[ApplicationAreas.Transfers] = "Create";

        Assert.Null(await service.UpdateRoleMatrixAsync(new RoleAccessMatrixForm { RoleId = role.Id, Access = access }, administrator.Email, default));
        Assert.Equal("Create", (await db.RolePageAccesses.SingleAsync(x => x.RoleId == role.Id && x.AreaKey == ApplicationAreas.Transfers)).AccessLevel);
        var audit = Assert.Single(await db.AuditLogs.Where(x => x.EntityName == "role-page-access").ToListAsync());
        Assert.Contains(ApplicationAreas.Transfers, audit.EntityKey);
        Assert.Equal(1, invalidation.Count);

        access.Remove(ApplicationAreas.Rooms);
        Assert.Contains("every application area", await service.UpdateRoleMatrixAsync(new RoleAccessMatrixForm { RoleId = role.Id, Access = access }, administrator.Email, default));
    }

    [Fact]
    public async Task AdminMatrixAndBuiltInIdentityAreImmutable()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());
        var adminRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);

        var matrixError = await service.UpdateRoleMatrixAsync(new RoleAccessMatrixForm
        {
            RoleId = adminRole.Id,
            Access = ApplicationAreas.All.ToDictionary(x => x.Key, _ => "None")
        }, administrator.Email, default);
        var renameError = await service.UpdateRoleAsync(new UpdateRoleForm
        {
            RoleId = adminRole.Id,
            Name = "Super Admin",
            IsActive = true
        }, administrator.Email, default);

        Assert.Contains("always has full access", matrixError);
        Assert.Contains("cannot be renamed", renameError);
    }

    [Fact]
    public async Task LastActiveAdminCannotBeReassignedOrDeactivated()
    {
        await using var db = CreateDb();
        var adminRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
        var viewerRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer);
        var administrator = AddAdministrator(db);
        administrator.UserRoles.Add(new UserRole { Role = adminRole });
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());

        var error = await service.UpdateUserAccessAsync(new UpdateUserAccessForm
        {
            UserId = administrator.Id,
            RoleId = viewerRole.Id,
            IsActive = true
        }, administrator.Email, default);

        Assert.Contains("last active Admin", error);
        Assert.Equal(adminRole.Id, (await db.UserRoles.SingleAsync(x => x.UserId == administrator.Id)).RoleId);
        Assert.True(administrator.IsActive);
    }

    [Fact]
    public async Task ConflictingLegacyAssignmentsAreDisplayedAndCannotBeEditedImplicitly()
    {
        await using var db = CreateDb();
        var adminRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
        var viewerRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer);
        var administrator = AddAdministrator(db);
        var conflicted = User("conflict@fruitandland.com");
        conflicted.UserRoles.Add(new UserRole { Role = adminRole });
        conflicted.UserRoles.Add(new UserRole { Role = viewerRole });
        db.Users.Add(conflicted);
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());

        var page = await service.GetUsersAsync(null, default);
        Assert.Equal("Role conflict", page.Users.Single(x => x.Id == conflicted.Id).Role);
        var error = await service.UpdateUserAccessAsync(new UpdateUserAccessForm
        {
            UserId = conflicted.Id,
            RoleId = viewerRole.Id,
            IsActive = true
        }, administrator.Email, default);
        Assert.Contains("exactly one current role", error);
    }

    [Fact]
    public async Task CustomRoleNameIsCaseInsensitiveAndOnlyUnusedRoleCanDeactivate()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());

        Assert.Null(await service.CreateRoleAsync(new CreateRoleForm { Name = "Shipping Review" }, administrator.Email, default));
        Assert.Contains("already exists", await service.CreateRoleAsync(new CreateRoleForm { Name = " shipping review " }, administrator.Email, default));
        var role = await db.Roles.SingleAsync(x => x.NormalizedName == "SHIPPING REVIEW");

        Assert.Null(await service.UpdateRoleAsync(new UpdateRoleForm
        {
            RoleId = role.Id,
            Name = role.Name,
            IsActive = false
        }, administrator.Email, default));
        role.IsActive = true;
        var assigned = User("shipping@fruitandland.com");
        assigned.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(assigned);
        await db.SaveChangesAsync();

        var error = await service.UpdateRoleAsync(new UpdateRoleForm
        {
            RoleId = role.Id,
            Name = role.Name,
            IsActive = false
        }, administrator.Email, default);
        Assert.Contains("assigned to active users", error);
        Assert.True(role.IsActive);
    }

    [Fact]
    public async Task AddUserRequiresAnActiveRoleAndCreatesExactlyOneAssignment()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());

        Assert.Contains("active role", await service.AddUserAsync(new AddUserForm
        {
            Email = "new.user@fruitandland.com",
            DisplayName = "New User",
            RoleId = int.MaxValue,
            IsActive = true
        }, administrator.Email, default));
        Assert.False(await db.Users.AnyAsync(x => x.Email == "new.user@fruitandland.com"));

        var viewer = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer);
        Assert.Null(await service.AddUserAsync(new AddUserForm
        {
            Email = "new.user@fruitandland.com",
            DisplayName = "New User",
            RoleId = viewer.Id,
            IsActive = true
        }, administrator.Email, default));
        var added = await db.Users.Include(x => x.UserRoles).SingleAsync(x => x.Email == "new.user@fruitandland.com");
        Assert.Single(added.UserRoles);
        Assert.Equal(viewer.Id, added.UserRoles.Single().RoleId);
    }

    [Fact]
    public async Task MatrixChangeImmediatelyUpdatesEveryAssignedUserAndPreservesPersonalSettings()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var role = NewRole("Receiving team");
        foreach (var area in ApplicationAreas.All) role.PageAccesses.Add(Cell(area.Key));
        var first = User("first@fruitandland.com");
        first.EmploymentFacility = EmploymentFacilities.Wp;
        first.GoogleCredentials.Add(new UserGoogleCredential
        {
            Provider = "Google",
            Scope = "openid",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        first.UserRoles.Add(new UserRole { Role = role });
        var second = User("second@fruitandland.com");
        second.UserRoles.Add(new UserRole { Role = role });
        db.AddRange(role, first, second);
        await db.SaveChangesAsync();
        var access = new UserAccessService(db, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var service = Service(db, access);

        Assert.Equal(PageAccessLevel.None, await access.GetAccessLevelAsync(first.Email, ApplicationAreas.Receipts, default));
        Assert.Equal(PageAccessLevel.None, await access.GetAccessLevelAsync(second.Email, ApplicationAreas.Receipts, default));
        var matrix = ApplicationAreas.All.ToDictionary(x => x.Key, _ => nameof(PageAccessLevel.None), StringComparer.OrdinalIgnoreCase);
        matrix[ApplicationAreas.Receipts] = nameof(PageAccessLevel.Create);
        Assert.Null(await service.UpdateRoleMatrixAsync(new RoleAccessMatrixForm { RoleId = role.Id, Access = matrix }, administrator.Email, default));

        Assert.Equal(PageAccessLevel.Create, await access.GetAccessLevelAsync(first.Email, ApplicationAreas.Receipts, default));
        Assert.Equal(PageAccessLevel.Create, await access.GetAccessLevelAsync(second.Email, ApplicationAreas.Receipts, default));
        Assert.Equal(EmploymentFacilities.Wp, (await db.Users.SingleAsync(x => x.Id == first.Id)).EmploymentFacility);
        Assert.Single(await db.UserGoogleCredentials.Where(x => x.UserId == first.Id).ToListAsync());
    }

    private static CropQcDbContext CreateDb()
    {
        var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static UserAdminService Service(CropQcDbContext db, IUserAccessService access) =>
        new(db, new GoogleAuthenticationOptions
        {
            AllowedDomains = new HashSet<string>(["fruitandland.com"], StringComparer.OrdinalIgnoreCase)
        }, access);

    private static User AddAdministrator(CropQcDbContext db)
    {
        var user = User("admin@fruitandland.com");
        db.Users.Add(user);
        return user;
    }

    private static User User(string email) => new()
    {
        Email = email,
        DisplayName = email,
        Domain = "fruitandland.com",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Role NewRole(string name) => new()
    {
        Name = name,
        NormalizedName = BuiltInRoleNames.Normalize(name),
        IsActive = true
    };

    private static RolePageAccess Cell(string area) => new()
    {
        AreaKey = area,
        AccessLevel = "None",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class TrackingAccessService : IUserAccessService
    {
        public int Count { get; private set; }
        public Task<bool> HasAccessAsync(System.Security.Claims.ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(PageAccessLevel.Admin);
        public void InvalidateAll() => Count++;
    }
}
