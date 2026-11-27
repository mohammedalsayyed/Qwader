using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Purchasing;

public sealed record SupplierReportLine(int ProductId, string Name, string? Sku, string Unit, decimal Quantity, decimal UnitCost, decimal LineDiscount,
    decimal LineTotal, decimal ReturnedQuantity);

public sealed record SupplierReportPaid(string Account, decimal Amount);

public sealed record SupplierReportReturn(int Id, string Number, DateTime Date, decimal Total);

/// <summary>A payment voucher tied to the invoice (transport, unloading…): an expense of the store, not a payment to the supplier.</summary>
public sealed record SupplierReportExpense(int PaymentVoucherId, string Number, DateTime Date, string Category, string Description, string Account,
    decimal Amount, bool Voided);

public sealed record SupplierReportInvoice(int Id, string Number, DateTime Date, string? SupplierInvoiceNo, bool Voided, string? VoidReason,
    decimal Subtotal, decimal Discount, bool DiscountIsIncome, decimal Total, decimal PaidAmount,
    IReadOnlyList<SupplierReportLine> Lines, IReadOnlyList<SupplierReportPaid> PaidWhenBuying, IReadOnlyList<SupplierReportReturn> Returns,
    IReadOnlyList<SupplierReportExpense> Expenses);

/// <param name="VoucherId">The payment voucher; null for money paid when buying (<paramref name="PurchaseInvoiceId"/>).</param>
public sealed record SupplierReportPayment(int? VoucherId, int? PurchaseInvoiceId, DateTime Date, string Number, string Kind, string Account, decimal Amount,
    bool Voided, string? Notes);

/// <summary>
/// Everything about a supplier over a period: what was bought, returned and paid and what is still owed (adding up to their
/// balance), each purchase invoice with its lines, payments when buying, returns and the vouchers tied to it, the payments,
/// and the statement. The supplier page's «التفاصيل» tab and the Excel files are built from it.
/// </summary>
public sealed record SupplierReportDto(int SupplierId, string Name, string Code, string? Phone, DateOnly? From, DateOnly To,
    decimal OpeningBalance, decimal OldBalance, decimal Bought, decimal Returned, decimal Paid, decimal Refunded, decimal Closing,
    IReadOnlyList<SupplierReportInvoice> Invoices, IReadOnlyList<SupplierReportPayment> Payments, StatementDto Statement)
{
    public IEnumerable<SupplierReportInvoice> Posted => Invoices.Where(i => !i.Voided);
}

public interface ISupplierReportService
{
    Task<SupplierReportDto> ReportAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public sealed class SupplierReportService(IAppDbContext db, ISupplierService suppliers, IClock clock) : ISupplierReportService
{
    public async Task<SupplierReportDto> ReportAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw new NotFoundException("المورد غير موجود");
        var statement = await suppliers.StatementAsync(supplierId, from, to, ct);
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(from ?? new DateOnly(2000, 1, 1), statement.To);

        // Totals by kind. A reversal (a voided document) counts against the kind it reverses, so a voided invoice and its
        // reversal cancel out and the totals add up to the balance.
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

        var invoices = await db.PurchaseInvoices.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.Status != PurchaseStatus.Draft && p.Date >= startUtc && p.Date < endUtc)
            .OrderBy(p => p.Date).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Number, p.Date, p.Status, p.SupplierInvoiceNo, p.Subtotal, p.Discount, p.DiscountIsIncome, p.Total, p.PaidAmount, p.VoidReason,
                OldAccount = p.PaymentAccount != null ? p.PaymentAccount.Name : null,
                Lines = p.Lines.OrderBy(l => l.Id).Select(l => new SupplierReportLine(l.ProductId, l.Product.Name, l.Product.Sku, l.UnitName ?? l.Product.Unit.Name,
                    l.Quantity, l.UnitCost, l.LineDiscount, l.LineTotal, l.ReturnedQuantity)).ToList(),
                Payments = p.Payments.Select(x => new SupplierReportPaid(x.FinancialAccount.Name, x.Amount)).ToList(),
            }).ToListAsync(ct);
        var ids = invoices.Select(i => i.Id).ToList();
        var returns = await db.PurchaseReturns.AsNoTracking().Where(r => ids.Contains(r.PurchaseInvoiceId)).OrderBy(r => r.Date)
            .Select(r => new { r.PurchaseInvoiceId, Row = new SupplierReportReturn(r.Id, r.Number, r.Date, r.Total) }).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.PurchaseInvoiceId != null && ids.Contains(e.PurchaseInvoiceId.Value)).OrderBy(e => e.Date)
            .Select(e => new
            {
                InvoiceId = e.PurchaseInvoiceId!.Value,
                Row = new SupplierReportExpense(e.PaymentVoucherId, e.PaymentVoucher.Number, e.Date, e.Category.Name, e.Description, e.FinancialAccount.Name,
                    e.Amount, e.Status == VoucherStatus.Voided),
            }).ToListAsync(ct);

        var rows = invoices.Select(i => new SupplierReportInvoice(i.Id, i.Number, i.Date, i.SupplierInvoiceNo, i.Status == PurchaseStatus.Voided, i.VoidReason,
            i.Subtotal, i.Discount, i.DiscountIsIncome, i.Total, i.PaidAmount, i.Lines,
            // Older invoices kept one payment account on the invoice itself.
            i.Payments.Count > 0 ? i.Payments : i.PaidAmount > 0 ? [new SupplierReportPaid(i.OldAccount ?? "—", i.PaidAmount)] : [],
            returns.Where(r => r.PurchaseInvoiceId == i.Id).Select(r => r.Row).ToList(),
            expenses.Where(e => e.InvoiceId == i.Id).Select(e => e.Row).ToList())).ToList();

        var vouchers = await db.PaymentVouchers.AsNoTracking()
            .Where(v => v.SupplierId == supplierId && v.Date >= startUtc && v.Date < endUtc)
            .Select(v => new { v.Id, v.Date, v.Number, Account = v.FinancialAccount.Name, v.Amount, v.Status, v.Reference, v.Notes }).ToListAsync(ct);
        var payments = vouchers.Select(v => new SupplierReportPayment(v.Id, null, v.Date, v.Number, "سند صرف", v.Account, v.Amount, v.Status == VoucherStatus.Voided,
                string.Join(" · ", new[] { v.Reference, v.Notes }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } n ? n : null))
            .Concat(rows.Where(i => !i.Voided).SelectMany(i => i.PaidWhenBuying.Select(x =>
                new SupplierReportPayment(null, i.Id, i.Date, i.Number, "مدفوع عند الشراء", x.Account, x.Amount, false, null))))
            .OrderBy(p => p.Date).ToList();

        return new SupplierReportDto(supplier.Id, supplier.Name, supplier.Code, supplier.Phone, from, statement.To,
            statement.OpeningBalance, Net(SupplierEntryType.Opening), Net(SupplierEntryType.Purchase), -Net(SupplierEntryType.Return),
            -Net(SupplierEntryType.Payment), Net(SupplierEntryType.Refund), statement.ClosingBalance, rows, payments, statement);
    }
}
