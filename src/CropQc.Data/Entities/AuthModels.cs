namespace CropQc.Data.Entities;

public sealed class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? GoogleSubjectId { get; set; }
    public string Domain { get; set; } = "";
    public string? PasswordHash { get; set; }
    public DateTimeOffset? PasswordLastChangedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string EmploymentFacility { get; set; } = EmploymentFacilities.Unassigned;
    public DateTimeOffset? EmploymentEffectiveAt { get; set; }
    public int? EmploymentUpdatedByUserId { get; set; }
    public User? EmploymentUpdatedByUser { get; set; }
    public DateTimeOffset? EmploymentUpdatedAt { get; set; }
    public ICollection<UserRole> UserRoles { get; } = new List<UserRole>();
    public ICollection<UserPageAccess> PageAccesses { get; } = new List<UserPageAccess>();
    public ICollection<QcSample> TakenSamples { get; } = new List<QcSample>();
    public ICollection<UserGoogleCredential> GoogleCredentials { get; } = new List<UserGoogleCredential>();
    public ICollection<UserEmploymentHistory> EmploymentHistory { get; } = new List<UserEmploymentHistory>();
    public ICollection<EndOfDayFillUserGroupAssignment> UserAssignments { get; } = new List<EndOfDayFillUserGroupAssignment>();
}

public static class EmploymentFacilities
{
    public const string Wp = "WP";
    public const string Ebs = "EBS";
    public const string Shared = "Shared";
    public const string Unassigned = "Unassigned";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Wp, Ebs, Shared, Unassigned };

    public static string? Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "WP" => Wp,
        "EBS" => Ebs,
        "SHARED" or "SHARED / MANAGEMENT" or "MANAGEMENT" => Shared,
        "UNASSIGNED" => Unassigned,
        _ => null
    };
}

public sealed class UserEmploymentHistory
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public required string PreviousEmploymentFacility { get; set; }
    public required string EmploymentFacility { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public int? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class UserPageAccess
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public required string AreaKey { get; set; }
    public required string AccessLevel { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserGoogleCredential
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Provider { get; set; } = "Google";
    public string? AccessTokenEncrypted { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public string Scope { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
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
