using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Identity;

/// <summary>
/// A short fingerprint of what a user may do (their roles and permissions), put in their access token. When an administrator
/// changes a role's permissions, a user's roles, or turns a user off, the fingerprint no longer matches what is stored: the
/// server answers the old token with 401 and the app refreshes it at once — the new permissions apply without signing out.
/// </summary>
public static class AccessStamp
{
    public const string Claim = "acc";

    public static string Of(IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var text = string.Join('|', roles.Order(StringComparer.Ordinal)) + '#' + string.Join('|', permissions.Distinct().Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}

/// <summary>The current fingerprint of each user, read from the database at most every few seconds per user.</summary>
public sealed class AccessStampCache(IServiceScopeFactory scopes, IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    public async Task<string> CurrentAsync(int userId, CancellationToken ct)
    {
        if (cache.TryGetValue(("access-stamp", userId), out string? stamp) && stamp is not null) return stamp;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = await db.Users.Where(u => u.Id == userId).Select(u => (bool?)u.IsActive).FirstOrDefaultAsync(ct);
        if (active != true)
            stamp = "inactive";
        else
        {
            var roleIds = db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId);
            var roles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name!).ToListAsync(ct);
            var permissions = await db.RolePermissions.Where(p => roleIds.Contains(p.RoleId)).Select(p => p.PermissionCode).Distinct().ToListAsync(ct);
            stamp = AccessStamp.Of(roles, permissions);
        }
        cache.Set(("access-stamp", userId), stamp, Lifetime);
        return stamp;
    }
}
