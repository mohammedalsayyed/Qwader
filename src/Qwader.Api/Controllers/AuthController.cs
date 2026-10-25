using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qwader.Application.Abstractions;
using Qwader.Application.Auth;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    public const string RefreshCookie = "qwader_rt";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        SetRefreshCookie(result);
        return result.Response;
    }

    /// <summary>Exchanges the refresh cookie for a new access token (and rotates the cookie).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<AuthResponse> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token)) throw new Application.Common.UnauthorizedException("انتهت الجلسة، يرجى تسجيل الدخول مجددًا");

        var result = await auth.RefreshAsync(token, ct);
        SetRefreshCookie(result);
        return result.Response;
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [HttpGet("me")]
    public Task<CurrentUserDto> Me(CancellationToken ct) => auth.GetCurrentUserAsync(currentUser.RequiredUserId, ct);

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(currentUser.RequiredUserId, request, ct);
        return NoContent();
    }

    private void SetRefreshCookie(AuthResult result) =>
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, CookieOptions(result.RefreshTokenExpiresAt));

    private CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
        IsEssential = true,
    };
}
