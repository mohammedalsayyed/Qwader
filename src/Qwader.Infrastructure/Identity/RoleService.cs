using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Users;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Identity;

public sealed class RoleService(AppDbContext db, IAuditService audit) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default) =>
        await db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Id)
            .Select(r => new RoleDto(
                r.Id, r.Name!, r.DisplayName, r.Description, r.IsSystem,
                r.Permissions.Select(p => p.PermissionCode).ToList(),
                r.UserRoles.Count(ur => ur.User.IsActive)))
            .ToListAsync(ct);

    public IReadOnlyList<PermissionGroupDto> PermissionCatalog() =>
        Permissions.All.GroupBy(p => p.Group).Select(g => new PermissionGroupDto(g.Key, g.ToList())).ToList();

    public async Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken ct = default)
    {
        var displayName = request.DisplayName.Trim();
        if (await db.Roles.AnyAsync(r => r.DisplayName == displayName, ct))
            throw new ConflictException("يوجد دور بنفس الاسم");

        var role = new AppRole
        {
            // Internal name is technical; users only ever see DisplayName.
            Name = "Custom_" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = displayName,
            Description = request.Description?.Trim(),
            IsSystem = false,
        };
        role.NormalizedName = role.Name.ToUpperInvariant();
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        foreach (var code in request.Permissions.Distinct()) role.Permissions.Add(new RolePermission { PermissionCode = code });

        db.Roles.Add(role);
        audit.Log("Role.Create", "Role", null, null, request);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(r => r.Id == role.Id);
    }

    public async Task<RoleDto> UpdateAsync(int id, SaveRoleRequest request, CancellationToken ct = default)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("الدور غير موجود");

        var displayName = request.DisplayName.Trim();
        if (await db.Roles.AnyAsync(r => r.Id != id && r.DisplayName == displayName, ct))
            throw new ConflictException("يوجد دور بنفس الاسم");

        var requested = request.Permissions.Distinct().ToHashSet();
        if (role.Name == SystemRoles.Admin && !Permissions.All.All(p => requested.Contains(p.Code)))
            throw new BusinessRuleException("لا يمكن تقليص صلاحيات دور مدير النظام");

        var before = new { role.DisplayName, role.Description, Permissions = role.Permissions.Select(p => p.PermissionCode).Order().ToArray() };

        role.DisplayName = displayName;
        role.Description = request.Description?.Trim();
        db.RolePermissions.RemoveRange(role.Permissions.Where(p => !requested.Contains(p.PermissionCode)));
        foreach (var code in requested.Where(c => role.Permissions.All(p => p.PermissionCode != c)))
            db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionCode = code });

        audit.Log("Role.Update", "Role", id.ToString(), before, new { role.DisplayName, role.Description, Permissions = requested.Order().ToArray() });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(r => r.Id == id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("الدور غير موجود");
        if (role.IsSystem) throw new BusinessRuleException("لا يمكن حذف الأدوار الأساسية للنظام");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id, ct))
            throw new BusinessRuleException("لا يمكن حذف دور مرتبط بمستخدمين، انقلهم إلى دور آخر أولًا");

        db.Roles.Remove(role);
        audit.Log("Role.Delete", "Role", id.ToString(), new { role.DisplayName });
        await db.SaveChangesAsync(ct);
    }
}
