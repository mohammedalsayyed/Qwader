using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Abstractions;
using Qwader.Application.Reports;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

/// <summary>«تقرير فواتير المبيعات»: behind its own permission «تقارير الفواتير», not the other reports'.</summary>
[ApiController]
[Route("api/reports/invoices")]
[HasPermission(Permissions.ReportsInvoices)]
public sealed class InvoicesReportController(IReportService reports, IExcelService excel, IClock clock) : ControllerBase
{
    private (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var end = to ?? clock.LocalToday;
        return (from ?? end, end);
    }

    /// <summary>Every sale invoice of the period — paid, where to, owed, and its profit with «عرض الأرباح».</summary>
    [HttpGet]
    public Task<InvoiceReportDto> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] InvoiceReportFilter filter = InvoiceReportFilter.All,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var (f, t) = Period(from, to);
        return reports.InvoicesAsync(f, t, filter, search, page, Math.Clamp(pageSize, 10, 200), ct);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] InvoiceReportFilter filter = InvoiceReportFilter.All,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        // Every invoice the screen's filter and search keep, not one page.
        var (f, t) = Period(from, to);
        var r = await reports.InvoicesAsync(f, t, filter, search, pageSize: 0, ct: ct);
        var withProfit = r.Profit is not null;
        var columns = new List<ExcelColumn> { new("رقم الفاتورة", 18), new("التاريخ", 18), new("العميل", 24), new("الموظف", 18), new("الإجمالي", 12),
            new("المرتجع", 12), new("المدفوع", 12), new("المتبقي", 12), new("الحالة", 14), new("وين انحوّلت", 40) };
        if (withProfit) columns.Add(new("الربح", 12));
        var lines = r.Rows.Select(x =>
        {
            var row = new List<object?> { x.Number, x.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), x.CustomerName, x.Salesperson, x.Total, x.Returned, x.Paid,
                x.Remaining, x.Status, string.Join(" + ", x.Payments.Select(p => $"{p.Account}: {p.Amount:0.00}")) };
            if (withProfit) row.Add(x.Profit);
            return row.ToArray();
        }).ToList();
        var all = r.Matched == r.Count;
        var total = new List<object?> { $"المجموع ({r.Matched} فاتورة)", null, null, null, r.MatchedTotal, r.MatchedReturned, r.MatchedPaid, r.MatchedRemaining, null,
            all ? string.Join(" + ", r.ByAccount.Select(p => $"{p.Account}: {p.Amount:0.00}")) : null };
        if (withProfit) total.Add(r.MatchedProfit);
        lines.Add(total.ToArray());
        var file = excel.Write("فواتير المبيعات", columns, lines);
        return File(file, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"invoices-{f:yyyyMMdd}-{t:yyyyMMdd}.xlsx");
    }
}
