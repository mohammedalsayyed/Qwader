using FluentValidation;

namespace Qwader.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CurrentUserDto(
    int Id,
    string UserName,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, CurrentUserDto User);

/// <summary>Result of a successful login/refresh. The refresh token travels in an httpOnly cookie, never in the body.</summary>
public sealed record AuthResult(AuthResponse Response, string RefreshToken, DateTime RefreshTokenExpiresAt);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentUserAsync(int userId, CancellationToken ct = default);
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);
}

public static class PasswordRules
{
    public const int MinLength = 8;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("كلمة المرور مطلوبة")
            .MinimumLength(MinLength).WithMessage($"كلمة المرور يجب ألا تقل عن {MinLength} أحرف")
            .Matches("[0-9]").WithMessage("كلمة المرور يجب أن تحتوي على رقم واحد على الأقل")
            .Matches(@"[^\d\s]").WithMessage("كلمة المرور يجب أن تحتوي على حرف واحد على الأقل");
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("اسم المستخدم مطلوب");
        RuleFor(x => x.Password).NotEmpty().WithMessage("كلمة المرور مطلوبة");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("كلمة المرور الحالية مطلوبة");
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("كلمة المرور الجديدة يجب أن تختلف عن الحالية");
    }
}
