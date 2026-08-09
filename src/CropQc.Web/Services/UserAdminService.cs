using System.Text.Json;
using System.Data;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IUserAdminService
{
    Task<UserAdminPageViewModel> GetUsersAsync(int? selectedRoleId, CancellationToken cancellationToken);
    Task<UserAdminPageViewModel> GetUsersAsync(int? selectedRoleId, int? compareRoleId, CancellationToken cancellationToken);
    Task<string?> AddUserAsync(AddUserForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateUserAccessAsync(UpdateUserAccessForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateUserEmploymentAsync(UpdateUserEmploymentForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> CreateRoleAsync(CreateRoleForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateRoleAsync(UpdateRoleForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<DeleteRoleResult> DeleteRoleAsync(int roleId, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateRoleMatrixAsync(RoleAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class UserAdminService(
    CropQcDbContext dbContext,
    GoogleAuthenticationOptions authOptions,
    IUserAccessService userAccessService) : IUserAdminService
{
    private const string ImportedRolePrefix = "Imported Access ";
    private const string ImportedRoleDescriptionPrefix = "Imported from the legacy per-user access matrix";

    public Task<UserAdminPageViewModel> GetUsersAsync(int? selectedRoleId, CancellationToken cancellationToken) =>
        GetUsersAsync(selectedRoleId, null, cancellationToken);

    public async Task<UserAdminPageViewModel> GetUsersAsync(int? selectedRoleId, int? compareRoleId, CancellationToken cancellationToken)
    {
        var roleEntities = await dbContext.Roles.AsNoTracking()
            .Include(x => x.PageAccesses)
            .Include(x => x.UserRoles).ThenInclude(x => x.User)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var selectedRole = selectedRoleId is null
            ? null
            : roleEntities.SingleOrDefault(x => x.Id == selectedRoleId);
        var comparisonRole = roleEntities.SingleOrDefault(x => x.Id == compareRoleId && x.Id != selectedRole?.Id);
        var roles = roleEntities.Select(ToRoleListItem).ToList();
        var users = await dbContext.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.UserAssignments)
            .Include(x => x.EmploymentUpdatedByUser)
            .Include(x => x.EmploymentHistory).ThenInclude(x => x.ChangedByUser)
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);
        var fillGroups = await dbContext.EndOfDayFillReportGroups.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return new UserAdminPageViewModel
        {
            Roles = roles,
            Areas = ApplicationAreas.All.Select(x => new ApplicationAreaViewModel(x.Key, x.Name, x.Group, x.Route)).ToList(),
            SelectedRole = selectedRole is null ? null : ToRoleDetail(selectedRole),
            RoleComparison = selectedRole is null || comparisonRole is null
                ? null
                : CompareRoles(selectedRole, comparisonRole),
            AddUserForm = new AddUserForm
            {
                RoleId = roleEntities.FirstOrDefault(x => x.Name == BuiltInRoleNames.Viewer && x.IsActive)?.Id
                    ?? roleEntities.FirstOrDefault(x => x.IsActive)?.Id
                    ?? 0
            },
            EndOfDayFillGroups = fillGroups.Select(x => new EndOfDayFillGroupOption(x.Id, x.Name, x.Facility)).ToList(),
            Users = users.Select(ToUserListItem).ToList()
        };
    }

    public async Task<string?> AddUserAsync(AddUserForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var email = form.Email.Trim().ToLowerInvariant();
        var domain = GoogleAuthenticationOptions.GetEmailDomain(email);
        if (domain is null || !authOptions.AllowedDomains.Contains(domain)) return "User email must be in an allowed Google domain.";
        if (await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken)) return "User already exists.";

        var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Id == form.RoleId, cancellationToken);
        if (role is null || !role.IsActive) return "Select an active role.";

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(form.DisplayName) ? email : form.DisplayName.Trim(),
            Domain = domain,
            IsActive = form.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRole { User = user, Role = role });
        await AddAuditAsync("create", "users", email, changedByEmail, null,
            JsonSerializer.Serialize(new { user.Email, user.DisplayName, Role = role.Name, user.IsActive }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        userAccessService.InvalidateAll();
        return null;
    }

    public async Task<string?> UpdateUserAccessAsync(UpdateUserAccessForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == form.UserId, cancellationToken);
        if (user is null) return "User not found.";
        if (user.UserRoles.Count != 1) return "The user does not have exactly one current role. Resolve the role data before editing.";
        var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Id == form.RoleId, cancellationToken);
        if (role is null || !role.IsActive) return "Select an active role.";

        var currentRole = user.UserRoles.Single().Role;
        var removingAdmin = currentRole.Name == BuiltInRoleNames.Admin && role.Name != BuiltInRoleNames.Admin;
        var deactivatingAdmin = currentRole.Name == BuiltInRoleNames.Admin && !form.IsActive;
        if ((removingAdmin || deactivatingAdmin) && !await HasAnotherActiveAdminAsync(user.Id, cancellationToken))
            return "Cannot remove or deactivate the last active Admin.";

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var before = JsonSerializer.Serialize(new { user.Email, Role = currentRole.Name, user.IsActive });
        if (currentRole.Id != role.Id)
        {
            dbContext.UserRoles.Remove(user.UserRoles.Single());
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }
        user.IsActive = form.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("update", "users", user.Id.ToString(), changedByEmail, before,
            JsonSerializer.Serialize(new { user.Email, Role = role.Name, user.IsActive }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        userAccessService.InvalidateAll();
        return null;
    }

    public async Task<string?> UpdateUserEmploymentAsync(UpdateUserEmploymentForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var normalized = EmploymentFacilities.Normalize(form.EmploymentFacility);
        if (normalized is null) return "Employment must be WP, EBS, Shared / Management, or Unassigned.";
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == form.UserId, cancellationToken);
        if (user is null) return "User not found.";
        var changedBy = await ResolveAdministratorAsync(changedByEmail, cancellationToken);
        if (changedBy is null) return "The administrator account could not be resolved.";

        var previous = EmploymentFacilities.Normalize(user.EmploymentFacility) ?? EmploymentFacilities.Unassigned;
        var previousEffectiveAt = user.EmploymentEffectiveAt;
        var effectiveAt = form.EffectiveAt ?? DateTimeOffset.UtcNow;
        if (string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase) && previousEffectiveAt == effectiveAt) return null;

        var now = DateTimeOffset.UtcNow;
        user.EmploymentFacility = normalized;
        user.EmploymentEffectiveAt = effectiveAt;
        user.EmploymentUpdatedByUserId = changedBy.Id;
        user.EmploymentUpdatedAt = now;
        user.UpdatedAt = now;
        dbContext.UserEmploymentHistory.Add(new UserEmploymentHistory
        {
            UserId = user.Id,
            PreviousEmploymentFacility = previous,
            EmploymentFacility = normalized,
            EffectiveAt = effectiveAt,
            ChangedByUserId = changedBy.Id,
            ChangedAt = now
        });
        await AddAuditAsync("update-employment", "user-employment", user.Id.ToString(), changedByEmail,
            JsonSerializer.Serialize(new { user.Email, EmploymentFacility = previous, EmploymentEffectiveAt = previousEffectiveAt }),
            JsonSerializer.Serialize(new { user.Email, EmploymentFacility = normalized, EffectiveAt = effectiveAt }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> CreateRoleAsync(CreateRoleForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var name = form.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return "Role name is required.";
        var normalizedName = BuiltInRoleNames.Normalize(name);
        if (await dbContext.Roles.AnyAsync(x => x.NormalizedName == normalizedName, cancellationToken)) return "A role with that name already exists.";
        Role? source = null;
        if (form.CopyFromRoleId is not null)
        {
            source = await dbContext.Roles.AsNoTracking().Include(x => x.PageAccesses)
                .SingleOrDefaultAsync(x => x.Id == form.CopyFromRoleId, cancellationToken);
            if (source is null) return "The role selected for copying was not found.";
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var changedBy = await ResolveAdministratorAsync(changedByEmail, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var role = new Role
        {
            Name = name,
            NormalizedName = normalizedName,
            Description = form.Description?.Trim(),
            IsSystemRole = false,
            IsActive = true
        };
        foreach (var area in ApplicationAreas.All)
        {
            var level = source?.PageAccesses.SingleOrDefault(x => x.AreaKey == area.Key)?.AccessLevel ?? nameof(PageAccessLevel.None);
            role.PageAccesses.Add(new RolePageAccess
            {
                AreaKey = area.Key,
                AccessLevel = UserAccessService.PersistedLevel(UserAccessService.ParseLevel(level)),
                UpdatedByUserId = changedBy?.Id,
                UpdatedAt = now
            });
        }
        dbContext.Roles.Add(role);
        await AddAuditAsync("create", "roles", normalizedName, changedByEmail, null,
            JsonSerializer.Serialize(new { role.Name, role.Description, CopyFrom = source?.Name }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        userAccessService.InvalidateAll();
        return null;
    }

    public async Task<string?> UpdateRoleAsync(UpdateRoleForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.Include(x => x.UserRoles).ThenInclude(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == form.RoleId, cancellationToken);
        if (role is null) return "Role not found.";
        if (role.IsSystemRole)
        {
            if (!string.Equals(role.Name, form.Name?.Trim(), StringComparison.Ordinal)
                || !form.IsActive) return "Built-in roles cannot be renamed or deactivated.";
        }
        if (role.Name == BuiltInRoleNames.Admin && !form.IsActive) return "The Admin role cannot be deactivated.";
        if (!form.IsActive && role.UserRoles.Any(x => x.User.IsActive)) return "A role assigned to active users cannot be deactivated.";

        var name = form.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return "Role name is required.";
        var normalized = BuiltInRoleNames.Normalize(name);
        if (await dbContext.Roles.AnyAsync(x => x.Id != role.Id && x.NormalizedName == normalized, cancellationToken)) return "A role with that name already exists.";
        var before = JsonSerializer.Serialize(new { role.Name, role.Description, role.IsActive });
        role.Name = name;
        role.NormalizedName = normalized;
        role.Description = form.Description?.Trim();
        role.IsActive = form.IsActive;
        await AddAuditAsync("update", "roles", role.Id.ToString(), changedByEmail, before,
            JsonSerializer.Serialize(new { role.Name, role.Description, role.IsActive }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        userAccessService.InvalidateAll();
        return null;
    }

    public async Task<DeleteRoleResult> DeleteRoleAsync(
        int roleId,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var role = await dbContext.Roles
            .Include(x => x.UserRoles)
            .Include(x => x.PageAccesses)
            .Include(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null) return DeleteRoleResult.Failure("Role not found.");
        if (IsProtectedBuiltInRole(role)) return DeleteRoleResult.Failure("Built-in roles cannot be deleted.");
        if (role.UserRoles.Count != 0) return DeleteRoleResult.Failure("Move all users off this role before deleting it.");

        var before = JsonSerializer.Serialize(new
        {
            RoleId = role.Id,
            role.Name,
            role.Description,
            role.IsActive,
            role.IsSystemRole,
            role.NormalizedName,
            AssignedUserCount = 0,
            PermissionMatrix = ApplicationAreas.All.Select(area =>
            {
                var cell = role.PageAccesses.SingleOrDefault(x => x.AreaKey == area.Key);
                return new
                {
                    area.Key,
                    area.Name,
                    AccessLevel = cell?.AccessLevel,
                    IsPersisted = cell is not null,
                    cell?.UpdatedByUserId,
                    cell?.UpdatedAt
                };
            }).ToList(),
            RolePermissions = role.Permissions
                .OrderBy(x => x.PermissionKey)
                .Select(x => new { x.PermissionKey, x.Description })
                .ToList()
        });

        await AddAuditAsync("delete", "roles", role.Id.ToString(), changedByEmail, before, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var roleName = role.Name;
        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        userAccessService.InvalidateAll();
        return DeleteRoleResult.Success(roleName);
    }

    public async Task<string?> UpdateRoleMatrixAsync(RoleAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.Include(x => x.PageAccesses)
            .SingleOrDefaultAsync(x => x.Id == form.RoleId, cancellationToken);
        if (role is null) return "Role not found.";
        if (role.Name == BuiltInRoleNames.Admin) return "Admin always has full access and its matrix cannot be edited.";
        var knownKeys = ApplicationAreas.All.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var submittedKeys = form.Access.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (submittedKeys.Except(knownKeys, StringComparer.OrdinalIgnoreCase).Any()) return "The submitted matrix contains an unknown application area.";
        if (knownKeys.Except(submittedKeys, StringComparer.OrdinalIgnoreCase).Any()) return "Submit an explicit access level for every application area.";
        if (form.Access.Values.Any(x => !Enum.TryParse<PageAccessLevel>(x, true, out _))) return "The submitted matrix contains an invalid access level.";

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var changedBy = await ResolveAdministratorAsync(changedByEmail, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var area in ApplicationAreas.All)
        {
            var requested = UserAccessService.ParseLevel(form.Access[area.Key]);
            var existing = role.PageAccesses.SingleOrDefault(x => x.AreaKey == area.Key);
            var previous = UserAccessService.ParseLevel(existing?.AccessLevel);
            if (existing is null)
            {
                existing = new RolePageAccess { RoleId = role.Id, AreaKey = area.Key, AccessLevel = nameof(PageAccessLevel.None), UpdatedAt = now };
                dbContext.RolePageAccesses.Add(existing);
            }
            if (previous == requested) continue;
            existing.AccessLevel = UserAccessService.PersistedLevel(requested);
            existing.UpdatedByUserId = changedBy?.Id;
            existing.UpdatedAt = now;
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "update",
                EntityName = "role-page-access",
                EntityKey = $"{role.Id}:{area.Key}",
                UserId = changedBy?.Id,
                BeforeValuesJson = JsonSerializer.Serialize(new { RoleId = role.Id, Role = role.Name, AreaKey = area.Key, Area = area.Name, AccessLevel = previous.ToString() }),
                AfterValuesJson = JsonSerializer.Serialize(new { RoleId = role.Id, Role = role.Name, AreaKey = area.Key, Area = area.Name, AccessLevel = requested.ToString() }),
                SourceApplication = "CropQc.Web",
                CreatedAt = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        userAccessService.InvalidateAll();
        return null;
    }

    private RoleAdminListItemViewModel ToRoleListItem(Role role) => new(
        role.Id, role.Name, role.Description ?? "", role.IsSystemRole, role.IsActive,
        role.UserRoles.Count(x => x.User.IsActive), role.Name == BuiltInRoleNames.Admin,
        ApplicationAreas.All.All(area => role.PageAccesses.Any(x => x.AreaKey == area.Key)),
        IsImportedMigrationRole(role),
        role.UserRoles
            .Select(x => x.User.DisplayName + (x.User.IsActive ? "" : " (inactive)"))
            .OrderBy(x => x).ToList());

    private RoleAdminDetailViewModel ToRoleDetail(Role role) => new(
        role.Id, role.Name, role.Description ?? "", role.IsSystemRole, role.IsActive,
        role.Name == BuiltInRoleNames.Admin,
        IsImportedMigrationRole(role),
        role.UserRoles
            .Select(x => x.User.DisplayName + (x.User.IsActive ? "" : " (inactive)"))
            .OrderBy(x => x).ToList(),
        ApplicationAreas.All.ToDictionary(
            area => area.Key,
            area => role.Name == BuiltInRoleNames.Admin
                ? PageAccessLevel.Admin
                : UserAccessService.ParseLevel(role.PageAccesses.SingleOrDefault(x => x.AreaKey == area.Key)?.AccessLevel),
            StringComparer.OrdinalIgnoreCase));

    private RoleComparisonViewModel CompareRoles(Role current, Role compared)
    {
        var currentAccess = ToRoleDetail(current).Access;
        var comparedAccess = ToRoleDetail(compared).Access;
        var differences = ApplicationAreas.All
            .Select(area => new
            {
                Area = area,
                Current = currentAccess[area.Key],
                Compared = comparedAccess[area.Key]
            })
            .Where(x => x.Current != x.Compared)
            .Select(x => new RoleComparisonDifferenceViewModel(
                x.Area.Key,
                x.Area.Name,
                x.Area.Group,
                x.Current,
                x.Compared,
                x.Compared > x.Current ? "Gain" : "Loss"))
            .ToList();
        return new RoleComparisonViewModel(
            current.Id,
            current.Name,
            compared.Id,
            compared.Name,
            differences.Count(x => x.Change == "Gain"),
            differences.Count(x => x.Change == "Loss"),
            ApplicationAreas.All.Count - differences.Count,
            differences);
    }

    private static bool IsImportedMigrationRole(Role role) =>
        !role.IsSystemRole
        && (role.Name.StartsWith(ImportedRolePrefix, StringComparison.OrdinalIgnoreCase)
            || (role.Description?.StartsWith(ImportedRoleDescriptionPrefix, StringComparison.OrdinalIgnoreCase) ?? false));

    private static bool IsProtectedBuiltInRole(Role role) =>
        role.IsSystemRole
        || BuiltInRoleNames.All.Contains(role.Name)
        || BuiltInRoleNames.All.Any(x =>
            string.Equals(BuiltInRoleNames.Normalize(x), role.NormalizedName, StringComparison.OrdinalIgnoreCase));

    private static UserAdminListItem ToUserListItem(User user)
    {
        var assignment = user.UserRoles.Count == 1 ? user.UserRoles.Single() : null;
        var roleName = assignment?.Role.Name ?? (user.UserRoles.Count == 0 ? "No role" : "Role conflict");
        var roleSummary = assignment?.Role.Description ?? "This user requires exactly one active role before access is granted.";
        return new UserAdminListItem(
            user.Id, user.Email, user.DisplayName, user.Domain, roleName, roleSummary, user.IsActive,
            user.LastLoginAt, user.EmploymentFacility, user.EmploymentEffectiveAt,
            user.EmploymentUpdatedByUser?.DisplayName ?? user.EmploymentUpdatedByUser?.Email ?? "—",
            user.EmploymentUpdatedAt,
            user.EmploymentHistory.OrderByDescending(x => x.ChangedAt)
                .Select(x => new UserEmploymentHistoryViewModel(
                    x.Id, x.PreviousEmploymentFacility, x.EmploymentFacility, x.EffectiveAt,
                    x.ChangedByUser?.DisplayName ?? x.ChangedByUser?.Email ?? "System", x.ChangedAt)).ToList(),
            user.UserAssignments.Select(x => x.ReportGroupId).Order().ToList(),
            UserAccessService.IsOwner(user.Email),
            assignment?.RoleId);
    }

    private async Task<bool> HasAnotherActiveAdminAsync(int userId, CancellationToken cancellationToken) =>
        await dbContext.Users.AnyAsync(x => x.Id != userId && x.IsActive
            && x.UserRoles.Count == 1
            && x.UserRoles.Any(r => r.Role.Name == BuiltInRoleNames.Admin && r.Role.IsActive), cancellationToken);

    private Task<User?> ResolveAdministratorAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email.Trim().ToLowerInvariant(), cancellationToken);

    private async Task AddAuditAsync(string action, string entityName, string entityKey, string by, string? before, string? after, CancellationToken ct)
    {
        var user = await ResolveAdministratorAsync(by, ct);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            UserId = user?.Id,
            BeforeValuesJson = before,
            AfterValuesJson = after,
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational() ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);
}
