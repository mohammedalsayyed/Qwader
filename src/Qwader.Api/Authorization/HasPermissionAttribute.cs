using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Qwader.Domain.Identity;

namespace Qwader.Api.Authorization;

/// <summary>Requires the caller's token to carry at least one of the given permission claims.</summary>
public sealed class HasPermissionAttribute(params string[] anyOf) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + string.Join(',', anyOf));

/// <summary>Builds "perm:A,B" policies on demand; every other policy name falls through to the default provider.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return _fallback.GetPolicyAsync(policyName);

        var codes = policyName[Prefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => codes.Any(code => ctx.User.HasClaim(Permissions.ClaimType, code)))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
