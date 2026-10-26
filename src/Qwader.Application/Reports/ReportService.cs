using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Finance;
using Qwader.Domain.Catalog;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;
using Qwader.Domain.Sales;

namespace Qwader.Application.Reports;

public sealed record NamedAmount(string Name, decimal Amount);

public sealed record AccountBalanceDto(int AccountId, string Name, decimal In, decimal Out, decimal ClosingBalance);

/// <summary>
/// One period, every figure. Fields the caller may not see are null:
/// cost/profit need Reports.Profit; money, debts and expenses need Reports.Financial.
/// </summary>
public sealed record PeriodReportDto(
    DateOnly From, DateOnly To,
    // sales
    int InvoiceCount, decimal GrossSales, decimal Discounts, decimal Returns, decimal NetSales, decimal AverageInvoice,
    // profit
    decimal? CostOfSales, decimal? DamagedReturnsCost, decimal? GrossProfit, decimal? InventoryLosses,
    decimal? Expenses, decimal? NetOperatingProfit, IReadOnlyList<NamedAmount>? ExpensesByCategory,
    // money
    decimal? CashIn, decimal? CashOut, decimal? NetCashFlow,
    IReadOnlyList<NamedAmount>? InByCategory, IReadOnlyList<NamedAmount>? OutByCategory,
    IReadOnlyList<AccountBalanceDto>? Accounts,
    decimal? NewDebt, decimal? Collections, int PendingTransferCount, decimal? PendingTransferAmount,
    // position (as of now)
    decimal? Receivables, decimal? Payables, decimal? InventoryValue,
    // Stock found or counted over: shown, never counted as profit. TotalExpenses = cost of goods sold + losses + operating expenses,
    // so NetOperatingProfit = NetSales («الإيرادات») − TotalExpenses («المصروفات»).
    decimal? InventoryGains = null, decimal? TotalExpenses = null);

public sealed record DailyRowDto(DateOnly Day, int Invoices, decimal NetSales, decimal? GrossProfit, decimal? Expenses, decimal? Received);

public sealed record ProductReportRow(int ProductId, string Sku, string Name, string CategoryName, decimal Quantity, decimal Revenue, decimal? Cost, decimal? Profit, decimal? MarginPercent);

public sealed record GroupReportRow(int Id, string Name, int Count, decimal Quantity, decimal Revenue, decimal? Cost, decimal? Profit);

public sealed record BalanceRow(int Id, string Code, string Name, string? Phone, decimal Balance, DateTime? LastActivity);

public sealed record InventoryValueRow(int CategoryId, string CategoryName, int ProductCount, decimal Quantity, decimal Value);

public enum ProductSort
{
    Revenue = 1,
    Quantity = 2,
    Profit = 3,
}

public interface IReportService
{
    Task<PeriodReportDto> PeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<DailyRowDto>> DailyAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<ProductReportRow>> ProductsAsync(DateOnly from, DateOnly to, ProductSort sort, int take, CancellationToken ct = default);
    Task<IReadOnlyList<GroupReportRow>> CategoriesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<GroupReportRow>> SalespeopleAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<BalanceRow>> ReceivablesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BalanceRow>> PayablesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InventoryValueRow>> InventoryValueAsync(CancellationToken ct = default);
}

public sealed class ReportService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IReportService
{
    private static readonly StockMovementType[] LossTypes = [StockMovementType.Damaged, StockMovementType.Lost, StockMovementType.CountDecrease];

    /// <summary>
    /// Surpluses are usually stock that was never entered (or entered through a count instead of a purchase);
    /// counting them as profit would inflate it by the value of the goods, so they are reported apart.
    /// </summary>
    private static readonly StockMovementType[] GainTypes = [StockMovementType.Found, StockMovementType.CountIncrease];

    /// <summary>Movements that move money between the store's own pockets or set its starting point: not cash flow.</summary>
    private static readonly AccountTransactionCategory[] NotCashFlow =
        [AccountTransactionCategory.OpeningBalance, AccountTransactionCategory.TransferIn, AccountTransactionCategory.TransferOut];

    private bool Profit => currentUser.HasPermission(Permissions.ReportsProfit);
    private bool Financial => currentUser.HasPermission(Permissions.ReportsFinancial);

