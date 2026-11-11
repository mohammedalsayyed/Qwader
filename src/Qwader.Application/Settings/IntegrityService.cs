using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Application.Settings;

/// <param name="Link">The page that shows the record, when it has one.</param>
public sealed record IntegrityIssue(string Label, decimal Stored, decimal Computed, string? Link);

/// <param name="Checked">How many records were compared.</param>
/// <param name="IssueCount">All mismatches found; <see cref="Issues"/> lists the first ones.</param>
public sealed record IntegrityCheck(string Name, string Explanation, int Checked, int IssueCount, IReadOnlyList<IntegrityIssue> Issues);

public sealed record IntegrityReport(DateTime CheckedAt, bool Ok, IReadOnlyList<IntegrityCheck> Checks);

public interface IIntegrityService
{
    /// <summary>
    /// Recomputes every stored balance from the movements behind it and lists any difference. Read-only: nothing is corrected
    /// or written, so it is safe to run on live data at any time.
    /// </summary>
    Task<IntegrityReport> CheckAsync(CancellationToken ct = default);
}

public sealed class IntegrityService(IAppDbContext db, IClock clock) : IIntegrityService
{
    private const decimal Money = 0.005m;     // half a cent
    private const decimal Quantity = 0.0005m; // half of the smallest stored quantity step
    private const decimal Cost = 0.00005m;    // half of the smallest stored cost step
    private const int Shown = 50;

    public async Task<IntegrityReport> CheckAsync(CancellationToken ct = default)
    {
        var checks = new List<IntegrityCheck>
        {
            await AccountsAsync(ct),
            await CustomersAsync(ct),
            await SuppliersAsync(ct),
            await StockAsync(ct),
            await AverageCostAsync(ct),
            await SaleInvoicesAsync(ct),
            await PurchaseInvoicesAsync(ct),
            await ReturnedQuantitiesAsync(ct),
        };
        return new IntegrityReport(clock.UtcNow, checks.All(c => c.IssueCount == 0), checks);
    }

    private static IntegrityCheck Check(string name, string explanation, int count, IEnumerable<IntegrityIssue> issues)
    {
        var all = issues.ToList();
        return new IntegrityCheck(name, explanation, count, all.Count, all.Take(Shown).ToList());
    }

    private async Task<IntegrityCheck> AccountsAsync(CancellationToken ct)
    {
        var rows = await db.FinancialAccounts.AsNoTracking().Select(a => new
        {
            a.Id, a.Name, a.CurrentBalance,
            Computed = db.AccountTransactions.Where(t => t.FinancialAccountId == a.Id)
                .Sum(t => (decimal?)(t.Direction == MoneyDirection.In ? t.Amount : -t.Amount)) ?? 0,
        }).ToListAsync(ct);
        return Check("أرصدة الحسابات المالية", "رصيد كل حساب (الصندوق، البنك، المحافظ) = مجموع ما دخله − مجموع ما خرج منه",
            rows.Count, rows.Where(r => Math.Abs(r.CurrentBalance - r.Computed) > Money)
                .Select(r => new IntegrityIssue(r.Name, r.CurrentBalance, r.Computed, $"/accounts/{r.Id}")));
    }

    private async Task<IntegrityCheck> CustomersAsync(CancellationToken ct)
    {
        var rows = await db.Customers.AsNoTracking().Select(c => new
        {
            c.Id, c.Code, c.Name, c.Balance, c.IsWalkIn,
            Computed = db.CustomerLedgerEntries.Where(e => e.CustomerId == c.Id).Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0,
        }).ToListAsync(ct);
        return Check("ديون العملاء", "رصيد كل عميل = مجموع حركات كشف حسابه (فواتير، دفعات، مرتجعات، سندات)",
            rows.Count, rows.Where(r => Math.Abs(r.Balance - r.Computed) > Money || (r.IsWalkIn && r.Balance != 0))
                .Select(r => new IntegrityIssue($"{r.Code} · {r.Name}", r.Balance, r.IsWalkIn ? 0 : r.Computed, $"/customers/{r.Id}")));
    }

