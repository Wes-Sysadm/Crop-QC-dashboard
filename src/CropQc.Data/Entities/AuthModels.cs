namespace CropQc.Data.Entities;

public sealed class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public DateTimeOffset? PasswordLastChangedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<UserRole> UserRoles { get; } = new List<UserRole>();
    public ICollection<QcSample> TakenSamples { get; } = new List<QcSample>();
}

public sealed class Role
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public ICollection<UserRole> UserRoles { get; } = new List<UserRole>();
    public ICollection<RolePermission> Permissions { get; } = new List<RolePermission>();
}

public sealed class UserRole
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public sealed class RolePermission
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public required string PermissionKey { get; set; }
    public required string Description { get; set; }
}

public sealed class PasswordPolicy
{
    public int Id { get; set; }
    public int MinimumLength { get; set; }
    public bool RequireUppercase { get; set; }
    public bool RequireLowercase { get; set; }
    public bool RequireNumber { get; set; }
    public bool RequireSymbol { get; set; }
    public int PasswordExpirationDays { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
