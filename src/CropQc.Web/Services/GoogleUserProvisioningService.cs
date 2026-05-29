using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IGoogleUserProvisioningService
{
    Task<User> ProvisionAllowedUserAsync(string email, string? displayName, CancellationToken cancellationToken);
}

public sealed class GoogleUserProvisioningService(CropQcDbContext dbContext, ILogger<GoogleUserProvisioningService> logger) : IGoogleUserProvisioningService
{
    public async Task<User> ProvisionAllowedUserAsync(string email, string? displayName, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users
            .Include(x => x.UserRoles)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail : displayName.Trim(),
                IsActive = true,
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

            user.IsActive = true;
            user.UpdatedAt = now;
        }

        var viewerRole = await dbContext.Roles.SingleOrDefaultAsync(x => x.Name == "Viewer", cancellationToken);
        if (viewerRole is not null && user.UserRoles.All(x => x.RoleId != viewerRole.Id))
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = viewerRole.Id });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Google login accepted for {Email}.", normalizedEmail);
        return user;
    }
}
