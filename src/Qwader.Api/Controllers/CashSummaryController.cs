using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Abstractions;
using Qwader.Application.Reports;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

/// <summary>«حركة الفلوس» on the home screen: behind its own permission (the administrator's only, unless ticked for a role).</summary>
[ApiController]
[Route("api/reports/cash-summary")]
[HasPermission(Permissions.DashboardCashFlow)]
public sealed class CashSummaryController(IReportService reports, IClock clock) : ControllerBase
{
    [HttpGet]
    public Task<CashSummaryDto> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var end = to ?? clock.LocalToday;
        return reports.CashSummaryAsync(from ?? end, end, ct);
    }
}
