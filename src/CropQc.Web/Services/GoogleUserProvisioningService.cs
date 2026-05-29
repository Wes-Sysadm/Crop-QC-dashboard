using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IGoogleUserProvisioningService
{
    Task<ProvisionedUserAccess> ProvisionAllowedUserAsync(string email, string? displayName, string? googleSubjectId, CancellationToken cancellationToken);
}

public sealed record ProvisionedUserAccess(User User, IReadOnlyList<string> Roles);

public sealed class GoogleUserProvisioningService(CropQcDbContext dbContext, CropQc.Web.Auth.GoogleAuthenticationOptions authOptions, ILogger<GoogleUserProvisioningService> logger) : IGoogleUserProvisioningService
{
    public async Task<ProvisionedUserAccess> ProvisionAllowedUserAsync(string email, string? displayName, string? googleSubjectId, CancellationToken cancellationToken)
    {
        await EnsureUserAccessColumnsAsync(cancellationToken);
        await EnsureRolesAsync(cancellationToken);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var domain = CropQc.Web.Auth.GoogleAuthenticationOptions.GetEmailDomain(normalizedEmail) ?? "";
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail : displayName.Trim(),
                GoogleSubjectId = googleSubjectId,
                Domain = domain,
                IsActive = true,
                LastLoginAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                user.DisplayName = displayName.Trim();
            }

            if (!user.IsActive)
            {
                logger.LogWarning("Google login rejected for inactive user {Email}.", normalizedEmail);
                throw new UnauthorizedAccessException("Your Crop QC Dashboard user account is inactive.");
            }

            user.GoogleSubjectId ??= googleSubjectId;
            user.Domain = string.IsNullOrWhiteSpace(user.Domain) ? domain : user.Domain;
            user.LastLoginAt = now;
            user.UpdatedAt = now;
        }

        var roleName = authOptions.IsBootstrapAdminEmail(normalizedEmail) ? "Admin" : "Viewer";
        var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Name == roleName, cancellationToken);
        if (role is not null && user.UserRoles.All(x => x.RoleId != role.Id))
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var roles = await dbContext.UserRoles.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.Role.Name)
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        logger.LogInformation("Google login accepted for {Email}.", normalizedEmail);
        return new ProvisionedUserAccess(user, roles);
    }

    private async Task EnsureUserAccessColumnsAsync(CancellationToken cancellationToken)
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
