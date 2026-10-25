using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Auth;
using Qwader.Application.Common;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Identity;

public sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    JwtTokenService tokens,
    IClock clock,
    IAuditService audit) : IAuthService
{
    private const string InvalidCredentials = "اسم المستخدم أو كلمة المرور غير صحيحة";
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(60);

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByNameAsync(request.UserName.Trim());
        if (user is null) throw new UnauthorizedException(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            throw new UnauthorizedException("تم إيقاف الدخول مؤقتًا بسبب محاولات خاطئة متكررة، حاول بعد بضع دقائق");

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive) throw new UnauthorizedException("هذا الحساب معطّل، راجع مدير النظام");

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.UtcNow;
        audit.Log("Auth.Login", "User", user.Id.ToString());
        return await IssueAsync(user, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) throw new UnauthorizedException("انتهت الجلسة، يرجى تسجيل الدخول مجددًا");

        var now = clock.UtcNow;
        // Two tabs refreshing at the same moment present the same token; the loser just retries with the new cookie.
        if (stored.RevokedAt is { } revokedAt && stored.ReplacedByHash is not null && now - revokedAt < RotationGrace)
            throw new UnauthorizedException("انتهت الجلسة، يرجى تسجيل الدخول مجددًا");

        if (stored.RevokedAt is not null)
        {
            // A rotated token was presented again: assume it leaked and end every session of this user.
            await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            throw new UnauthorizedException("انتهت الجلسة، يرجى تسجيل الدخول مجددًا");
        }

        if (!stored.IsActive(now) || !stored.User.IsActive)
            throw new UnauthorizedException("انتهت الجلسة، يرجى تسجيل الدخول مجددًا");

        var result = await IssueAsync(stored.User, ct, rotated: stored);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = JwtTokenService.Hash(refreshToken);
        var now = clock.UtcNow;
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException();
        var (roles, permissions) = await LoadAccessAsync(userId, ct);
        return ToDto(user, roles, permissions);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
                throw new ValidationFailedException(nameof(request.CurrentPassword), "كلمة المرور الحالية غير صحيحة");
            throw IdentityErrors.ToException(result, nameof(request.NewPassword));
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        audit.Log("Auth.ChangePassword", "User", user.Id.ToString());
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResult> IssueAsync(AppUser user, CancellationToken ct, RefreshToken? rotated = null)
    {
        var now = clock.UtcNow;
        var (roles, permissions) = await LoadAccessAsync(user.Id, ct);
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user, roles, permissions, now);
        var (refreshToken, refreshHash, refreshExpires) = tokens.CreateRefreshToken(now);

        if (rotated is not null)
        {
            rotated.RevokedAt = now;
            rotated.ReplacedByHash = refreshHash;
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = refreshExpires,
        });

        // Housekeeping: drop this user's long-dead tokens.
        var cutoff = now.AddDays(-30);
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);

        return new AuthResult(new AuthResponse(accessToken, accessExpires, ToDto(user, roles, permissions)), refreshToken, refreshExpires);
    }

    private async Task<(List<string> Roles, List<string> Permissions)> LoadAccessAsync(int userId, CancellationToken ct)
    {
        var roleIds = db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId);
        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name!).ToListAsync(ct);
        var permissions = await db.RolePermissions.Where(p => roleIds.Contains(p.RoleId))
            .Select(p => p.PermissionCode).Distinct().ToListAsync(ct);
        return (roles, permissions);
    }

    private static CurrentUserDto ToDto(AppUser user, List<string> roles, List<string> permissions) =>
        new(user.Id, user.UserName!, user.FullName, roles, permissions.Order().ToList(), user.MustChangePassword);
}

internal static class IdentityErrors
{
    public static ValidationFailedException ToException(IdentityResult result, string field)
    {
        var messages = result.Errors.Select(e => e.Code switch
        {
            nameof(IdentityErrorDescriber.DuplicateUserName) => "اسم المستخدم مستخدم من قبل",
            nameof(IdentityErrorDescriber.InvalidUserName) => "اسم المستخدم غير صالح",
            nameof(IdentityErrorDescriber.PasswordTooShort) => "كلمة المرور قصيرة جدًا",
            nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "كلمة المرور يجب أن تحتوي على رقم",
            nameof(IdentityErrorDescriber.PasswordMismatch) => "كلمة المرور غير صحيحة",
            _ => "تعذر حفظ البيانات: " + e.Code,
        }).Distinct().ToArray();
        return new ValidationFailedException(new Dictionary<string, string[]> { [field] = messages });
    }
}
