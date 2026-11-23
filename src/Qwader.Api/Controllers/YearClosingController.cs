using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Settings;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

/// <summary>«إقفال السنة» in the settings (off by default): turn it on or off, close the next year, reopen the last one.</summary>
[ApiController]
[Route("api/year-closing")]
[HasPermission(Permissions.SettingsManage)]
public sealed class YearClosingController(IYearClosingService years) : ControllerBase
{
    [HttpGet]
    public Task<YearClosingStatusDto> Status(CancellationToken ct) => years.StatusAsync(ct);

    [HttpPut("enabled")]
    public Task<YearClosingStatusDto> SetEnabled(SetYearClosingRequest request, CancellationToken ct) => years.SetEnabledAsync(request, ct);

    [HttpPost("close")]
    public Task<YearClosingStatusDto> Close(CloseYearRequest request, CancellationToken ct) => years.CloseAsync(request, ct);

    [HttpPost("{year:int}/reopen")]
    public Task<YearClosingStatusDto> Reopen(int year, ReopenYearRequest request, CancellationToken ct) => years.ReopenAsync(year, request, ct);
}
