using Microsoft.AspNetCore.Identity;

namespace Qwader.Domain.Identity;

public class AppUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}

public class AppRole : IdentityRole<int>
{
    /// <summary>Arabic display name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>System roles cannot be deleted (their permissions can still be edited, except the administrator's).</summary>
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}

public class AppUserRole : IdentityUserRole<int>
{
    public AppUser User { get; set; } = null!;
    public AppRole Role { get; set; } = null!;
}

public class RolePermission
{
    public int RoleId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public AppRole Role { get; set; } = null!;
}

public class RefreshToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    /// <summary>SHA-256 of the token; the raw token is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }

    public AppUser User { get; set; } = null!;

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