    private async Task<IntegrityCheck> SuppliersAsync(CancellationToken ct)
    {
        var rows = await db.Suppliers.AsNoTracking().Select(s => new
        {
            s.Id, s.Code, s.Name, s.Balance,
            Computed = db.SupplierLedgerEntries.Where(e => e.SupplierId == s.Id).Sum(e => (decimal?)(e.Credit - e.Debit)) ?? 0,
        }).ToListAsync(ct);
        return Check("مستحقات الموردين", "رصيد كل مورد = مجموع حركات كشف حسابه (فواتير شراء، دفعات، مردودات)",
            rows.Count, rows.Where(r => Math.Abs(r.Balance - r.Computed) > Money)
                .Select(r => new IntegrityIssue($"{r.Code} · {r.Name}", r.Balance, r.Computed, $"/suppliers/{r.Id}")));
    }

    private async Task<IntegrityCheck> StockAsync(CancellationToken ct)
    {
        var balances = await db.StockBalances.AsNoTracking()
            .Select(b => new { b.ProductId, b.WarehouseId, b.Quantity }).ToListAsync(ct);
        var moved = await db.StockMovements.AsNoTracking().GroupBy(m => new { m.ProductId, m.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Quantity = g.Sum(m => m.Quantity) }).ToListAsync(ct);
        var keys = balances.Select(b => (b.ProductId, b.WarehouseId)).Union(moved.Select(m => (m.ProductId, m.WarehouseId))).ToList();
        var stored = balances.ToDictionary(b => (b.ProductId, b.WarehouseId), b => b.Quantity);
        var computed = moved.ToDictionary(m => (m.ProductId, m.WarehouseId), m => m.Quantity);
        var bad = keys.Where(k => Math.Abs(stored.GetValueOrDefault(k) - computed.GetValueOrDefault(k)) > Quantity).ToList();
        var names = await NamesAsync(bad.Select(k => k.ProductId), ct);
        return Check("كميات المخزون", "رصيد كل منتج = مجموع حركات مخزونه (شراء، بيع، مرتجعات، تالف، جرد…)",
            keys.Count, bad.Select(k => new IntegrityIssue(names.GetValueOrDefault(k.ProductId, $"#{k.ProductId}"),
                stored.GetValueOrDefault(k), computed.GetValueOrDefault(k), $"/products/{k.ProductId}")));
    }

    private async Task<IntegrityCheck> AverageCostAsync(CancellationToken ct)
    {
        // The product's average cost is what its last stock movement left.
        var rows = await db.Products.AsNoTracking()
            .Select(p => new
            {
                p.Id, p.AverageCost,
                Last = db.StockMovements.Where(m => m.ProductId == p.Id).OrderByDescending(m => m.Id).Select(m => (decimal?)m.AverageCostAfter).FirstOrDefault(),
            })
            .Where(p => p.Last != null).ToListAsync(ct);
        var bad = rows.Where(r => Math.Abs(r.AverageCost - r.Last!.Value) > Cost).ToList();
        var names = await NamesAsync(bad.Select(r => r.Id), ct);
        return Check("متوسط التكلفة", "متوسط تكلفة كل منتج = ما تركته آخر حركة مخزون له",
            rows.Count, bad.Select(r => new IntegrityIssue(names.GetValueOrDefault(r.Id, $"#{r.Id}"), r.AverageCost, r.Last!.Value, $"/products/{r.Id}")));
    }

    private async Task<IntegrityCheck> SaleInvoicesAsync(CancellationToken ct)
    {
        var rows = await db.SaleInvoices.AsNoTracking().Select(s => new
        {
            s.Id, s.Number, s.Status, s.Total, s.PaidAmount, s.CollectedAmount,
            Lines = s.Lines.Sum(l => (decimal?)l.NetTotal) ?? 0,
            Payments = s.Payments.Sum(p => (decimal?)p.Amount) ?? 0,
            Allocated = db.ReceiptAllocations.Where(a => a.SaleInvoiceId == s.Id).Sum(a => (decimal?)a.Amount) ?? 0,
            ReturnedOnBalance = db.SaleReturns.Where(r => r.SaleInvoiceId == s.Id && r.Settlement == ReturnSettlement.ReduceBalance).Sum(r => (decimal?)r.Total) ?? 0,
        }).ToListAsync(ct);
        var issues = new List<IntegrityIssue>();
        foreach (var s in rows)
        {
            var link = $"/sales/{s.Id}";
            if (Math.Abs(s.Total - s.Lines) > Money) issues.Add(new($"{s.Number}: الإجمالي = مجموع البنود", s.Total, s.Lines, link));
            if (Math.Abs(s.PaidAmount - s.Payments) > Money) issues.Add(new($"{s.Number}: المدفوع عند البيع = مجموع الدفعات", s.PaidAmount, s.Payments, link));
            if (Math.Abs(s.CollectedAmount - s.Allocated) > Money) issues.Add(new($"{s.Number}: المحصّل لاحقًا = مجموع السندات", s.CollectedAmount, s.Allocated, link));
            if (s.Status != SaleStatus.Voided && s.PaidAmount + s.CollectedAmount + s.ReturnedOnBalance > s.Total + Money)
                issues.Add(new($"{s.Number}: المدفوع والمحصّل والمرتجع أكبر من الإجمالي", s.PaidAmount + s.CollectedAmount + s.ReturnedOnBalance, s.Total, link));
        }
        return Check("فواتير البيع", "إجمالي كل فاتورة = بنودها، والمدفوع = دفعاتها، والمحصّل لاحقًا = سنداتها، ولا يتجاوز المدفوع الإجمالي",
            rows.Count, issues);
    }

