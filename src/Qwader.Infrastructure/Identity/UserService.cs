using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Users;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Identity;

public sealed class UserService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit) : IUserService
{
    public async Task<PagedResult<UserDto>> ListAsync(UserListRequest request, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();
        if (request.IsActive is { } active) query = query.Where(u => u.IsActive == active);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            query = query.Where(u => u.UserName!.Contains(s) || u.FullName.Contains(s) || u.PhoneNumber!.Contains(s));
        }

        return await query.OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName)
            .Select(Projection)
            .ToPagedAsync(request, ct);
    }

    public async Task<UserDto> GetAsync(int id, CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("المستخدم غير موجود");

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var roles = await ResolveRolesAsync(request.Roles, ct);

        var user = new AppUser
        {
            UserName = request.UserName.Trim(),
            FullName = request.FullName.Trim(),
            PhoneNumber = NullIfEmpty(request.PhoneNumber),
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = clock.UtcNow,
            LockoutEnabled = true,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var field = result.Errors.Any(e => e.Code.Contains("UserName")) ? nameof(request.UserName) : nameof(request.Password);
            throw IdentityErrors.ToException(result, field);
        }

        foreach (var role in roles) db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = role.Id });
        audit.Log("User.Create", "User", user.Id.ToString(), null, new { user.UserName, user.FullName, Roles = request.Roles });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("المستخدم غير موجود");
        var roles = await ResolveRolesAsync(request.Roles, ct);
        var before = new { user.FullName, user.PhoneNumber, user.IsActive, Roles = user.UserRoles.Select(r => r.Role.Name).ToArray() };

        var willBeAdmin = request.IsActive && roles.Any(r => r.Name == SystemRoles.Admin);
        if (id == currentUser.UserId && !willBeAdmin && user.UserRoles.Any(r => r.Role.Name == SystemRoles.Admin))
            throw new BusinessRuleException("لا يمكنك إزالة صلاحية مدير النظام عن حسابك أو تعطيله");

        if (!willBeAdmin && user.UserRoles.Any(r => r.Role.Name == SystemRoles.Admin) && user.IsActive)
        {
            var otherActiveAdmins = await db.UserRoles.CountAsync(ur => ur.Role.Name == SystemRoles.Admin && ur.UserId != id && ur.User.IsActive, ct);
            if (otherActiveAdmins == 0) throw new BusinessRuleException("يجب أن يبقى مدير نظام فعّال واحد على الأقل");
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = NullIfEmpty(request.PhoneNumber);

        if (user.IsActive && !request.IsActive)
        {
            // Deactivation ends existing sessions immediately (access tokens expire within minutes).
            var now = clock.UtcNow;
            await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        }
        user.IsActive = request.IsActive;

        db.UserRoles.RemoveRange(user.UserRoles.Where(ur => roles.All(r => r.Id != ur.RoleId)));
        foreach (var role in roles.Where(r => user.UserRoles.All(ur => ur.RoleId != r.Id)))
            db.UserRoles.Add(new AppUserRole { UserId = id, RoleId = role.Id });

        audit.Log("User.Update", "User", id.ToString(), before, new { user.FullName, user.PhoneNumber, user.IsActive, Roles = request.Roles });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("المستخدم غير موجود");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded) throw IdentityErrors.ToException(result, nameof(request.NewPassword));

        user.MustChangePassword = id != currentUser.UserId;
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.UpdateAsync(user);

        var now = clock.UtcNow;
        await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        audit.Log("User.ResetPassword", "User", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<AppRole>> ResolveRolesAsync(string[] names, CancellationToken ct)
    {
        var distinct = names.Distinct().ToArray();
        var roles = await db.Roles.Where(r => distinct.Contains(r.Name!)).ToListAsync(ct);
        if (roles.Count != distinct.Length) throw new ValidationFailedException("Roles", "أحد الأدوار المختارة غير موجود");
        return roles;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static readonly System.Linq.Expressions.Expression<Func<AppUser, UserDto>> Projection = u => new UserDto(
        u.Id,
        u.UserName!,
        u.FullName,
        u.PhoneNumber,
        u.IsActive,
        u.UserRoles.Select(ur => new RoleRefDto(ur.Role.Name!, ur.Role.DisplayName)).ToList(),
        u.CreatedAt,
        u.LastLoginAt);
}
