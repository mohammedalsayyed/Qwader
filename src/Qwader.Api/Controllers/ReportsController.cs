using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Reports;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/reports")]
[HasPermission(Permissions.ReportsSales, Permissions.ReportsProfit, Permissions.ReportsFinancial)]
public sealed class ReportsController(IReportService reports, IExcelService excel, IClock clock) : ControllerBase
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var end = to ?? clock.LocalToday;
        return (from ?? end, end);
    }

    [HttpGet("period")]
    public Task<PeriodReportDto> PeriodReport([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        return reports.PeriodAsync(f, t, ct);
    }

    [HttpGet("daily")]
    public Task<IReadOnlyList<DailyRowDto>> Daily([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        return reports.DailyAsync(f, t, ct);
    }

    [HttpGet("products")]
    public Task<IReadOnlyList<ProductReportRow>> Products([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] ProductSort sort = ProductSort.Revenue, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var (f, t) = Period(from, to);
        return reports.ProductsAsync(f, t, sort, take, ct);
    }

    [HttpGet("categories")]
    public Task<IReadOnlyList<GroupReportRow>> Categories([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        return reports.CategoriesAsync(f, t, ct);
    }

    [HttpGet("salespeople")]
    public Task<IReadOnlyList<GroupReportRow>> Salespeople([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        return reports.SalespeopleAsync(f, t, ct);
    }

    [HttpGet("receivables")]
    [HasPermission(Permissions.ReportsFinancial)]
    public Task<IReadOnlyList<BalanceRow>> Receivables(CancellationToken ct) => reports.ReceivablesAsync(ct);

    [HttpGet("payables")]
    [HasPermission(Permissions.ReportsFinancial)]
    public Task<IReadOnlyList<BalanceRow>> Payables(CancellationToken ct) => reports.PayablesAsync(ct);

    [HttpGet("inventory-value")]
    public Task<IReadOnlyList<InventoryValueRow>> InventoryValue(CancellationToken ct) => reports.InventoryValueAsync(ct);

    /// <summary>Excel (right-to-left, Arabic headers) for any report on the page.</summary>
    [HttpGet("{report}/export")]
    public async Task<IActionResult> Export(string report, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        var name = $"{report}-{f:yyyyMMdd}-{t:yyyyMMdd}.xlsx";
        var financial = User.HasClaim(Permissions.ClaimType, Permissions.ReportsFinancial);
        byte[] file;
        switch (report)
        {
            case "summary":
            {
                var r = await reports.PeriodAsync(f, t, ct);
                var rows = new List<object?[]>
                {
                    new object?[] { "الفترة", $"{f:yyyy-MM-dd} — {t:yyyy-MM-dd}" },
                    new object?[] { "عدد الفواتير", r.InvoiceCount },
                    new object?[] { "إجمالي المبيعات", r.GrossSales },
                    new object?[] { "الخصومات", r.Discounts },
                    new object?[] { "مردودات المبيعات", r.Returns },
                    new object?[] { "المبيعات (الإيرادات)", r.NetSales },
                    new object?[] { "تكلفة البضاعة المباعة (سعر الشراء)", r.CostOfSales },
                    new object?[] { "إجمالي الربح", r.GrossProfit },
                    new object?[] { "المصروفات (رواتب، أعطال، مصاريف تشغيلية)", r.TotalExpenses },
                    new object?[] { "صافي الربح (إجمالي الربح − المصروفات)", r.NetOperatingProfit },
                    new object?[] { "رأس المال: قيمة المخزون الآن (بالتكلفة)", r.InventoryValue },
                    new object?[] { "رأس المال: نقص بالتالف والمفقود خلال الفترة", r.InventoryLosses },
                    new object?[] { "رأس المال: زيادة بالجرد خلال الفترة", r.InventoryGains },
                    new object?[] { "الأموال الداخلة", r.CashIn },
                    new object?[] { "المدفوعات", r.CashOut },
                    new object?[] { "صافي التدفق النقدي", r.NetCashFlow },
                    new object?[] { "ديون جديدة", r.NewDebt },
                    new object?[] { "تحصيلات", r.Collections },
                    new object?[] { "ذمم العملاء (حاليًا)", r.Receivables },
                    new object?[] { "مستحق للموردين (حاليًا)", r.Payables },
                    new object?[] { "قيمة المخزون بالتكلفة (حاليًا)", r.InventoryValue },
                };
                rows.AddRange((r.ExpensesByCategory ?? []).Select(e => new object?[] { "مصروف: " + e.Name, e.Amount }));
                rows.AddRange((r.Accounts ?? []).Select(a => new object?[] { "رصيد نهاية الفترة: " + a.Name, a.ClosingBalance }));
                file = excel.Write("ملخص", [new("البند", 34), new("القيمة", 18)], rows.Where(x => x[1] is not null));
                break;
            }
            case "daily":
                file = excel.Write("يومي", [new("اليوم", 14), new("الفواتير", 10), new("صافي المبيعات", 16), new("إجمالي الربح", 16), new("المصروفات", 14), new("المقبوض", 14)],
                    (await reports.DailyAsync(f, t, ct)).Select(d => new object?[] { d.Day.ToString("yyyy-MM-dd"), d.Invoices, d.NetSales, d.GrossProfit, d.Expenses, d.Received }));
                break;
            case "products":
                file = excel.Write("المنتجات", [new("الكود", 12), new("المنتج", 30), new("الصنف", 18), new("الكمية", 10), new("الإيراد", 14), new("التكلفة", 14), new("الربح", 14), new("نسبة الربح %", 12)],
                    (await reports.ProductsAsync(f, t, ProductSort.Revenue, 500, ct)).Select(p => new object?[] { p.Sku, p.Name, p.CategoryName, p.Quantity, p.Revenue, p.Cost, p.Profit, p.MarginPercent }));
                break;
            case "categories":
                file = excel.Write("الأصناف", [new("الصنف", 24), new("عدد المنتجات", 12), new("الكمية", 10), new("الإيراد", 14), new("التكلفة", 14), new("الربح", 14)],
                    (await reports.CategoriesAsync(f, t, ct)).Select(c => new object?[] { c.Name, c.Count, c.Quantity, c.Revenue, c.Cost, c.Profit }));
                break;
            case "salespeople":
                file = excel.Write("الموظفون", [new("الموظف", 24), new("الفواتير", 10), new("الكمية", 10), new("المبيعات", 14), new("الربح", 14)],
                    (await reports.SalespeopleAsync(f, t, ct)).Select(c => new object?[] { c.Name, c.Count, c.Quantity, c.Revenue, c.Profit }));
                break;
            case "receivables":
                if (!financial) return Forbid();
                file = excel.Write("ذمم العملاء", [new("الكود", 10), new("العميل", 26), new("الجوال", 14), new("الرصيد", 14), new("آخر حركة", 18)],
                    (await reports.ReceivablesAsync(ct)).Select(b => new object?[] { b.Code, b.Name, b.Phone, b.Balance, b.LastActivity }));
                break;
            case "payables":
                if (!financial) return Forbid();
                file = excel.Write("مستحقات الموردين", [new("الكود", 10), new("المورد", 26), new("الجوال", 14), new("المستحق", 14), new("آخر حركة", 18)],
                    (await reports.PayablesAsync(ct)).Select(b => new object?[] { b.Code, b.Name, b.Phone, b.Balance, b.LastActivity }));
                break;
            case "inventory":
                file = excel.Write("قيمة المخزون", [new("الصنف", 24), new("عدد المنتجات", 12), new("الكمية", 12), new("القيمة بالتكلفة", 16)],
                    (await reports.InventoryValueAsync(ct)).Select(c => new object?[] { c.CategoryName, c.ProductCount, c.Quantity, c.Value }));
                break;
            default:
                throw new NotFoundException("التقرير غير موجود");
        }
        return File(file, XlsxType, name);
    }
}