    private async Task<IntegrityCheck> PurchaseInvoicesAsync(CancellationToken ct)
    {
        var rows = await db.PurchaseInvoices.AsNoTracking().Select(p => new
        {
            p.Id, p.Number, p.Subtotal, p.Discount, p.Total, p.PaidAmount,
            Lines = p.Lines.Sum(l => (decimal?)l.LineTotal) ?? 0,
        }).ToListAsync(ct);
        var issues = new List<IntegrityIssue>();
        foreach (var p in rows)
        {
            var link = $"/purchases/{p.Id}";
            if (Math.Abs(p.Subtotal - p.Lines) > Money) issues.Add(new($"{p.Number}: المجموع = مجموع البنود", p.Subtotal, p.Lines, link));
            if (Math.Abs(p.Total - (p.Subtotal - p.Discount)) > Money) issues.Add(new($"{p.Number}: الإجمالي = المجموع − الخصم", p.Total, p.Subtotal - p.Discount, link));
            if (p.PaidAmount > p.Total + Money) issues.Add(new($"{p.Number}: المدفوع أكبر من الإجمالي", p.PaidAmount, p.Total, link));
        }
        return Check("فواتير الشراء", "مجموع كل فاتورة = بنودها، والإجمالي = المجموع − الخصم، والمدفوع لا يتجاوز الإجمالي", rows.Count, issues);
    }

    private async Task<IntegrityCheck> ReturnedQuantitiesAsync(CancellationToken ct)
    {
        var sale = await db.SaleInvoiceLines.AsNoTracking().Select(l => new
        {
            l.SaleInvoiceId, l.SaleInvoice.Number, l.ProductName, l.ReturnedQuantity,
            Computed = db.SaleReturnLines.Where(r => r.SaleInvoiceLineId == l.Id).Sum(r => (decimal?)r.Quantity) ?? 0,
        }).ToListAsync(ct);
        var purchase = await db.PurchaseInvoiceLines.AsNoTracking().Select(l => new
        {
            l.PurchaseInvoiceId, l.PurchaseInvoice.Number, l.ProductId, l.ReturnedQuantity,
            Computed = db.PurchaseReturnLines.Where(r => r.PurchaseInvoiceLineId == l.Id).Sum(r => (decimal?)r.Quantity) ?? 0,
        }).ToListAsync(ct);
        var issues = sale.Where(l => Math.Abs(l.ReturnedQuantity - l.Computed) > Quantity)
            .Select(l => new IntegrityIssue($"{l.Number}: {l.ProductName}", l.ReturnedQuantity, l.Computed, $"/sales/{l.SaleInvoiceId}"))
            .Concat(purchase.Where(l => Math.Abs(l.ReturnedQuantity - l.Computed) > Quantity)
                .Select(l => new IntegrityIssue($"{l.Number}: منتج #{l.ProductId}", l.ReturnedQuantity, l.Computed, $"/purchases/{l.PurchaseInvoiceId}")));
        return Check("الكميات المرتجعة", "الكمية المرتجعة من كل بند = مجموع مستندات المرتجع عليه (بيع وشراء)", sale.Count + purchase.Count, issues);
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Sku, p.Name }).ToListAsync(ct))
            .ToDictionary(p => p.Id, p => $"{p.Sku} · {p.Name}");
    }
}
