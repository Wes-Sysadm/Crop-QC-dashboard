using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IUserAdminService
{
    Task<UserAdminPageViewModel> GetUsersAsync(CancellationToken cancellationToken);
    Task<string?> AddUserAsync(AddUserForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateUserAccessAsync(UpdateUserAccessForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateUserEmploymentAsync(UpdateUserEmploymentForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateUserMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class UserAdminService(CropQcDbContext dbContext, GoogleAuthenticationOptions authOptions, IUserAccessService userAccessService) : IUserAdminService
{
    private static readonly IReadOnlyDictionary<string, string> RoleSummaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Admin"] = "Full access, including users, master data, configuration, overrides, exports, and audit review.",
        ["Manager"] = "QC workflow manager; can use normal QC pages, manage master data, and manage QC Stations.",
        ["QC User"] = "Same-day QC entry for receipts, samples, pressure, weight, grade, defects, starch, and photos.",
        ["Viewer"] = "Read-only dashboard, receipt, and sample access."
    };

    private static readonly IReadOnlyList<RolePermissionViewModel> RolePermissionRows =
    [
        new("View dashboard", "Yes", "Yes", "Yes", "Yes"),
        new("View Daily QC", "Yes", "Yes", "Yes", "Yes"),
        new("View receipts/samples", "Yes", "Yes", "Yes", "Yes"),
        new("View Master Data", "Yes", "Yes", "No", "No"),
        new("Manage QC Stations", "Yes", "Yes", "No", "No"),
        new("Manage users/roles", "Yes", "No", "No", "No"),
        new("Open Admin Downloads", "Yes", "No", "No", "No"),
        new("Edit configuration", "Yes", "No", "No", "No"),
        new("Create receiving receipts", "Yes", "Yes", "Yes", "No"),
        new("Create QC samples", "Yes", "Yes", "Yes", "No"),
        new("Enter same-day QC data", "Yes", "Yes", "Yes", "No"),
        new("Edit same-day QC data", "Yes", "Yes", "Yes", "No"),
        new("Edit older QC data", "Yes", "Yes", "No", "No"),
        new("Void samples", "Yes", "Yes", "No", "No"),
        new("Resend QC Summary", "Yes", "Yes", "No", "No"),
        new("Override/send with missing data", "Yes", "Yes", "No", "No"),
        new("Edit master data", "Yes", "Yes", "No", "No"),
        new("View audit logs", "Yes", "No", "No", "No"),
        new("Export receiving data", "Yes", "Yes", "No", "No")
    ];

    public async Task<UserAdminPageViewModel> GetUsersAsync(CancellationToken cancellationToken)
    {
        await EnsureUserAccessColumnsAsync(cancellationToken);
        await EnsureRolesAsync(cancellationToken);
        var roleEntities = await dbContext.Roles.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var roles = roleEntities.Select(x => new RoleOptionViewModel(x.Id, x.Name, RoleSummary(x.Name))).ToList();
        var users = await dbContext.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.UserAssignments)
            .Include(x => x.EmploymentUpdatedByUser)
            .Include(x => x.EmploymentHistory).ThenInclude(x => x.ChangedByUser)
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);

        var fillGroups = await dbContext.EndOfDayFillReportGroups.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return new UserAdminPageViewModel
        {
            Roles = roles,
            RolePermissions = RolePermissionRows,
            Areas = ApplicationAreas.All.Select(x => new ApplicationAreaViewModel(x.Key, x.Name, x.Group, x.Route)).ToList(),
            AccessMatrix = await userAccessService.GetMatrixAsync(cancellationToken),
            AddUserForm = new AddUserForm { RoleId = roles.FirstOrDefault(x => x.Name == "Viewer")?.Id ?? roles.FirstOrDefault()?.Id ?? 0 },
            EndOfDayFillGroups = fillGroups.Select(x => new EndOfDayFillGroupOption(x.Id, x.Name, x.Facility)).ToList(),
            Users = users.Select(x =>
            {
                var roleName = PrimaryRoleName(x);
                return new UserAdminListItem(
                    x.Id,
                    x.Email,
                    x.DisplayName,
                    x.Domain,
                    roleName,
                    RoleSummary(roleName),
                    x.IsActive,
                    x.LastLoginAt,
                    x.EmploymentFacility,
                    x.EmploymentEffectiveAt,
                    x.EmploymentUpdatedByUser?.DisplayName ?? x.EmploymentUpdatedByUser?.Email ?? "—",
                    x.EmploymentUpdatedAt,
                    x.EmploymentHistory
                        .OrderByDescending(history => history.ChangedAt)
                        .Select(history => new UserEmploymentHistoryViewModel(
                            history.Id,
                            history.PreviousEmploymentFacility,
                            history.EmploymentFacility,
                            history.EffectiveAt,
                            history.ChangedByUser?.DisplayName ?? history.ChangedByUser?.Email ?? "System",
                            history.ChangedAt))
                        .ToList(),
                    x.UserAssignments.Select(assignment => assignment.ReportGroupId).Order().ToList());
            }).ToList()
        };
    }

    public async Task<string?> AddUserAsync(AddUserForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureUserAccessColumnsAsync(cancellationToken);
        await EnsureRolesAsync(cancellationToken);
        var email = form.Email.Trim().ToLowerInvariant();
        var domain = GoogleAuthenticationOptions.GetEmailDomain(email);
        if (domain is null || !authOptions.AllowedDomains.Contains(domain))
        {
            return "User email must be in an allowed Google domain.";
        }

        if (await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            return "User already exists.";
        }

        var role = await dbContext.Roles.FindAsync([form.RoleId], cancellationToken);
        if (role is null) return "Selected role was not found.";

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
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await AddAuditAsync("create", "users", user.Id.ToString(), changedByEmail, null, JsonSerializer.Serialize(new { user.Email, user.DisplayName, Role = role.Name, user.IsActive }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> UpdateUserAccessAsync(UpdateUserAccessForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureUserAccessColumnsAsync(cancellationToken);
        await EnsureRolesAsync(cancellationToken);
        var user = await dbContext.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == form.UserId, cancellationToken);
        if (user is null) return "User not found.";
        var role = await dbContext.Roles.FindAsync([form.RoleId], cancellationToken);
        if (role is null) return "Selected role was not found.";

        var before = JsonSerializer.Serialize(new { user.Email, Role = PrimaryRoleName(user), user.IsActive });
        var removingAdmin = user.UserRoles.Any(x => x.Role.Name == "Admin") && role.Name != "Admin";
        var deactivatingAdmin = user.UserRoles.Any(x => x.Role.Name == "Admin") && !form.IsActive;
        if ((removingAdmin || deactivatingAdmin) && !await HasAnotherActiveAdminAsync(user.Id, cancellationToken))
        {
            return "Cannot remove or deactivate the last active Admin.";
        }

        dbContext.UserRoles.RemoveRange(user.UserRoles);
        dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        user.IsActive = form.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("update", "users", user.Id.ToString(), changedByEmail, before, JsonSerializer.Serialize(new { user.Email, Role = role.Name, user.IsActive }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> UpdateUserEmploymentAsync(UpdateUserEmploymentForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var normalized = EmploymentFacilities.Normalize(form.EmploymentFacility);
        if (normalized is null)
        {
            return "Employment must be WP, EBS, Shared / Management, or Unassigned.";
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == form.UserId, cancellationToken);
        if (user is null)
        {
            return "User not found.";
        }

        var changedBy = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);
        if (changedBy is null)
        {
            return "The administrator account could not be resolved.";
        }

        var previous = EmploymentFacilities.Normalize(user.EmploymentFacility) ?? EmploymentFacilities.Unassigned;
        var previousEffectiveAt = user.EmploymentEffectiveAt;
        var effectiveAt = form.EffectiveAt ?? DateTimeOffset.UtcNow;
        if (string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase)
            && user.EmploymentEffectiveAt == effectiveAt)
        {
            return null;
        }

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
        await AddAuditAsync(
            "update-employment",
            "user-employment",
            user.Id.ToString(),
            changedByEmail,
            JsonSerializer.Serialize(new { user.Email, EmploymentFacility = previous, EmploymentEffectiveAt = previousEffectiveAt }),
            JsonSerializer.Serialize(new { user.Email, EmploymentFacility = normalized, EffectiveAt = effectiveAt }),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> UpdateUserMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken) =>
        await userAccessService.SaveMatrixAsync(form, changedByEmail, cancellationToken);

    private async Task<bool> HasAnotherActiveAdminAsync(int userId, CancellationToken cancellationToken) =>
        await dbContext.Users.AnyAsync(x => x.Id != userId && x.IsActive && x.UserRoles.Any(role => role.Role.Name == "Admin"), cancellationToken);

    private static string PrimaryRoleName(User user) =>
        user.UserRoles.OrderBy(x => x.RoleId).Select(x => x.Role.Name).FirstOrDefault() ?? "Viewer";

    private static string RoleSummary(string roleName) =>
        RoleSummaries.TryGetValue(roleName, out var summary) ? summary : "Custom role.";

    private async Task AddAuditAsync(string action, string entityName, string entityKey, string by, string? before, string? after, CancellationToken ct)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == by, ct);
        dbContext.AuditLogs.Add(new AuditLog { Action = action, EntityName = entityName, EntityKey = entityKey, UserId = user?.Id, BeforeValuesJson = before, AfterValuesJson = after, SourceApplication = "CropQc.Web", CreatedAt = DateTimeOffset.UtcNow });
    }

    public async Task EnsureUserAccessColumnsAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "GoogleSubjectId" character varying(200) NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Domain" character varying(150) NOT NULL DEFAULT '';
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastLoginAt" timestamp with time zone NULL;
                CREATE INDEX IF NOT EXISTS "IX_Users_GoogleSubjectId" ON "Users" ("GoogleSubjectId");
                """, cancellationToken);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Users', 'GoogleSubjectId') IS NULL ALTER TABLE [Users] ADD [GoogleSubjectId] nvarchar(200) NULL;
                IF COL_LENGTH('Users', 'Domain') IS NULL ALTER TABLE [Users] ADD [Domain] nvarchar(150) NOT NULL CONSTRAINT [DF_Users_Domain] DEFAULT N'';
                IF COL_LENGTH('Users', 'LastLoginAt') IS NULL ALTER TABLE [Users] ADD [LastLoginAt] datetimeoffset NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_GoogleSubjectId' AND object_id = OBJECT_ID(N'[Users]')) CREATE INDEX [IX_Users_GoogleSubjectId] ON [Users] ([GoogleSubjectId]);
                """, cancellationToken);
        }
    }

    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var role in new[]
        {
            ("Admin", "Full access, user management, master data editing, configuration, and override send."),
            ("Manager", "Review, resend, and override workflows."),
            ("QC User", "Create and edit same-day QC data."),
            ("Viewer", "Read-only dashboard access.")
        })
        {
            if (!await dbContext.Roles.AnyAsync(x => x.Name == role.Item1, cancellationToken))
            {
                dbContext.Roles.Add(new Role { Name = role.Item1, Description = role.Item2, IsSystemRole = true });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
