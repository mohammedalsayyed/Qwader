using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Qwader.Api.Authorization;
using Qwader.Application.Lookups;
using Qwader.Application.Settings;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settings) : ControllerBase
{
    /// <summary>Readable by every signed-in user: store name/address are printed on invoices and vouchers.</summary>
    [HttpGet]
    public Task<SettingsDto> Get(CancellationToken ct) => settings.GetAsync(ct);

    [HttpPut]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SettingsDto> Update(SettingsDto request, CancellationToken ct) => settings.UpdateAsync(request, ct);

    /// <summary>Wipes data for a fresh start (the service also requires the Admin role and the caller's password).</summary>
    [HttpPost("reset")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SystemResetResult> Reset(SystemResetRequest request, [FromServices] ISystemResetService reset, CancellationToken ct) =>
        reset.ResetAsync(request, ct);
}

[ApiController]
[Route("api/audit-log")]
[HasPermission(Permissions.AuditView)]
public sealed class AuditLogController(IAuditLogService audit) : ControllerBase
{
    [HttpGet]
    public Task<Qwader.Application.Common.PagedResult<AuditLogDto>> List([FromQuery] AuditLogRequest request, CancellationToken ct) => audit.ListAsync(request, ct);
}

[ApiController]
[Route("api/lookups")]
public sealed class LookupsController(ILookupService lookups) : ControllerBase
{
    [HttpGet]
    public Task<LookupsDto> Get(CancellationToken ct) => lookups.GetAsync(ct);
}

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbOk = await db.Database.CanConnectAsync(ct);
        var pending = dbOk ? (await db.Database.GetPendingMigrationsAsync(ct)).Count() : -1;
        return dbOk && pending == 0
            ? Ok(new { status = "ok" })
            : StatusCode(503, new { status = "unavailable", database = dbOk, pendingMigrations = pending });
    }
}
