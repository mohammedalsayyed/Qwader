using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Common;
using Qwader.Application.Users;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/users")]
[HasPermission(Permissions.UsersManage)]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserDto>> List([FromQuery] UserListRequest request, CancellationToken ct) => users.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<UserDto> Get(int id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:int}")]
    public Task<UserDto> Update(int id, UpdateUserRequest request, CancellationToken ct) => users.UpdateAsync(id, request, ct);

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/roles")]
[HasPermission(Permissions.UsersManage)]
public sealed class RolesController(IRoleService roles) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => roles.ListAsync(ct);

    [HttpGet("permissions")]
    public IReadOnlyList<PermissionGroupDto> PermissionCatalog() => roles.PermissionCatalog();

    [HttpPost]
    public Task<RoleDto> Create(SaveRoleRequest request, CancellationToken ct) => roles.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    public Task<RoleDto> Update(int id, SaveRoleRequest request, CancellationToken ct) => roles.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, ct);
        return NoContent();
    }
}