    private (DateTime Start, DateTime End) Range(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ValidationFailedException("to", "تاريخ النهاية قبل تاريخ البداية");
        if (to.DayNumber - from.DayNumber > 3660) throw new ValidationFailedException("from", "الفترة طويلة جدًا (أكثر من 10 سنوات)");
        return clock.LocalDaysToUtcRange(from, to);
    }

    // ------------------------------------------------------------------ period summary

    public async Task<PeriodReportDto> PeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);

        // Voided invoices are treated as never having happened.
        var sales = db.SaleInvoices.AsNoTracking().Where(s => s.Date >= start && s.Date < end && s.Status != SaleStatus.Voided);
        var s = await sales.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            Gross = g.Sum(x => x.GrossTotal),
            Discounts = g.Sum(x => x.LineDiscountTotal + x.InvoiceDiscount),
            Net = g.Sum(x => x.Total),
            Paid = g.Sum(x => x.PaidAmount),
            Cost = g.Sum(x => x.TotalCost),
        }).FirstOrDefaultAsync(ct);

        // Returns count in the period they happen, whatever the invoice date.
        var returnLines = db.SaleReturnLines.AsNoTracking().Where(l => l.SaleReturn.Date >= start && l.SaleReturn.Date < end);
        var r = await returnLines.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Sum(l => l.LineTotal),
            SellableCost = g.Where(l => l.Condition == ReturnCondition.Sellable).Sum(l => l.Quantity * l.UnitCost),
            DamagedCost = g.Where(l => l.Condition == ReturnCondition.Damaged).Sum(l => l.Quantity * l.UnitCost),
        }).FirstOrDefaultAsync(ct);

        var gross = s?.Gross ?? 0;
        var discounts = s?.Discounts ?? 0;
        var returns = r?.Total ?? 0;
        var netSales = gross - discounts - returns;
        var count = s?.Count ?? 0;

        decimal? cogs = null, damaged = null, grossProfit = null, losses = null, expenses = null, netProfit = null, gains = null, totalExpenses = null;
        IReadOnlyList<NamedAmount>? expensesByCategory = null;
        if (Profit)
        {
            cogs = CostMath.RoundMoney((s?.Cost ?? 0) - (r?.SellableCost ?? 0));
            damaged = CostMath.RoundMoney(r?.DamagedCost ?? 0);
            grossProfit = netSales - cogs;
            // Shrinkage: damaged, lost and count shortages cost money.
            losses = CostMath.RoundMoney(await db.StockMovements.AsNoTracking()
                .Where(m => m.OccurredAt >= start && m.OccurredAt < end && LossTypes.Contains(m.Type))
                .SumAsync(m => (decimal?)(-m.Quantity * m.UnitCost), ct) ?? 0);
            gains = CostMath.RoundMoney(await db.StockMovements.AsNoTracking()
                .Where(m => m.OccurredAt >= start && m.OccurredAt < end && GainTypes.Contains(m.Type))
                .SumAsync(m => (decimal?)(m.Quantity * m.UnitCost), ct) ?? 0);
        }
        if (Profit || Financial)
        {
            var expenseRows = await db.Expenses.AsNoTracking().Where(e => e.Date >= start && e.Date < end && e.Status == VoucherStatus.Posted)
                .GroupBy(e => e.Category.Name).Select(g => new NamedAmount(g.Key, g.Sum(e => e.Amount))).ToListAsync(ct);
            expensesByCategory = expenseRows.OrderByDescending(e => e.Amount).ToList();
            expenses = expenseRows.Sum(e => e.Amount);
            if (Profit)
            {
                netProfit = grossProfit - expenses - losses;
                totalExpenses = cogs + losses + expenses;
            }
        }

        decimal? cashIn = null, cashOut = null, newDebt = null, collections = null, pendingAmount = null, receivables = null, payables = null;
        IReadOnlyList<NamedAmount>? inByCategory = null, outByCategory = null;
        IReadOnlyList<AccountBalanceDto>? accounts = null;
        var pendingCount = 0;
        if (Financial)
        {
            var flows = await db.AccountTransactions.AsNoTracking()
                .Where(t => t.Date >= start && t.Date < end && !NotCashFlow.Contains(t.Category))
                .GroupBy(t => new { t.Direction, t.Category })
                .Select(g => new { g.Key.Direction, g.Key.Category, Amount = g.Sum(t => t.Amount) })
                .ToListAsync(ct);
            cashIn = flows.Where(f => f.Direction == MoneyDirection.In).Sum(f => f.Amount);
            cashOut = flows.Where(f => f.Direction == MoneyDirection.Out).Sum(f => f.Amount);
            inByCategory = flows.Where(f => f.Direction == MoneyDirection.In).OrderByDescending(f => f.Amount)
                .Select(f => new NamedAmount(FinanceNames.Category(f.Category), f.Amount)).ToList();
            outByCategory = flows.Where(f => f.Direction == MoneyDirection.Out).OrderByDescending(f => f.Amount)
                .Select(f => new NamedAmount(FinanceNames.Category(f.Category), f.Amount)).ToList();
            collections = flows.Where(f => f.Category == AccountTransactionCategory.DebtCollection && f.Direction == MoneyDirection.In).Sum(f => f.Amount);
            newDebt = (s?.Net ?? 0) - (s?.Paid ?? 0);

            // Per account: money in/out during the period (all movements) and the balance at the end of the period.
            var perAccount = await db.AccountTransactions.AsNoTracking().Where(t => t.Date < end)
                .GroupBy(t => t.FinancialAccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    In = g.Where(t => t.Date >= start && t.Direction == MoneyDirection.In).Sum(t => (decimal?)t.Amount) ?? 0,
                    Out = g.Where(t => t.Date >= start && t.Direction == MoneyDirection.Out).Sum(t => (decimal?)t.Amount) ?? 0,
                    Closing = g.Sum(t => t.Direction == MoneyDirection.In ? t.Amount : -t.Amount),
                }).ToListAsync(ct);
            var accountNames = await db.FinancialAccounts.AsNoTracking().Where(a => a.IsActive || perAccount.Select(p => p.AccountId).Contains(a.Id))
                .OrderBy(a => a.Type).ThenBy(a => a.Id).Select(a => new { a.Id, a.Name }).ToListAsync(ct);
            accounts = accountNames.Select(a =>
            {
                var p = perAccount.FirstOrDefault(x => x.AccountId == a.Id);
                return new AccountBalanceDto(a.Id, a.Name, p?.In ?? 0, p?.Out ?? 0, p?.Closing ?? 0);
            }).ToList();

            var pending = await db.IncomingTransfers.AsNoTracking().Where(t => t.Status == TransferStatus.Pending)
                .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(t => t.Amount) }).FirstOrDefaultAsync(ct);
            pendingCount = pending?.Count ?? 0;
            pendingAmount = pending?.Amount ?? 0;
            receivables = await db.Customers.Where(c => c.Balance > 0).SumAsync(c => (decimal?)c.Balance, ct) ?? 0;
            payables = await db.Suppliers.Where(x => x.Balance > 0).SumAsync(x => (decimal?)x.Balance, ct) ?? 0;
        }

        decimal? inventoryValue = Profit || Financial
            ? CostMath.RoundMoney(await db.StockBalances.AsNoTracking().SumAsync(b => (decimal?)(b.Quantity * b.Product.AverageCost), ct) ?? 0)
            : null;

        return new PeriodReportDto(from, to, count, gross, discounts, returns, netSales, count == 0 ? 0 : CostMath.RoundMoney((s?.Net ?? 0) / count),
            cogs, damaged, grossProfit, losses, expenses, netProfit, expensesByCategory,
            cashIn, cashOut, cashIn - cashOut, inByCategory, outByCategory, accounts,
            newDebt, collections, pendingCount, pendingAmount, receivables, payables, inventoryValue, gains, totalExpenses);
    }

    // ------------------------------------------------------------------ daily series

    public async Task<IReadOnlyList<DailyRowDto>> DailyAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to.DayNumber - from.DayNumber > 400) throw new ValidationFailedException("from", "اختر فترة لا تزيد عن 400 يوم للعرض اليومي");
        var (start, end) = Range(from, to);

        var sales = await db.SaleInvoices.AsNoTracking().Where(s => s.Date >= start && s.Date < end && s.Status != SaleStatus.Voided)
            .Select(s => new { s.Date, s.Total, s.TotalCost }).ToListAsync(ct);
        var returns = await db.SaleReturnLines.AsNoTracking().Where(l => l.SaleReturn.Date >= start && l.SaleReturn.Date < end)
            .Select(l => new { l.SaleReturn.Date, l.LineTotal, SellableCost = l.Condition == ReturnCondition.Sellable ? l.Quantity * l.UnitCost : 0 }).ToListAsync(ct);
        var expenses = Profit || Financial
            ? await db.Expenses.AsNoTracking().Where(e => e.Date >= start && e.Date < end && e.Status == VoucherStatus.Posted).Select(e => new { e.Date, e.Amount }).ToListAsync(ct)
            : [];
        var received = Financial
            ? await db.AccountTransactions.AsNoTracking().Where(t => t.Date >= start && t.Date < end && t.Direction == MoneyDirection.In
                    && (t.Category == AccountTransactionCategory.SaleReceipt || t.Category == AccountTransactionCategory.DebtCollection || t.Category == AccountTransactionCategory.OtherIncome))
                .Select(t => new { t.Date, t.Amount }).ToListAsync(ct)
            : [];

        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(clock.ToLocal(utc));
        var rows = new List<DailyRowDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var daySales = sales.Where(x => Day(x.Date) == day).ToList();
            var dayReturns = returns.Where(x => Day(x.Date) == day).ToList();
            var net = daySales.Sum(x => x.Total) - dayReturns.Sum(x => x.LineTotal);
            var cost = daySales.Sum(x => x.TotalCost) - dayReturns.Sum(x => x.SellableCost);
            rows.Add(new DailyRowDto(day, daySales.Count, net,
                Profit ? CostMath.RoundMoney(net - cost) : null,
                Profit || Financial ? expenses.Where(x => Day(x.Date) == day).Sum(x => x.Amount) : null,
                Financial ? received.Where(x => Day(x.Date) == day).Sum(x => x.Amount) : null));
        }
        return rows;
    }

    // ------------------------------------------------------------------ products & groups

    private async Task<List<(int ProductId, int CategoryId, int SalespersonId, decimal Qty, decimal Revenue, decimal Cost, int InvoiceId)>> LineFactsAsync(
        DateTime start, DateTime end, CancellationToken ct)
    {
        var sold = await db.SaleInvoiceLines.AsNoTracking()
            .Where(l => l.SaleInvoice.Date >= start && l.SaleInvoice.Date < end && l.SaleInvoice.Status != SaleStatus.Voided)
            .Select(l => new { l.ProductId, l.Product.CategoryId, l.SaleInvoice.SalespersonId, l.Quantity, l.NetTotal, Cost = l.Quantity * l.UnitCost, l.SaleInvoiceId })
            .ToListAsync(ct);
        var returned = await db.SaleReturnLines.AsNoTracking()
            .Where(l => l.SaleReturn.Date >= start && l.SaleReturn.Date < end)
            .Select(l => new { l.ProductId, l.SaleInvoiceLine.Product.CategoryId, l.SaleReturn.SaleInvoice.SalespersonId, l.Quantity, l.LineTotal,
                Cost = l.Condition == ReturnCondition.Sellable ? l.Quantity * l.UnitCost : 0 })
            .ToListAsync(ct);

        return sold.Select(x => (x.ProductId, x.CategoryId, x.SalespersonId, x.Quantity, x.NetTotal, x.Cost, x.SaleInvoiceId))
            .Concat(returned.Select(x => (x.ProductId, x.CategoryId, x.SalespersonId, -x.Quantity, -x.LineTotal, -x.Cost, 0)))
            .ToList();
    }

    public async Task<IReadOnlyList<ProductReportRow>> ProductsAsync(DateOnly from, DateOnly to, ProductSort sort, int take, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var facts = await LineFactsAsync(start, end, ct);
        var grouped = facts.GroupBy(f => f.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(f => f.Qty), Revenue = g.Sum(f => f.Revenue), Cost = g.Sum(f => f.Cost) })
            .Where(g => g.Qty != 0 || g.Revenue != 0)
            .ToList();
        var ordered = (sort switch
        {
            ProductSort.Quantity => grouped.OrderByDescending(g => g.Qty),
            ProductSort.Profit when Profit => grouped.OrderByDescending(g => g.Revenue - g.Cost),
            _ => grouped.OrderByDescending(g => g.Revenue),
        }).Take(Math.Clamp(take, 1, 500)).ToList();

        var ids = ordered.Select(o => o.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, p.Size, p.Color, p.Model, Category = p.Category.Name }).ToDictionaryAsync(p => p.Id, ct);

        return ordered.Select(o =>
        {
            var p = products[o.ProductId];
            var profit = CostMath.RoundMoney(o.Revenue - o.Cost);
            return new ProductReportRow(o.ProductId, p.Sku, Product.BuildDisplayName(p.Name, p.Size, p.Color, p.Model), p.Category, o.Qty, o.Revenue,
                Profit ? CostMath.RoundMoney(o.Cost) : null, Profit ? profit : null,
                Profit && o.Revenue != 0 ? Math.Round(profit / o.Revenue * 100, 1) : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<GroupReportRow>> CategoriesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var facts = await LineFactsAsync(start, end, ct);
        var names = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return facts.GroupBy(f => f.CategoryId)
            .Select(g => new GroupReportRow(g.Key, names.GetValueOrDefault(g.Key, "—"), g.Select(f => f.ProductId).Distinct().Count(),
                g.Sum(f => f.Qty), g.Sum(f => f.Revenue),
                Profit ? CostMath.RoundMoney(g.Sum(f => f.Cost)) : null, Profit ? CostMath.RoundMoney(g.Sum(f => f.Revenue - f.Cost)) : null))
            .OrderByDescending(r => Profit ? r.Profit : r.Revenue)
            .ToList();
    }

    public async Task<IReadOnlyList<GroupReportRow>> SalespeopleAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var facts = await LineFactsAsync(start, end, ct);
        // Invoice-level discounts are already inside each line's NetTotal, so revenue here matches net sales.
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return facts.GroupBy(f => f.SalespersonId)
            .Select(g => new GroupReportRow(g.Key, names.GetValueOrDefault(g.Key, "—"), g.Where(f => f.InvoiceId != 0).Select(f => f.InvoiceId).Distinct().Count(),
                g.Sum(f => f.Qty), g.Sum(f => f.Revenue),
                Profit ? CostMath.RoundMoney(g.Sum(f => f.Cost)) : null, Profit ? CostMath.RoundMoney(g.Sum(f => f.Revenue - f.Cost)) : null))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    // ------------------------------------------------------------------ positions

    public async Task<IReadOnlyList<BalanceRow>> ReceivablesAsync(CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().Where(c => c.Balance != 0).OrderByDescending(c => c.Balance)
            .Select(c => new BalanceRow(c.Id, c.Code, c.Name, c.Phone, c.Balance,
                db.CustomerLedgerEntries.Where(e => e.CustomerId == c.Id).Max(e => (DateTime?)e.Date)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BalanceRow>> PayablesAsync(CancellationToken ct = default) =>
        await db.Suppliers.AsNoTracking().Where(s => s.Balance != 0).OrderByDescending(s => s.Balance)
            .Select(s => new BalanceRow(s.Id, s.Code, s.Name, s.Phone, s.Balance,
                db.SupplierLedgerEntries.Where(e => e.SupplierId == s.Id).Max(e => (DateTime?)e.Date)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InventoryValueRow>> InventoryValueAsync(CancellationToken ct = default)
    {
        var rows = await db.Products.AsNoTracking().Where(p => !p.IsArchived)
            .Select(p => new { p.CategoryId, Category = p.Category.Name, Qty = p.StockBalances.Sum(b => (decimal?)b.Quantity) ?? 0, p.AverageCost })
            .ToListAsync(ct);
        return rows.GroupBy(r => new { r.CategoryId, r.Category })
            .Select(g => new InventoryValueRow(g.Key.CategoryId, g.Key.Category, g.Count(), g.Sum(r => r.Qty),
                Profit || Financial ? CostMath.RoundMoney(g.Sum(r => r.Qty * r.AverageCost)) : 0))
            .OrderByDescending(r => r.Value)
            .ToList();
    }
}
