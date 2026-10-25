using System.IdentityModel.Tokens.Jwt;
using Qwader.Application.Abstractions;
using Qwader.Domain.Identity;

namespace Qwader.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? UserId =>
        int.TryParse(Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public string? UserName => Principal?.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool HasPermission(string permission) => Principal?.HasClaim(Permissions.ClaimType, permission) == true;
}
