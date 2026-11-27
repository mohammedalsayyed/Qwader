using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Purchasing;

public interface ISupplierStatementExcel
{
    /// <summary>
    /// The supplier's statement as an Excel workbook: «الملخص» (bought, returned, paid, still owed), «كشف الحساب» (every entry
    /// with its running balance), «فواتير الشراء» (the list, then each invoice in its own section: صنف | عدد | سعر | إجمالي) and «الدفعات».
    /// </summary>
    Task<(byte[] File, string FileName)> BuildAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public sealed class SupplierStatementExcel(IAppDbContext db, ISupplierService suppliers, IExcelService excel, IClock clock) : ISupplierStatementExcel
{
    public async Task<(byte[] File, string FileName)> BuildAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw new NotFoundException("المورد غير موجود");
        var statement = await suppliers.StatementAsync(supplierId, from, to, ct);
        var end = statement.To;
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(from ?? new DateOnly(2000, 1, 1), end);
        var period = from is null ? $"من البداية حتى {end:yyyy-MM-dd}" : $"من {from:yyyy-MM-dd} حتى {end:yyyy-MM-dd}";
        DateTime Local(DateTime utc) => clock.ToLocal(utc);

        // ---- totals by kind. A reversal (a voided document) counts against the kind it reverses, so a voided invoice
        // and its reversal cancel out and the totals add up to the balance.
        var all = await db.SupplierLedgerEntries.AsNoTracking().Where(e => e.SupplierId == supplierId)
            .Select(e => new { e.Date, e.Type, e.SourceType, e.SourceId, e.Debit, e.Credit }).ToListAsync(ct);
        SupplierEntryType KindOf(SupplierEntryType type, string sourceType, int? sourceId, bool debit)
        {
            if (type != SupplierEntryType.Reversal) return type;
            // A debit reversal undoes a credit (a purchase, a refund, an old balance); a credit reversal undoes a debit.
            return all.Where(o => o.Type != SupplierEntryType.Reversal && o.SourceType == sourceType && o.SourceId == sourceId && (debit ? o.Credit > 0 : o.Debit > 0))
                .Select(o => o.Type).FirstOrDefault(debit ? SupplierEntryType.Purchase : SupplierEntryType.Payment);
        }
        var inPeriod = all.Where(e => e.Date >= startUtc && e.Date < endUtc)
            .Select(e => new { Kind = KindOf(e.Type, e.SourceType, e.SourceId, e.Debit > 0), Net = e.Credit - e.Debit }).ToList();
        decimal Net(SupplierEntryType kind) => inPeriod.Where(e => e.Kind == kind).Sum(e => e.Net);
        var bought = Net(SupplierEntryType.Purchase);
        var paid = -Net(SupplierEntryType.Payment);
        var returned = -Net(SupplierEntryType.Return);
        var refunded = Net(SupplierEntryType.Refund);
        var oldBalance = Net(SupplierEntryType.Opening);
        var closing = statement.ClosingBalance;

        var summary = new List<ExcelRow>
        {
            ExcelRow.Title($"كشف حساب المورد: {supplier.Name}"),
            ExcelRow.Data("رقم المورد", supplier.Code),
            ExcelRow.Data("الجوال", supplier.Phone),
            ExcelRow.Data("الفترة", period),
            ExcelRow.Data("تاريخ الكشف", Local(clock.UtcNow)),
            ExcelRow.Blank,
            ExcelRow.Header("البند", "المبلغ (₪)"),
        };
        if (from is not null) summary.Add(ExcelRow.Data("الرصيد قبل الفترة", statement.OpeningBalance));
        if (oldBalance != 0) summary.Add(ExcelRow.Data("رصيد سابق (قبل النظام)", oldBalance));
        summary.Add(ExcelRow.Data("المشتريات (فواتير الشراء)", bought));
        summary.Add(ExcelRow.Data("المردودات للمورد", -returned));
        summary.Add(ExcelRow.Data("المدفوع للمورد", -paid));
        if (refunded != 0) summary.Add(ExcelRow.Data("استرداد من المورد", refunded));
        summary.Add(ExcelRow.Total(closing >= 0 ? "الباقي له علينا" : "لنا عنده (دفعنا أكثر)", Math.Abs(closing)));
        summary.Add(ExcelRow.Blank);
        summary.Add(ExcelRow.Note("الفواتير الملغاة لا تُحسب. التفاصيل في الأوراق: «فواتير الشراء» (قائمة الفواتير ثم كل فاتورة في قسم)، «كشف الحساب»، «الدفعات»."));

        // ---- the statement
        var ledger = new List<ExcelRow>
        {
            ExcelRow.Title($"كشف حساب المورد: {supplier.Name} — {period}"),
            ExcelRow.Header("التاريخ", "البيان", "المرجع", "التفاصيل", "علينا (مشتريات)", "دفعنا / رجّعنا", "الرصيد"),
            ExcelRow.Data(null, "الرصيد قبل الفترة", null, null, null, null, statement.OpeningBalance),
        };
        ledger.AddRange(statement.Lines.Select(l =>
            ExcelRow.Data(Local(l.Date), l.Type, l.Reference, l.Description, l.Credit == 0 ? null : l.Credit, l.Debit == 0 ? null : l.Debit, l.Balance)));
        ledger.Add(ExcelRow.Total("المجموع", null, null, null, statement.TotalCredit, statement.TotalDebit, closing));

        // ---- each invoice in its own section
        var invoices = await db.PurchaseInvoices.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.Status != PurchaseStatus.Draft && p.Date >= startUtc && p.Date < endUtc)
            .OrderBy(p => p.Date).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Number, p.Date, p.Status, p.SupplierInvoiceNo, p.Subtotal, p.Discount, p.DiscountIsIncome, p.Total, p.PaidAmount, p.VoidReason,
                OldAccount = p.PaymentAccount != null ? p.PaymentAccount.Name : null,
                Lines = p.Lines.OrderBy(l => l.Id).Select(l => new
                {
                    l.Product.Name, l.Product.Sku, l.Quantity, Unit = l.UnitName ?? l.Product.Unit.Name, l.UnitCost, l.LineDiscount, l.LineTotal, l.ReturnedQuantity,
                }).ToList(),
                Payments = p.Payments.Select(x => new { Account = x.FinancialAccount.Name, x.Amount }).ToList(),
            }).ToListAsync(ct);
        var returns = await db.PurchaseReturns.AsNoTracking().Where(r => r.SupplierId == supplierId)
            .Select(r => new { r.PurchaseInvoiceId, r.Number, r.Date, r.Total }).ToListAsync(ct);

        // «فواتير الشراء»: the list of the period's invoices, then each invoice in its own section: صنف | عدد | سعر | إجمالي.
        var posted = invoices.Where(i => i.Status != PurchaseStatus.Voided).ToList();
        var invoiceRows = new List<ExcelRow> { ExcelRow.Title($"فواتير الشراء من {supplier.Name} — {period}") };
        if (invoices.Count == 0) invoiceRows.Add(ExcelRow.Note("لا توجد فواتير شراء في هذه الفترة."));
        else
        {
            invoiceRows.Add(ExcelRow.Header("الفاتورة", "التاريخ", "رقم فاتورة المورد", "الإجمالي", "مدفوع عند الشراء"));
            invoiceRows.AddRange(invoices.Select(i => i.Status == PurchaseStatus.Voided
                ? ExcelRow.Data($"{i.Number} (ملغاة)", Local(i.Date), i.SupplierInvoiceNo, null, null)
                : ExcelRow.Data(i.Number, Local(i.Date), i.SupplierInvoiceNo, i.Total, i.PaidAmount == 0 ? null : i.PaidAmount)));
            invoiceRows.Add(ExcelRow.Total($"المجموع ({posted.Count} فاتورة)", null, null, posted.Sum(i => i.Total), posted.Sum(i => i.PaidAmount)));
            invoiceRows.Add(ExcelRow.Blank);
        }
        foreach (var inv in invoices)
        {
            var voided = inv.Status == PurchaseStatus.Voided;
            invoiceRows.Add(ExcelRow.Title($"فاتورة {inv.Number} — {Local(inv.Date):yyyy-MM-dd}"
                + (string.IsNullOrWhiteSpace(inv.SupplierInvoiceNo) ? "" : $" — رقم فاتورة المورد {inv.SupplierInvoiceNo}")
                + (voided ? " — ملغاة" : "")));
            if (voided) invoiceRows.Add(ExcelRow.Note($"ملغاة ولا تُحسب في المجاميع{(string.IsNullOrWhiteSpace(inv.VoidReason) ? "" : $": {inv.VoidReason}")}"));
            invoiceRows.Add(ExcelRow.Header("الصنف", "العدد", "السعر", "الإجمالي"));
            invoiceRows.AddRange(inv.Lines.Select(l => ExcelRow.Data(
                l.Name + (string.IsNullOrWhiteSpace(l.Sku) ? "" : $" ({l.Sku})") + $" — {l.Unit}" + (l.LineDiscount == 0 ? "" : $" — خصم {l.LineDiscount:0.##}"),
                l.Quantity, l.UnitCost, l.LineTotal)));
            invoiceRows.Add(ExcelRow.Total("المجموع", inv.Lines.Sum(l => l.Quantity), null, inv.Subtotal));
            if (inv.Discount != 0) invoiceRows.Add(ExcelRow.Data(inv.DiscountIsIncome ? "خصم على الفاتورة (خصم مكتسب)" : "خصم على الفاتورة", null, null, -inv.Discount));
            if (inv.Discount != 0) invoiceRows.Add(ExcelRow.Total("إجمالي الفاتورة", null, null, inv.Total));
            var payments = inv.Payments.Count > 0 ? inv.Payments.Select(x => (x.Account, x.Amount)).ToList()
                : inv.PaidAmount > 0 ? [(inv.OldAccount ?? "—", inv.PaidAmount)] : new List<(string Account, decimal Amount)>();
            foreach (var (account, amount) in payments) invoiceRows.Add(ExcelRow.Data($"مدفوع عند الشراء من {account}", null, null, amount));
            foreach (var r in returns.Where(r => r.PurchaseInvoiceId == inv.Id).OrderBy(r => r.Date))
                invoiceRows.Add(ExcelRow.Data($"مردود {r.Number} بتاريخ {Local(r.Date):yyyy-MM-dd}", null, null, -r.Total));
            invoiceRows.Add(ExcelRow.Blank);
        }

        // ---- payments: vouchers to the supplier and what was paid when buying
        var vouchers = await db.PaymentVouchers.AsNoTracking()
            .Where(v => v.SupplierId == supplierId && v.Date >= startUtc && v.Date < endUtc)
            .OrderBy(v => v.Date).ThenBy(v => v.Id)
            .Select(v => new { v.Date, v.Number, Account = v.FinancialAccount.Name, v.Amount, v.Status, v.Reference, v.Notes }).ToListAsync(ct);
        var paymentRows = new List<ExcelRow>
        {
            ExcelRow.Title($"الدفعات للمورد: {supplier.Name} — {period}"),
            ExcelRow.Header("التاريخ", "المستند", "النوع", "من حساب", "المبلغ", "ملاحظات"),
        };
        var list = vouchers.Select(v => (Date: Local(v.Date), v.Number, Kind: v.Status == VoucherStatus.Voided ? "سند صرف (ملغى)" : "سند صرف", v.Account,
                Amount: v.Amount, Counted: v.Status != VoucherStatus.Voided, Notes: string.Join(" · ", new[] { v.Reference, v.Notes }.Where(x => !string.IsNullOrWhiteSpace(x)))))
            .Concat(posted.SelectMany(i => (i.Payments.Count > 0 ? i.Payments.Select(x => (x.Account, x.Amount)).ToList()
                    : i.PaidAmount > 0 ? [(i.OldAccount ?? "—", i.PaidAmount)] : new List<(string Account, decimal Amount)>())
                .Select(x => (Date: Local(i.Date), i.Number, Kind: "مدفوع عند الشراء", x.Account, x.Amount, Counted: true, Notes: ""))))
            .OrderBy(x => x.Date).ToList();
        if (list.Count == 0) paymentRows.Add(ExcelRow.Note("لا توجد دفعات في هذه الفترة."));
        paymentRows.AddRange(list.Select(x => ExcelRow.Data(x.Date, x.Number, x.Kind, x.Account, x.Counted ? x.Amount : null, x.Counted ? x.Notes : $"ملغى: {x.Amount:0.##} {x.Notes}".Trim())));
        paymentRows.Add(ExcelRow.Total("مجموع الدفعات", null, null, null, list.Where(x => x.Counted).Sum(x => x.Amount), null));

        var file = excel.WriteSheets(
        [
            new ExcelSheet("الملخص", [34, 22], summary),
            new ExcelSheet("فواتير الشراء", [44, 16, 18, 16, 18], invoiceRows),
            new ExcelSheet("كشف الحساب", [17, 18, 20, 36, 16, 16, 14], ledger),
            new ExcelSheet("الدفعات", [17, 20, 18, 20, 14, 34], paymentRows),
        ]);
        return (file, $"supplier-{supplier.Code}-{end:yyyyMMdd}.xlsx");
    }
}
