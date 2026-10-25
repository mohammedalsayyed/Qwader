using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Identity;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        b.Property(u => u.PhoneNumber).HasMaxLength(20);
        b.HasMany(u => u.UserRoles).WithOne(ur => ur.User).HasForeignKey(ur => ur.UserId).IsRequired();
    }
}

public sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> b)
    {
        b.Property(r => r.DisplayName).HasMaxLength(50).IsRequired();
        b.Property(r => r.Description).HasMaxLength(200);
        b.HasMany(r => r.UserRoles).WithOne(ur => ur.Role).HasForeignKey(ur => ur.RoleId).IsRequired();
        b.HasMany(r => r.Permissions).WithOne(p => p.Role).HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(p => new { p.RoleId, p.PermissionCode });
        b.Property(p => p.PermissionCode).HasMaxLength(50).IsUnicode(false);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(t => t.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(t => t.ReplacedByHash).HasMaxLength(64).IsUnicode(false);
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.UserId);
        b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
