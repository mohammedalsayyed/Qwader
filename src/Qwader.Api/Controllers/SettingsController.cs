using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Qwader.Api.Authorization;
using Qwader.Application.Common;
using Qwader.Application.Lookups;
using Qwader.Application.Settings;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settings) : ControllerBase
{
    /// <summary>What this copy allows (from its server settings): the settings screen hides the reset when it is switched off.</summary>
    [HttpGet("system")]
    [HasPermission(Permissions.SettingsManage)]
    public object System([FromServices] IConfiguration config) =>
        new { resetAllowed = !IsOff(config, "System:AllowReset"), integrityCheckAllowed = !IsOff(config, "System:AllowIntegrityCheck") };

    /// <summary>Readable by every signed-in user: store name/address are printed on invoices and vouchers.</summary>
    [HttpGet]
    public Task<SettingsDto> Get(CancellationToken ct) => settings.GetAsync(ct);

    [HttpPut]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SettingsDto> Update(SettingsDto request, CancellationToken ct) => settings.UpdateAsync(request, ct);

    /// <summary>Sets the default «حد التنبيه» on every existing product.</summary>
    [HttpPost("apply-reorder-level")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<object> ApplyReorderLevel(CancellationToken ct) => new { changed = await settings.ApplyDefaultReorderLevelAsync(ct) };

    /// <summary>Recomputes every stored balance from its movements and lists differences. Read-only.</summary>
    [HttpGet("integrity")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<IntegrityReport> Integrity([FromServices] IIntegrityService integrity, [FromServices] IConfiguration config, CancellationToken ct) =>
        IsOff(config, "System:AllowIntegrityCheck")
            ? throw new ForbiddenException("فحص سلامة الحسابات معطّل في هذه النسخة")
            : integrity.CheckAsync(ct);

    private static bool IsOff(IConfiguration config, string key) => string.Equals(config[key], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Deletes the audit log of a period (Admin only, with the caller's password; the deletion itself is logged).</summary>
    [HttpPost("audit-log/delete")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<object> DeleteAuditLog(DeleteAuditLogRequest request, [FromServices] ISystemResetService reset, CancellationToken ct) =>
        new { deleted = await reset.DeleteAuditLogAsync(request, ct) };

    /// <summary>Wipes data for a fresh start (the service also requires the Admin role and the caller's password).</summary>
    [HttpPost("reset")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SystemResetResult> Reset(SystemResetRequest request, [FromServices] ISystemResetService reset, CancellationToken ct) =>
        reset.ResetAsync(request, ct);

    /// <summary>A ZIP of the whole store (Admin only; the service checks the role). Built in a temp file, deleted once sent.</summary>
    [HttpGet("backup")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Backup([FromServices] ISystemBackupService backup, CancellationToken ct)
    {
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await backup.WriteAsync(file, ct);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        file.Position = 0;
        return File(file, "application/zip", backup.FileName());
    }
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

    public sealed record AddUnitRequest(string Name);

    [HttpPost("units")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<LookupItem> AddUnit(AddUnitRequest request, CancellationToken ct) => lookups.AddUnitAsync(request.Name, ct);

    public sealed record SetUnitRequest(bool WholeNumbersOnly);

    [HttpPut("units/{id:int}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<UnitLookupItem> SetUnit(int id, SetUnitRequest request, CancellationToken ct) => lookups.SetUnitWholeNumbersAsync(id, request.WholeNumbersOnly, ct);
}

/// <summary>The version, for signed-in users only (shown under the copyright line); not told to anyone before sign-in.</summary>
[ApiController]
[Route("api/app-info")]
public sealed class AppInfoController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public object Get() => new { version = config["App:Version"] ?? "1.0.0" };
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
