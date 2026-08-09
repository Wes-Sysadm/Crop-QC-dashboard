using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class GoogleUserProvisioningRoleTests
{
    [Theory]
    [InlineData("viewer@fruitandland.com", BuiltInRoleNames.Viewer)]
    [InlineData("owner@fruitandland.com", BuiltInRoleNames.Admin)]
    public async Task NewUserReceivesExactlyOneExpectedActiveRole(string email, string expectedRole)
    {
        await using var db = CreateDb();
        var result = await Service(db).ProvisionAllowedUserAsync(email, "New User", "subject", default);

        Assert.Equal([expectedRole], result.Roles);
        var assignments = await db.UserRoles.Include(x => x.Role).Where(x => x.UserId == result.User.Id).ToListAsync();
        Assert.Single(assignments);
        Assert.True(assignments[0].Role.IsActive);
        Assert.Equal(expectedRole, assignments[0].Role.Name);
    }

    [Fact]
    public async Task MissingRequiredInitialRoleDoesNotPersistPartialUser()
    {
        await using var db = CreateDb();
        (await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer)).IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db)
            .ProvisionAllowedUserAsync("missing-role@fruitandland.com", "Missing Role", "subject", default));

        Assert.False(await db.Users.AnyAsync(x => x.Email == "missing-role@fruitandland.com"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InvalidExistingRoleCardinalityRejectsWithoutUpdatingLoginMetadata(int roleCount)
    {
        await using var db = CreateDb();
        var originalLogin = DateTimeOffset.UtcNow.AddDays(-2);
        var user = new User
        {
            Email = "conflict@fruitandland.com",
            DisplayName = "Original Name",
            Domain = "fruitandland.com",
            IsActive = true,
            LastLoginAt = originalLogin,
            CreatedAt = originalLogin,
            UpdatedAt = originalLogin
        };
        if (roleCount > 0) user.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Viewer) });
        if (roleCount > 1) user.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Manager) });
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db)
            .ProvisionAllowedUserAsync(user.Email, "Changed Name", "new-subject", default));

        db.ChangeTracker.Clear();
        var unchanged = await db.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal("Original Name", unchanged.DisplayName);
        Assert.Equal(originalLogin, unchanged.LastLoginAt);
        Assert.Null(unchanged.GoogleSubjectId);
    }

    private static CropQcDbContext CreateDb()
    {
        var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static GoogleUserProvisioningService Service(CropQcDbContext db) => new(
        db,
        new GoogleAuthenticationOptions
        {
            BootstrapAdminEmails = new HashSet<string>(["owner@fruitandland.com"], StringComparer.OrdinalIgnoreCase)
        },
        NullLogger<GoogleUserProvisioningService>.Instance);
}
