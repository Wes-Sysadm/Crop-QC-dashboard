using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class RoleUserAdminTests
{
    [Fact]
    public async Task EmptyCustomRoleDeletionCascadesOwnedRowsAndPreservesCompleteAudit()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var role = NewRole("Temporary receiving review");
        foreach (var area in ApplicationAreas.All) role.PageAccesses.Add(Cell(area.Key));
        role.Permissions.Add(new RolePermission
        {
            PermissionKey = "legacy-test",
            Description = "Legacy role-owned permission"
        });
        var unrelated = NewRole("Unrelated role");
        foreach (var area in ApplicationAreas.All) unrelated.PageAccesses.Add(Cell(area.Key));
        db.AddRange(role, unrelated);
        await db.SaveChangesAsync();
        var roleId = role.Id;
        var unrelatedId = unrelated.Id;
        var invalidation = new TrackingAccessService();

        var result = await Service(db, invalidation)
            .DeleteRoleAsync(roleId, administrator.Email, default);

        Assert.True(result.Succeeded);
        Assert.Equal("Temporary receiving review", result.DeletedRoleName);
        Assert.Null(result.Error);
        Assert.False(await db.Roles.AnyAsync(x => x.Id == roleId));
        Assert.False(await db.RolePageAccesses.AnyAsync(x => x.RoleId == roleId));
        Assert.False(await db.RolePermissions.AnyAsync(x => x.RoleId == roleId));
        Assert.True(await db.Roles.AnyAsync(x => x.Id == unrelatedId));
        Assert.Equal(1, invalidation.Count);

        var audit = Assert.Single(await db.AuditLogs.Where(x =>
            x.Action == "delete" && x.EntityName == "roles" && x.EntityKey == roleId.ToString()).ToListAsync());
        Assert.Null(audit.AfterValuesJson);
        using var snapshot = JsonDocument.Parse(Assert.IsType<string>(audit.BeforeValuesJson));
        var root = snapshot.RootElement;
        Assert.Equal(roleId, root.GetProperty("RoleId").GetInt32());
        Assert.Equal("Temporary receiving review", root.GetProperty("Name").GetString());
        Assert.Equal(0, root.GetProperty("AssignedUserCount").GetInt32());
        Assert.Equal(ApplicationAreas.All.Count, root.GetProperty("PermissionMatrix").GetArrayLength());
        Assert.All(root.GetProperty("PermissionMatrix").EnumerateArray(), cell =>
            Assert.True(cell.GetProperty("IsPersisted").GetBoolean()));
        Assert.Single(root.GetProperty("RolePermissions").EnumerateArray());
    }

    [Fact]
    public async Task EmptyImportedMigrationRoleCanBeDeleted()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var role = NewRole("Imported Access A");
        role.Description = "Imported from the legacy per-user access matrix during the role-based authorization conversion.";
        foreach (var area in ApplicationAreas.All) role.PageAccesses.Add(Cell(area.Key));
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var result = await Service(db, new TrackingAccessService())
            .DeleteRoleAsync(role.Id, administrator.Email, default);

        Assert.True(result.Succeeded);
        Assert.False(await db.Roles.AnyAsync(x => x.Id == role.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnyAssignedUserPreventsCustomRoleDeletion(bool userIsActive)
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var role = NewRole("Assigned custom role");
        foreach (var area in ApplicationAreas.All) role.PageAccesses.Add(Cell(area.Key));
        var assigned = User("assigned@fruitandland.com");
        assigned.IsActive = userIsActive;
        assigned.UserRoles.Add(new UserRole { Role = role });
        db.AddRange(role, assigned);
        await db.SaveChangesAsync();
        var pageAccessCount = role.PageAccesses.Count;

        var result = await Service(db, new TrackingAccessService())
            .DeleteRoleAsync(role.Id, administrator.Email, default);

        Assert.False(result.Succeeded);
        Assert.Equal("Move all users off this role before deleting it.", result.Error);
        Assert.True(await db.Roles.AnyAsync(x => x.Id == role.Id));
        Assert.Equal(pageAccessCount, await db.RolePageAccesses.CountAsync(x => x.RoleId == role.Id));
        Assert.True(await db.UserRoles.AnyAsync(x => x.RoleId == role.Id && x.UserId == assigned.Id));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.Action == "delete" && x.EntityKey == role.Id.ToString()));
    }

    [Theory]
    [InlineData(BuiltInRoleNames.Viewer)]
    [InlineData(BuiltInRoleNames.QcTech)]
    [InlineData(BuiltInRoleNames.QcAdmin)]
    [InlineData(BuiltInRoleNames.Manager)]
    [InlineData(BuiltInRoleNames.Admin)]
    public async Task BuiltInRolesCannotBeDeleted(string roleName)
    {
        await using var db = CreateDb();
        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        var administrator = AddAdministrator(db);
        await db.SaveChangesAsync();

        var result = await Service(db, new TrackingAccessService())
            .DeleteRoleAsync(role.Id, administrator.Email, default);

        Assert.False(result.Succeeded);
        Assert.Equal("Built-in roles cannot be deleted.", result.Error);
        Assert.True(await db.Roles.AnyAsync(x => x.Id == role.Id));
    }

    [Fact]
    public async Task SystemFlagAndBuiltInNameEachProtectRoleDeletion()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var flagged = NewRole("Protected internal role");
        flagged.IsSystemRole = true;
        foreach (var area in ApplicationAreas.All) flagged.PageAccesses.Add(Cell(area.Key));
        var viewer = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer);
        viewer.IsSystemRole = false;
        db.Roles.Add(flagged);
        await db.SaveChangesAsync();

        var service = Service(db, new TrackingAccessService());
        Assert.Equal("Built-in roles cannot be deleted.",
            (await service.DeleteRoleAsync(flagged.Id, administrator.Email, default)).Error);
        Assert.Equal("Built-in roles cannot be deleted.",
            (await service.DeleteRoleAsync(viewer.Id, administrator.Email, default)).Error);
        Assert.True(await db.Roles.AnyAsync(x => x.Id == flagged.Id));
        Assert.True(await db.Roles.AnyAsync(x => x.Id == viewer.Id));
    }

    [Fact]
    public async Task DeletingUnknownRoleFailsSafelyWithoutWrites()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        await db.SaveChangesAsync();
        var roleCount = await db.Roles.CountAsync();

        var result = await Service(db, new TrackingAccessService())
            .DeleteRoleAsync(int.MaxValue, administrator.Email, default);

        Assert.False(result.Succeeded);
        Assert.Equal("Role not found.", result.Error);
        Assert.Equal(roleCount, await db.Roles.CountAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task MissingOrInvalidRoleSelectionDoesNotImplicitlySelectViewer()
    {
        await using var db = CreateDb();
        var service = Service(db, new TrackingAccessService());

        var unselected = await service.GetUsersAsync(null, default);
        var invalid = await service.GetUsersAsync(int.MaxValue, default);

        Assert.Null(unselected.SelectedRole);
        Assert.Null(unselected.RoleComparison);
        Assert.Null(invalid.SelectedRole);
        Assert.Null(invalid.RoleComparison);
        Assert.Contains(unselected.Roles, x => x.Name == BuiltInRoleNames.Viewer);
    }

    [Theory]
    [InlineData(BuiltInRoleNames.Viewer)]
    [InlineData(BuiltInRoleNames.QcTech)]
    [InlineData(BuiltInRoleNames.Manager)]
    [InlineData(BuiltInRoleNames.Admin)]
    public async Task ExplicitRoleSelectionReturnsExactlyTheRequestedMatrix(string roleName)
    {
        await using var db = CreateDb();
        var role = await db.Roles.Include(x => x.PageAccesses).SingleAsync(x => x.Name == roleName);

        var page = await Service(db, new TrackingAccessService()).GetUsersAsync(role.Id, default);

        var selected = Assert.IsType<RoleAdminDetailViewModel>(page.SelectedRole);
        Assert.Equal(role.Id, selected.Id);
        Assert.Equal(role.Name, selected.Name);
        Assert.Equal(ApplicationAreas.All.Count, selected.Access.Count);
        foreach (var area in ApplicationAreas.All)
        {
            var expected = role.Name == BuiltInRoleNames.Admin
                ? PageAccessLevel.Admin
                : UserAccessService.ParseLevel(role.PageAccesses.Single(x => x.AreaKey == area.Key).AccessLevel);
            Assert.Equal(expected, selected.Access[area.Key]);
        }
    }

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

    [Fact]
    public async Task ImportedMigrationRoleIsEditableVisibleAndListsAssignedUsers()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var imported = NewRole("Imported Access A");
        imported.Description = "Imported from the legacy per-user access matrix during the role-based authorization conversion. Review and rename or reassign in User Administration.";
        foreach (var area in ApplicationAreas.All) imported.PageAccesses.Add(Cell(area.Key));
        var alexis = User("alexis@wp-packing.com");
        alexis.DisplayName = "Alexis Ledezma";
        alexis.UserRoles.Add(new UserRole { Role = imported });
        var james = User("james@fruitandland.com");
        james.DisplayName = "James Foreman";
        james.UserRoles.Add(new UserRole { Role = imported });
        var jorge = User("jorge@wp-packing.com");
        jorge.DisplayName = "Jorge Ledezma";
        jorge.UserRoles.Add(new UserRole { Role = imported });
        var archived = User("archived@fruitandland.com");
        archived.DisplayName = "Archived User";
        archived.IsActive = false;
        archived.UserRoles.Add(new UserRole { Role = imported });
        db.AddRange(imported, alexis, james, jorge, archived);
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());

        var page = await service.GetUsersAsync(imported.Id, default);
        var summary = Assert.Single(page.Roles, x => x.Id == imported.Id);
        Assert.True(summary.IsImportedMigrationRole);
        Assert.False(summary.IsSystemRole);
        Assert.True(summary.IsActive);
        Assert.Equal(["Alexis Ledezma", "Archived User (inactive)", "James Foreman", "Jorge Ledezma"], summary.AssignedUsers);
        Assert.True(page.SelectedRole!.IsImportedMigrationRole);
        Assert.Equal(summary.AssignedUsers, page.SelectedRole.AssignedUsers);

        Assert.Null(await service.UpdateRoleAsync(new UpdateRoleForm
        {
            RoleId = imported.Id,
            Name = "Packing Operations Review",
            Description = imported.Description,
            IsActive = true
        }, administrator.Email, default));
        Assert.Equal("Packing Operations Review", imported.Name);
        var renamedPage = await service.GetUsersAsync(imported.Id, default);
        Assert.True(renamedPage.SelectedRole!.IsImportedMigrationRole);
        Assert.Contains("active users", await service.UpdateRoleAsync(new UpdateRoleForm
        {
            RoleId = imported.Id,
            Name = imported.Name,
            Description = imported.Description,
            IsActive = false
        }, administrator.Email, default));
    }

    [Fact]
    public async Task RoleComparisonReturnsOnlyDifferencesAndCountsGainsLossesAndUnchangedAreas()
    {
        await using var db = CreateDb();
        var current = NewRole("Imported Access B");
        var compared = NewRole("QC Review Candidate");
        foreach (var area in ApplicationAreas.All)
        {
            current.PageAccesses.Add(Cell(area.Key));
            compared.PageAccesses.Add(Cell(area.Key));
        }
        current.PageAccesses.Single(x => x.AreaKey == ApplicationAreas.Receipts).AccessLevel = nameof(PageAccessLevel.View);
        current.PageAccesses.Single(x => x.AreaKey == ApplicationAreas.Transfers).AccessLevel = nameof(PageAccessLevel.Admin);
        compared.PageAccesses.Single(x => x.AreaKey == ApplicationAreas.Receipts).AccessLevel = nameof(PageAccessLevel.Create);
        db.AddRange(current, compared);
        await db.SaveChangesAsync();

        var page = await Service(db, new TrackingAccessService()).GetUsersAsync(current.Id, compared.Id, default);

        var comparison = Assert.IsType<RoleComparisonViewModel>(page.RoleComparison);
        Assert.Equal(1, comparison.AreasGained);
        Assert.Equal(1, comparison.AreasLost);
        Assert.Equal(ApplicationAreas.All.Count - 2, comparison.UnchangedAreas);
        Assert.Equal(2, comparison.Differences.Count);
        Assert.Contains(comparison.Differences, x => x.AreaKey == ApplicationAreas.Receipts && x.Change == "Gain");
        Assert.Contains(comparison.Differences, x => x.AreaKey == ApplicationAreas.Transfers && x.Change == "Loss");
    }

    [Fact]
    public async Task RoleAndMatrixUpdatesRedirectBackToAndRerenderTheEditedRole()
    {
        await using var db = CreateDb();
        var administrator = AddAdministrator(db);
        var role = NewRole("Dispatch review");
        foreach (var area in ApplicationAreas.All) role.PageAccesses.Add(Cell(area.Key));
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var service = Service(db, new TrackingAccessService());
        var controller = new AdminController(
            service,
            new AdminAuthorizationService(),
            null!,
            null!,
            null!,
            null!,
            new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Email, administrator.Email)],
                        "Test"))
                }
            }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            new TestTempDataProvider());

        var roleResult = Assert.IsType<RedirectToActionResult>(await controller.UpdateRole(new UpdateRoleForm
        {
            RoleId = role.Id,
            Name = role.Name,
            Description = "Updated description",
            IsActive = true
        }, default));
        Assert.Equal(nameof(AdminController.Users), roleResult.ActionName);
        Assert.Equal(role.Id, roleResult.RouteValues!["roleId"]);
        Assert.Equal("roles", roleResult.Fragment);

        var matrix = ApplicationAreas.All.ToDictionary(x => x.Key, _ => nameof(PageAccessLevel.None));
        matrix[ApplicationAreas.Rooms] = nameof(PageAccessLevel.View);
        var matrixResult = Assert.IsType<RedirectToActionResult>(await controller.UpdateRoleMatrix(new RoleAccessMatrixForm
        {
            RoleId = role.Id,
            Access = matrix
        }, default));
        Assert.Equal(nameof(AdminController.Users), matrixResult.ActionName);
        Assert.Equal(role.Id, matrixResult.RouteValues!["roleId"]);
        Assert.Equal("roles", matrixResult.Fragment);

        var rerendered = await service.GetUsersAsync(role.Id, default);
        Assert.Equal(role.Id, rerendered.SelectedRole!.Id);
        Assert.Equal("Updated description", rerendered.SelectedRole.Description);
        Assert.Equal(PageAccessLevel.View, rerendered.SelectedRole.Access[ApplicationAreas.Rooms]);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
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
