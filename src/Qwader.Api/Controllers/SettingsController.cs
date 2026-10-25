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
