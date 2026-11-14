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
        // It reads every movement on purpose; on a large store with a cold cache that may take longer than a screen's 30 seconds.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
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
        var sums = await db.AccountTransactions.AsNoTracking().GroupBy(t => t.FinancialAccountId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Direction == MoneyDirection.In ? t.Amount : -t.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var rows = (await db.FinancialAccounts.AsNoTracking().Select(a => new { a.Id, a.Name, a.CurrentBalance }).ToListAsync(ct))
            .Select(a => new { a.Id, a.Name, a.CurrentBalance, Computed = sums.GetValueOrDefault(a.Id) }).ToList();
        return Check("أرصدة الحسابات المالية", "رصيد كل حساب (الصندوق، البنك، المحافظ) = مجموع ما دخله − مجموع ما خرج منه",
            rows.Count, rows.Where(r => Math.Abs(r.CurrentBalance - r.Computed) > Money)
                .Select(r => new IntegrityIssue(r.Name, r.CurrentBalance, r.Computed, $"/accounts/{r.Id}"))
                // Money cannot be below nothing: a negative account means money left that was never there.
                .Concat(rows.Where(r => r.CurrentBalance < -Money)
                    .Select(r => new IntegrityIssue($"{r.Name}: الرصيد بالسالب", r.CurrentBalance, 0, $"/accounts/{r.Id}"))));
    }

    private async Task<IntegrityCheck> CustomersAsync(CancellationToken ct)
    {
        var sums = await db.CustomerLedgerEntries.AsNoTracking().GroupBy(e => e.CustomerId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(e => e.Debit - e.Credit) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var rows = (await db.Customers.AsNoTracking().Select(c => new { c.Id, c.Code, c.Name, c.Balance, c.IsWalkIn }).ToListAsync(ct))
            .Select(c => new { c.Id, c.Code, c.Name, c.Balance, c.IsWalkIn, Computed = sums.GetValueOrDefault(c.Id) }).ToList();
        return Check("ديون العملاء", "رصيد كل عميل = مجموع حركات كشف حسابه (فواتير، دفعات، مرتجعات، سندات)",
            rows.Count, rows.Where(r => Math.Abs(r.Balance - r.Computed) > Money || (r.IsWalkIn && r.Balance != 0))
                .Select(r => new IntegrityIssue($"{r.Code} · {r.Name}", r.Balance, r.IsWalkIn ? 0 : r.Computed, $"/customers/{r.Id}")));
    }

    private async Task<IntegrityCheck> SuppliersAsync(CancellationToken ct)
    {
        var sums = await db.SupplierLedgerEntries.AsNoTracking().GroupBy(e => e.SupplierId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(e => e.Credit - e.Debit) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var rows = (await db.Suppliers.AsNoTracking().Select(x => new { x.Id, x.Code, x.Name, x.Balance }).ToListAsync(ct))
            .Select(x => new { x.Id, x.Code, x.Name, x.Balance, Computed = sums.GetValueOrDefault(x.Id) }).ToList();
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
        // The product's average cost is what its last stock movement left. One grouped query, not one lookup per product.
        var lastIds = db.StockMovements.GroupBy(m => m.ProductId).Select(g => g.Max(m => m.Id));
        var last = await db.StockMovements.AsNoTracking().Where(m => lastIds.Contains(m.Id))
            .Select(m => new { m.ProductId, m.AverageCostAfter }).ToDictionaryAsync(m => m.ProductId, m => m.AverageCostAfter, ct);
        var products = await db.Products.AsNoTracking().Select(p => new { p.Id, p.AverageCost }).ToListAsync(ct);
        var rows = products.Where(p => last.ContainsKey(p.Id)).ToList();
        var bad = rows.Where(r => Math.Abs(r.AverageCost - last[r.Id]) > Cost).ToList();
        var names = await NamesAsync(bad.Select(r => r.Id), ct);
        return Check("متوسط التكلفة", "متوسط تكلفة كل منتج = ما تركته آخر حركة مخزون له",
            rows.Count, bad.Select(r => new IntegrityIssue(names.GetValueOrDefault(r.Id, $"#{r.Id}"), r.AverageCost, last[r.Id], $"/products/{r.Id}")));
    }

    private async Task<IntegrityCheck> SaleInvoicesAsync(CancellationToken ct)
    {
        // Each total is summed once for every invoice (grouped queries), then matched in memory: a store with tens of thousands of
        // invoices is checked in a few seconds instead of one lookup per invoice.
        var invoices = await db.SaleInvoices.AsNoTracking()
            .Select(s => new { s.Id, s.Number, s.Status, s.Total, s.PaidAmount, s.CollectedAmount }).ToListAsync(ct);
        var lines = await db.SaleInvoiceLines.AsNoTracking().GroupBy(l => l.SaleInvoiceId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(l => l.NetTotal) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var payments = await db.SalePayments.AsNoTracking().GroupBy(p => p.SaleInvoiceId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var allocated = await db.ReceiptAllocations.AsNoTracking().Where(a => a.SaleInvoiceId != null).GroupBy(a => a.SaleInvoiceId!.Value)
            .Select(g => new { Id = g.Key, Sum = g.Sum(a => a.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var returned = await db.SaleReturns.AsNoTracking().Where(r => r.Settlement == ReturnSettlement.ReduceBalance).GroupBy(r => r.SaleInvoiceId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(r => r.Total) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var rows = invoices.Select(s => new
        {
            s.Id, s.Number, s.Status, s.Total, s.PaidAmount, s.CollectedAmount,
            Lines = lines.GetValueOrDefault(s.Id), Payments = payments.GetValueOrDefault(s.Id),
            Allocated = allocated.GetValueOrDefault(s.Id), ReturnedOnBalance = returned.GetValueOrDefault(s.Id),
        }).ToList();
        var issues = new List<IntegrityIssue>();
        foreach (var s in rows)
        {
            var link = $"/sales/{s.Id}";
            if (Math.Abs(s.Total - s.Lines) > Money) issues.Add(new($"{s.Number}: الإجمالي = مجموع البنود", s.Total, s.Lines, link));
            if (Math.Abs(s.PaidAmount - s.Payments) > Money) issues.Add(new($"{s.Number}: المدفوع عند البيع = مجموع الدفعات", s.PaidAmount, s.Payments, link));
            if (Math.Abs(s.CollectedAmount - s.Allocated) > Money) issues.Add(new($"{s.Number}: المحصّل لاحقًا = مجموع السندات", s.CollectedAmount, s.Allocated, link));
            // Paid at the till and collected later never exceed the total. A return credited to the balance of a paid invoice
            // is not an overpayment: it leaves the customer a credit.
            if (s.Status != SaleStatus.Voided && s.PaidAmount + s.CollectedAmount > s.Total + Money)
                issues.Add(new($"{s.Number}: المدفوع والمحصّل أكبر من الإجمالي", s.PaidAmount + s.CollectedAmount, s.Total, link));
        }
        return Check("فواتير البيع", "إجمالي كل فاتورة = بنودها، والمدفوع = دفعاتها، والمحصّل لاحقًا = سنداتها، ولا يتجاوز المدفوع الإجمالي",
            rows.Count, issues);
    }

    private async Task<IntegrityCheck> PurchaseInvoicesAsync(CancellationToken ct)
    {
        var lines = await db.PurchaseInvoiceLines.AsNoTracking().GroupBy(l => l.PurchaseInvoiceId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(l => l.LineTotal) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var rows = (await db.PurchaseInvoices.AsNoTracking()
                .Select(p => new { p.Id, p.Number, p.Subtotal, p.Discount, p.Total, p.PaidAmount }).ToListAsync(ct))
            .Select(p => new { p.Id, p.Number, p.Subtotal, p.Discount, p.Total, p.PaidAmount, Lines = lines.GetValueOrDefault(p.Id) }).ToList();
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
        // Only lines with a returned quantity, or with a return document, can disagree; the rest need no lookup.
        var saleReturns = await db.SaleReturnLines.AsNoTracking().GroupBy(r => r.SaleInvoiceLineId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(r => r.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var saleIds = saleReturns.Keys.ToList();
        var sale = (await db.SaleInvoiceLines.AsNoTracking().Where(l => l.ReturnedQuantity != 0 || saleIds.Contains(l.Id))
                .Select(l => new { l.Id, l.SaleInvoiceId, l.SaleInvoice.Number, l.ProductName, l.ReturnedQuantity }).ToListAsync(ct))
            .Select(l => new { l.SaleInvoiceId, l.Number, l.ProductName, l.ReturnedQuantity, Computed = saleReturns.GetValueOrDefault(l.Id) }).ToList();
        var purchaseReturns = await db.PurchaseReturnLines.AsNoTracking().GroupBy(r => r.PurchaseInvoiceLineId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(r => r.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var purchaseIds = purchaseReturns.Keys.ToList();
        var purchase = (await db.PurchaseInvoiceLines.AsNoTracking().Where(l => l.ReturnedQuantity != 0 || purchaseIds.Contains(l.Id))
                .Select(l => new { l.Id, l.PurchaseInvoiceId, l.PurchaseInvoice.Number, l.ProductId, l.ReturnedQuantity }).ToListAsync(ct))
            .Select(l => new { l.PurchaseInvoiceId, l.Number, l.ProductId, l.ReturnedQuantity, Computed = purchaseReturns.GetValueOrDefault(l.Id) }).ToList();
        var checkedLines = await db.SaleInvoiceLines.CountAsync(ct) + await db.PurchaseInvoiceLines.CountAsync(ct);
        var issues = sale.Where(l => Math.Abs(l.ReturnedQuantity - l.Computed) > Quantity)
            .Select(l => new IntegrityIssue($"{l.Number}: {l.ProductName}", l.ReturnedQuantity, l.Computed, $"/sales/{l.SaleInvoiceId}"))
            .Concat(purchase.Where(l => Math.Abs(l.ReturnedQuantity - l.Computed) > Quantity)
                .Select(l => new IntegrityIssue($"{l.Number}: منتج #{l.ProductId}", l.ReturnedQuantity, l.Computed, $"/purchases/{l.PurchaseInvoiceId}")));
        return Check("الكميات المرتجعة", "الكمية المرتجعة من كل بند = مجموع مستندات المرتجع عليه (بيع وشراء)", checkedLines, issues);
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Sku, p.Name }).ToListAsync(ct))
            .ToDictionary(p => p.Id, p => $"{p.Sku} · {p.Name}");
    }
}
