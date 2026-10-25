using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Qwader.Domain.Identity;

namespace Qwader.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "Qwader";
    public string Audience { get; set; } = "Qwader.Web";

    /// <summary>Supplied through user-secrets / environment (Jwt__SigningKey). Never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    public SymmetricSecurityKey GetKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Set it with user-secrets or the Jwt__SigningKey environment variable.");
        return new SymmetricSecurityKey(bytes);
    }
}

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public const string FullNameClaim = "full_name";

    /// <summary>Present until the user sets their own password; the API then only serves /api/auth.</summary>
    public const string MustChangePasswordClaim = "pwd_change";

    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) CreateAccessToken(AppUser user, IEnumerable<string> roles, IEnumerable<string> permissions, DateTime utcNow)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(FullNameClaim, user.FullName),
        };
        if (user.MustChangePassword) claims.Add(new Claim(MustChangePasswordClaim, "true"));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));

        var expires = utcNow.AddMinutes(_options.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            notBefore: utcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(_options.GetKey(), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string Token, string Hash, DateTime ExpiresAt) CreateRefreshToken(DateTime utcNow)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return (token, Hash(token), utcNow.AddDays(_options.RefreshTokenDays));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
