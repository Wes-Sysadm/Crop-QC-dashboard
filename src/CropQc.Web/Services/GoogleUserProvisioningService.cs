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
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var domain = CropQc.Web.Auth.GoogleAuthenticationOptions.GetEmailDomain(normalizedEmail) ?? "";
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        var isNewUser = user is null;
        Role? initialRole = null;

        if (user is null)
        {
            var roleName = authOptions.IsBootstrapAdminEmail(normalizedEmail) ? BuiltInRoleNames.Admin : BuiltInRoleNames.Viewer;
            initialRole = await dbContext.Roles.SingleOrDefaultAsync(x => x.Name == roleName && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException($"Required active role '{roleName}' is not configured.");
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
            user.UserRoles.Add(new UserRole { Role = initialRole });
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

            if (user.UserRoles.Count != 1 || !user.UserRoles.Single().Role.IsActive)
            {
                logger.LogError("Google login rejected because {Email} has {RoleCount} role assignments or an inactive role; exactly one active role is required.", normalizedEmail, user.UserRoles.Count);
                throw new UnauthorizedAccessException("Your Crop QC Dashboard account requires exactly one active role. Contact an administrator.");
            }

            user.GoogleSubjectId ??= googleSubjectId;
            user.Domain = string.IsNullOrWhiteSpace(user.Domain) ? domain : user.Domain;
            user.LastLoginAt = now;
            user.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var role = isNewUser ? initialRole! : user.UserRoles.Single().Role;
        logger.LogInformation("Google login accepted for {Email}.", normalizedEmail);
        return new ProvisionedUserAccess(user, [role.Name]);
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

}
