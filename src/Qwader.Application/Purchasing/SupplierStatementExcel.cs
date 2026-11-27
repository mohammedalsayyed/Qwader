using Qwader.Application.Abstractions;
using Qwader.Application.Settings;

namespace Qwader.Application.Purchasing;

public interface ISupplierStatementExcel
{
    /// <summary>
    /// The supplier's report (<see cref="ISupplierReportService"/>) as Excel: several sheets — «الملخص», «فواتير الشراء» (the list, then
    /// each invoice in its own section: صنف | عدد | سعر | إجمالي), «كشف الحساب», «الدفعات» — or everything on one printed sheet
    /// under the store's name (<paramref name="singleSheet"/>).
    /// </summary>
    Task<(byte[] File, string FileName)> BuildAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default, bool singleSheet = false);
}

public sealed class SupplierStatementExcel(ISupplierReportService reports, ISettingsService settings, IExcelService excel, IClock clock) : ISupplierStatementExcel
{
    public async Task<(byte[] File, string FileName)> BuildAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default, bool singleSheet = false)
    {
        var r = await reports.ReportAsync(supplierId, from, to, ct);
        var period = r.From is null ? $"من البداية حتى {r.To:yyyy-MM-dd}" : $"من {r.From:yyyy-MM-dd} حتى {r.To:yyyy-MM-dd}";
        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(clock.ToLocal(utc));
        var posted = r.Posted.ToList();
        var counted = r.Payments.Where(p => !p.Voided).ToList();

        List<ExcelRow> Summary()
        {
            var rows = new List<ExcelRow> { ExcelRow.Header("البند", "المبلغ (₪)") };
            if (r.From is not null) rows.Add(ExcelRow.Data("الرصيد قبل الفترة", r.OpeningBalance));
            if (r.OldBalance != 0) rows.Add(ExcelRow.Data("رصيد سابق (قبل النظام)", r.OldBalance));
            rows.Add(ExcelRow.Data("المشتريات (فواتير الشراء)", r.Bought));
            rows.Add(ExcelRow.Data("المردودات للمورد", -r.Returned));
            rows.Add(ExcelRow.Data("المدفوع للمورد", -r.Paid));
            if (r.Refunded != 0) rows.Add(ExcelRow.Data("استرداد من المورد", r.Refunded));
            rows.Add(ExcelRow.Highlight(r.Closing >= 0 ? "الباقي له علينا" : "لنا عنده (دفعنا أكثر)", Math.Abs(r.Closing)));
            return rows;
        }

        List<ExcelRow> InvoiceList()
        {
            if (r.Invoices.Count == 0) return [ExcelRow.Note("لا توجد فواتير شراء في هذه الفترة.")];
            var rows = new List<ExcelRow> { ExcelRow.Header("الفاتورة", "التاريخ", "رقم فاتورة المورد", "الإجمالي", "مدفوع عند الشراء") };
            rows.AddRange(r.Invoices.Select(i => i.Voided
                ? ExcelRow.Data($"{i.Number} (ملغاة)", Day(i.Date), i.SupplierInvoiceNo, null, null)
                : ExcelRow.Data(i.Number, Day(i.Date), i.SupplierInvoiceNo, i.Total, i.PaidAmount == 0 ? null : i.PaidAmount)));
            rows.Add(ExcelRow.Total($"المجموع ({posted.Count} فاتورة)", null, null, posted.Sum(i => i.Total), posted.Sum(i => i.PaidAmount)));
            return rows;
        }

        List<ExcelRow> Invoice(SupplierReportInvoice inv)
        {
            var rows = new List<ExcelRow>
            {
                ExcelRow.Title($"فاتورة {inv.Number} — {Day(inv.Date):yyyy-MM-dd}"
                    + (string.IsNullOrWhiteSpace(inv.SupplierInvoiceNo) ? "" : $" — فاتورة المورد {inv.SupplierInvoiceNo}") + (inv.Voided ? " — ملغاة" : "")),
            };
            if (inv.Voided) rows.Add(ExcelRow.Note($"ملغاة ولا تُحسب في المجاميع{(string.IsNullOrWhiteSpace(inv.VoidReason) ? "" : $": {inv.VoidReason}")}"));
            rows.Add(ExcelRow.Header("الصنف", "العدد", "السعر", "الإجمالي"));
            rows.AddRange(inv.Lines.Select(l => ExcelRow.Data(
                l.Name + (string.IsNullOrWhiteSpace(l.Sku) ? "" : $" ({l.Sku})") + $" — {l.Unit}" + (l.LineDiscount == 0 ? "" : $" — خصم {l.LineDiscount:0.##}"),
                l.Quantity, l.UnitCost, l.LineTotal)));
            rows.Add(ExcelRow.Total("المجموع", inv.Lines.Sum(l => l.Quantity), null, inv.Subtotal));
            if (inv.Discount != 0)
            {
                rows.Add(ExcelRow.Data(inv.DiscountIsIncome ? "خصم على الفاتورة (خصم مكتسب)" : "خصم على الفاتورة", null, null, -inv.Discount));
                rows.Add(ExcelRow.Total("إجمالي الفاتورة", null, null, inv.Total));
            }
            rows.AddRange(inv.PaidWhenBuying.Select(p => ExcelRow.Data($"مدفوع عند الشراء من {p.Account}", null, null, p.Amount)));
            rows.AddRange(inv.Returns.Select(x => ExcelRow.Data($"مردود {x.Number} بتاريخ {Day(x.Date):yyyy-MM-dd}", null, null, -x.Total)));
            rows.AddRange(inv.Expenses.Select(e => ExcelRow.Data(
                $"سند صرف {e.Number} مرتبط بالفاتورة — {e.Category}: {e.Description} (من {e.Account}){(e.Voided ? " — ملغى" : "")}", null, null, e.Voided ? null : e.Amount)));
            return rows;
        }

        List<ExcelRow> Payments()
        {
            if (r.Payments.Count == 0) return [ExcelRow.Note("لا توجد دفعات في هذه الفترة.")];
            var rows = new List<ExcelRow> { ExcelRow.Header("التاريخ", "المستند", "النوع", "من حساب", "المبلغ") };
            rows.AddRange(r.Payments.Select(p => ExcelRow.Data(Day(p.Date), p.Number,
                p.Kind + (p.Voided ? " (ملغى)" : "") + (p.Notes is null ? "" : $" — {p.Notes}"), p.Account, p.Voided ? null : p.Amount)));
            rows.Add(ExcelRow.Total("مجموع الدفعات", null, null, null, counted.Sum(p => p.Amount)));
            return rows;
        }

        List<ExcelRow> Ledger()
        {
            var rows = new List<ExcelRow>
            {
                ExcelRow.Header("البيان", "التاريخ", "علينا (مشتريات)", "دفعنا / رجّعنا", "الرصيد"),
                ExcelRow.Data("الرصيد قبل الفترة", null, null, null, r.Statement.OpeningBalance),
            };
            rows.AddRange(r.Statement.Lines.Select(l => ExcelRow.Data(
                l.Type + (string.IsNullOrWhiteSpace(l.Reference) ? "" : $" {l.Reference}") + (string.IsNullOrWhiteSpace(l.Description) ? "" : $" — {l.Description}"),
                Day(l.Date), l.Credit == 0 ? null : l.Credit, l.Debit == 0 ? null : l.Debit, l.Balance)));
            rows.Add(ExcelRow.Total("المجموع", null, r.Statement.TotalCredit, r.Statement.TotalDebit, r.Closing));
            return rows;
        }

        List<ExcelRow> Heading(string title)
        {
            var rows = new List<ExcelRow> { ExcelRow.Title(title), ExcelRow.Data("المورد", r.Name), ExcelRow.Data("رقم المورد", r.Code) };
            if (!string.IsNullOrWhiteSpace(r.Phone)) rows.Add(ExcelRow.Data("الجوال", r.Phone));
            rows.Add(ExcelRow.Data("الفترة", period));
            rows.Add(ExcelRow.Data("تاريخ الكشف", $"{clock.ToLocal(clock.UtcNow):yyyy-MM-dd HH:mm}"));
            rows.Add(ExcelRow.Blank);
            return rows;
        }
        const string closingNote = "الرصيد الموجب = علينا للمورد، والسالب = لنا عنده. الفواتير والسندات الملغاة لا تُحسب في المجاميع.";

        if (singleSheet)
        {
            // One printed sheet: the store's name on top, then the summary, the invoices list, each invoice, the payments and the statement.
            var store = await settings.GetAsync(ct);
            var rows = new List<ExcelRow> { ExcelRow.Brand(string.IsNullOrWhiteSpace(store.StoreName) ? "معرض قويدر" : store.StoreName) };
            var contact = string.Join(" · ", new[] { store.StoreAddress, store.StorePhone }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (contact.Length > 0) rows.Add(ExcelRow.Subtitle(contact));
            rows.Add(ExcelRow.Blank);
            rows.AddRange(Heading($"كشف حساب مورد — {r.Name}"));
            rows.Add(ExcelRow.Section("الملخص"));
            rows.AddRange(Summary());
            rows.Add(ExcelRow.Blank);
            rows.Add(ExcelRow.Section($"فواتير الشراء ({posted.Count})"));
            rows.AddRange(InvoiceList());
            rows.Add(ExcelRow.Blank);
            foreach (var inv in r.Invoices) { rows.AddRange(Invoice(inv)); rows.Add(ExcelRow.Blank); }
            rows.Add(ExcelRow.Section("الدفعات للمورد"));
            rows.AddRange(Payments());
            rows.Add(ExcelRow.Blank);
            rows.Add(ExcelRow.Section("كشف الحساب (كل الحركات)"));
            rows.AddRange(Ledger());
            rows.Add(ExcelRow.Blank);
            rows.Add(ExcelRow.Note(closingNote));
            return (excel.WriteSheets([new ExcelSheet("كشف المورد", [48, 14, 18, 18, 16], rows, Report: true)]),
                $"supplier-{r.Code}-{r.To:yyyyMMdd}-sheet.xlsx");
        }

        var summary = Heading($"كشف حساب المورد: {r.Name}");
        summary.AddRange(Summary());
        summary.Add(ExcelRow.Blank);
        summary.Add(ExcelRow.Note("التفاصيل في الأوراق: «فواتير الشراء» (قائمة الفواتير ثم كل فاتورة في قسم)، «كشف الحساب»، «الدفعات». " + closingNote));
        var invoiceRows = new List<ExcelRow> { ExcelRow.Title($"فواتير الشراء من {r.Name} — {period}") };
        invoiceRows.AddRange(InvoiceList());
        invoiceRows.Add(ExcelRow.Blank);
        foreach (var inv in r.Invoices) { invoiceRows.AddRange(Invoice(inv)); invoiceRows.Add(ExcelRow.Blank); }
        var ledger = new List<ExcelRow> { ExcelRow.Title($"كشف حساب المورد: {r.Name} — {period}") };
        ledger.AddRange(Ledger());
        var payments = new List<ExcelRow> { ExcelRow.Title($"الدفعات للمورد: {r.Name} — {period}") };
        payments.AddRange(Payments());

        return (excel.WriteSheets(
        [
            new ExcelSheet("الملخص", [34, 22], summary),
            new ExcelSheet("فواتير الشراء", [48, 14, 18, 16, 18], invoiceRows),
            new ExcelSheet("كشف الحساب", [48, 14, 18, 18, 16], ledger),
            new ExcelSheet("الدفعات", [14, 20, 40, 20, 16], payments),
        ]), $"supplier-{r.Code}-{r.To:yyyyMMdd}.xlsx");
    }
}
