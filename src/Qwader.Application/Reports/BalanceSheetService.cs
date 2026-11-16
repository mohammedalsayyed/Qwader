using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Application.Reports;

/// <summary>One line of the balance sheet. <paramref name="Link"/>: where the figure comes from in the app.</summary>
public sealed record BalanceLine(string Name, decimal Amount, string? Link = null);

/// <summary>A group of lines (current assets, current liabilities…) and its total.</summary>
public sealed record BalanceSection(string Title, IReadOnlyList<BalanceLine> Lines, decimal Total);

/// <summary>
/// «الميزانية العمومية» at the end of a day. Assets and liabilities are the balances of the store's own ledgers (accounts,
/// customers, suppliers, stock, employee advances); equity is built separately from what made it (opening balances, capital
/// added or withdrawn, profit), so <see cref="Difference"/> = assets − liabilities − equity is a real check, never forced to 0.
/// </summary>
public sealed record BalanceSheetDto(
    DateOnly AsOf, DateOnly YearStart,
    IReadOnlyList<BalanceSection> Assets, decimal TotalAssets,
    IReadOnlyList<BalanceSection> Liabilities, decimal TotalLiabilities,
    IReadOnlyList<BalanceSection> Equity, decimal TotalEquity,
    decimal Difference,
    /// <summary>How the year's net profit is made (sales, cost of goods, expenses…).</summary>
    IReadOnlyList<BalanceLine> YearProfit,
    /// <summary>Items a balance sheet usually has that the system does not record yet (shown as such, not as 0).</summary>
    IReadOnlyList<string> NotRecorded);

public interface IBalanceSheetService
{
    Task<BalanceSheetDto> AsOfAsync(DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Builds the balance sheet from the existing ledgers — no parallel bookkeeping:
/// <list type="bullet">
/// <item>money: <c>AccountTransactions</c> (a voided document's reversal counts as its own kind: a cancelled withdrawal is not a withdrawal);</item>
/// <item>customers and suppliers: their ledgers (a customer in credit is a liability, a supplier paid ahead an asset);</item>
/// <item>stock: the last movement of each product and warehouse (quantity × weighted average cost at that moment);</item>
/// <item>employee advances: paid out less what settled salaries took.</item>
/// </list>
/// </summary>
public sealed class BalanceSheetService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IBalanceSheetService
{
    private static readonly StockMovementType[] SaleTypes =
        [StockMovementType.Sale, StockMovementType.SaleReturn, StockMovementType.SaleVoid, StockMovementType.SaleReturnVoid, StockMovementType.SaleRestore];

    private static readonly StockMovementType[] AdjustmentTypes =
        [StockMovementType.Damaged, StockMovementType.Lost, StockMovementType.Found, StockMovementType.CountIncrease, StockMovementType.CountDecrease, StockMovementType.ArchiveWriteOff];

    private const string Salaries = "الرواتب";

    public async Task<BalanceSheetDto> AsOfAsync(DateOnly asOf, CancellationToken ct = default)
    {
        if (!currentUser.HasPermission(Permissions.ReportsFinancial)) throw new ForbiddenException();
        if (asOf > clock.LocalToday) throw new ValidationFailedException("date", "اختر يومًا حتى اليوم");
        var yearStart = new DateOnly(asOf.Year, 1, 1);
        var (_, end) = clock.LocalDaysToUtcRange(asOf, asOf);
        var (yearStartUtc, _) = clock.LocalDaysToUtcRange(yearStart, yearStart);
        bool ThisYear(DateTime d) => d >= yearStartUtc;

        // ---------------------------------------------------------------- money
        var txs = await db.AccountTransactions.AsNoTracking().Where(t => t.Date < end)
            .Select(t => new { t.FinancialAccountId, t.Date, t.Category, t.Direction, t.Amount, t.SourceType, t.SourceId })
            .ToListAsync(ct);
        // A reversal belongs to what it reverses: a withdrawal cancelled is no withdrawal.
        var originals = txs.Where(t => t.Category != AccountTransactionCategory.Reversal)
            .GroupBy(t => (t.SourceType, t.SourceId, t.FinancialAccountId))
            .ToDictionary(g => g.Key, g => g.First().Category);
        var money = txs.Select(t => new
        {
            t.FinancialAccountId, t.Date, t.SourceId, t.SourceType,
            Signed = t.Direction == MoneyDirection.In ? t.Amount : -t.Amount,
            Kind = t.Category == AccountTransactionCategory.Reversal
                ? originals.GetValueOrDefault((t.SourceType, t.SourceId, t.FinancialAccountId), AccountTransactionCategory.Reversal)
                : t.Category,
        }).ToList();

        var accounts = await db.FinancialAccounts.AsNoTracking().OrderBy(a => a.Type).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Name, a.Type, a.IsActive }).ToListAsync(ct);
        var balances = money.GroupBy(m => m.FinancialAccountId).ToDictionary(g => g.Key, g => g.Sum(m => m.Signed));
        BalanceLine[] AccountsOf(FinancialAccountType type) => accounts
            .Where(a => a.Type == type && (a.IsActive || balances.GetValueOrDefault(a.Id) != 0))
            .Select(a => new BalanceLine(a.Name, balances.GetValueOrDefault(a.Id), $"/accounts/{a.Id}")).ToArray();

        decimal Money(AccountTransactionCategory kind, bool? thisYear = null) =>
            money.Where(m => m.Kind == kind && (thisYear is null || ThisYear(m.Date) == thisYear)).Sum(m => m.Signed);

        // Employee advances still owed: paid out, less what settled salaries took off.
        var advancesPaid = -Money(AccountTransactionCategory.EmployeeAdvance);
        var settlements = await db.PayrollSettlements.AsNoTracking()
            .Where(s => s.Date < end && (s.Status == VoucherStatus.Posted || s.VoidedAt >= end) && s.AdvanceExpense > 0)
            .Select(s => new { s.Date, s.AdvanceExpense }).ToListAsync(ct);
        var advancesOwed = advancesPaid - settlements.Sum(s => s.AdvanceExpense);

        // ---------------------------------------------------------------- customers and suppliers
        var customerRows = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.Date < end)
            .GroupBy(e => e.CustomerId).Select(g => g.Sum(e => e.Debit - e.Credit)).ToListAsync(ct);
        var receivables = customerRows.Where(b => b > 0).Sum();
        var customerCredits = -customerRows.Where(b => b < 0).Sum();
        var customerOpening = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.Date < end && e.Type == CustomerEntryType.Opening)
            .SumAsync(e => (decimal?)(e.Debit - e.Credit), ct) ?? 0;

        var supplierRows = await db.SupplierLedgerEntries.AsNoTracking().Where(e => e.Date < end)
            .GroupBy(e => e.SupplierId).Select(g => g.Sum(e => e.Credit - e.Debit)).ToListAsync(ct);
        var payables = supplierRows.Where(b => b > 0).Sum();
        var supplierAdvances = -supplierRows.Where(b => b < 0).Sum();
        var supplierOpening = await db.SupplierLedgerEntries.AsNoTracking().Where(e => e.Date < end && e.Type == SupplierEntryType.Opening)
            .SumAsync(e => (decimal?)(e.Credit - e.Debit), ct) ?? 0;

        // ---------------------------------------------------------------- stock
        var lastIds = db.StockMovements.Where(m => m.OccurredAt < end).GroupBy(m => new { m.ProductId, m.WarehouseId }).Select(g => g.Max(m => m.Id));
        var inventory = CostMath.RoundMoney(await db.StockMovements.AsNoTracking().Where(m => lastIds.Contains(m.Id))
            .SumAsync(m => (decimal?)(m.BalanceAfter * m.AverageCostAfter), ct) ?? 0);
        var movements = await db.StockMovements.AsNoTracking().Where(m => m.OccurredAt < end)
            .GroupBy(m => new { m.Type, ThisYear = m.OccurredAt >= yearStartUtc })
            .Select(g => new
            {
                g.Key.Type, g.Key.ThisYear,
                Value = g.Sum(m => m.Quantity * m.UnitCost),
            }).ToListAsync(ct);
        // What each movement did to the value of the stock beyond its own quantity × cost: a cost correction (no quantity, a new
        // average) and goods bought again after the stock went below zero (the average restarts at the new cost, so pieces sold
        // before they arrived are re-costed) — plus rounding of the average. Each is the value after the movement less the value
        // before it and less quantity × cost, from the recorded rows, so it is shown, not assumed.
        var revaluations = await db.Database.SqlQuery<RevaluationRow>($"""
            SELECT x.Type AS Type, CAST(SUM(x.Residual) AS decimal(18, 4)) AS Value FROM (
                SELECT m.Type, m.BalanceAfter * m.AverageCostAfter
                    - ISNULL(LAG(m.BalanceAfter * m.AverageCostAfter) OVER (PARTITION BY m.ProductId, m.WarehouseId ORDER BY m.Id), 0)
                    - m.Quantity * m.UnitCost AS Residual
                FROM StockMovements m WHERE m.OccurredAt < {end}) x
            GROUP BY x.Type
            """).ToListAsync(ct);
        decimal Stock(Func<StockMovementType, bool> of, bool? thisYear = null) =>
            movements.Where(m => of(m.Type) && (thisYear is null || m.ThisYear == thisYear)).Sum(m => m.Value);
        var stockOpening = Stock(t => t == StockMovementType.OpeningBalance);
        var stockAdjustments = Stock(AdjustmentTypes.Contains);
        var costCorrections = revaluations.Where(r => r.Type == (int)StockMovementType.CostCorrection).Sum(r => r.Value);
        var revaluationDifferences = revaluations.Where(r => r.Type != (int)StockMovementType.CostCorrection).Sum(r => r.Value);

        // ---------------------------------------------------------------- profit (cumulative and this year)
        var invoices = await db.SaleInvoices.AsNoTracking()
            .Where(s => s.Date < end && (s.Status != SaleStatus.Voided || s.VoidedAt >= end))
            .GroupBy(s => s.Date >= yearStartUtc).Select(g => new { ThisYear = g.Key, Total = g.Sum(s => s.Total) }).ToListAsync(ct);
        var returns = await db.SaleReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Date < end && (r.VoidedAt == null || r.VoidedAt >= end))
            .GroupBy(r => r.Date >= yearStartUtc).Select(g => new { ThisYear = g.Key, Total = g.Sum(r => r.Total) }).ToListAsync(ct);

        // Expenses as paid from the accounts (a voided one's reversal takes it back), by category so salaries stand apart.
        var expenseCategories = await db.Expenses.AsNoTracking()
            .Select(e => new { e.PaymentVoucherId, Category = e.Category.Name }).ToDictionaryAsync(e => e.PaymentVoucherId, e => e.Category, ct);
        var expenseMoney = money.Where(m => m.Kind == AccountTransactionCategory.Expense).ToList();
        decimal Expenses(bool salaries, bool? thisYear = null) => -expenseMoney
            .Where(m => (m.SourceType == "PaymentVoucher" && m.SourceId is { } id && expenseCategories.GetValueOrDefault(id) == Salaries) == salaries
                && (thisYear is null || ThisYear(m.Date) == thisYear))
            .Sum(m => m.Signed);
        decimal AdvanceSalaries(bool? thisYear = null) =>
            settlements.Where(s => thisYear is null || ThisYear(s.Date) == thisYear).Sum(s => s.AdvanceExpense);

        decimal Profit(bool? thisYear)
        {
            var sales = invoices.Where(i => thisYear is null || i.ThisYear == thisYear).Sum(i => i.Total)
                - returns.Where(r => thisYear is null || r.ThisYear == thisYear).Sum(r => r.Total);
            var cogs = -Stock(SaleTypes.Contains, thisYear);
            return sales - cogs - Expenses(false, thisYear) - Expenses(true, thisYear) - AdvanceSalaries(thisYear)
                + Money(AccountTransactionCategory.OtherIncome, thisYear) + Money(AccountTransactionCategory.OtherPayment, thisYear);
        }

        var yearSales = invoices.Where(i => i.ThisYear).Sum(i => i.Total) - returns.Where(r => r.ThisYear).Sum(r => r.Total);
        var yearCogs = -Stock(SaleTypes.Contains, true);
        var yearProfit = Profit(true);
        var priorProfit = Profit(false);

        // ---------------------------------------------------------------- the sheet
        static BalanceSection Section(string title, params BalanceLine[] lines) =>
            new(title, lines, lines.Sum(l => l.Amount));

        var assets = new[]
        {
            Section("الصندوق والنقدية", AccountsOf(FinancialAccountType.Cash)),
            Section("الحسابات البنكية", AccountsOf(FinancialAccountType.Bank)),
            Section("المحافظ الإلكترونية", AccountsOf(FinancialAccountType.Wallet)),
            Section("أصول متداولة أخرى",
                new BalanceLine("حسابات العملاء (الذمم المدينة)", receivables, "/reports"),
                new BalanceLine("المخزون (بالمتوسط المرجّح)", inventory, "/inventory"),
                new BalanceLine("سلف الموظفين", advancesOwed, "/payroll"),
                new BalanceLine("دفعات مقدمة للموردين", supplierAdvances, "/suppliers")),
        }.Where(s => s.Lines.Count > 0).ToList();

        var liabilities = new List<BalanceSection>
        {
            Section("الالتزامات المتداولة",
                new BalanceLine("حسابات الموردين (الذمم الدائنة)", payables, "/suppliers"),
                new BalanceLine("دفعات مقدمة وأرصدة للعملاء", customerCredits, "/customers")),
        };

        var openingCapital = Money(AccountTransactionCategory.OpeningBalance) + stockOpening + customerOpening - supplierOpening;
        var equity = new List<BalanceSection>
        {
            Section("حقوق الملكية",
                new BalanceLine("رأس المال الافتتاحي (أرصدة بداية النظام)", openingCapital),
                new BalanceLine("رأس مال مضاف", Money(AccountTransactionCategory.CapitalContribution)),
                new BalanceLine("المسحوبات الشخصية (مصاريف صاحب العمل)", Money(AccountTransactionCategory.OwnerWithdrawal), "/owner-expenses"),
                new BalanceLine($"الأرباح المحتجزة (قبل {yearStart.Year})", priorProfit),
                new BalanceLine($"صافي ربح {yearStart.Year} حتى التاريخ", yearProfit),
                new BalanceLine("تسويات المخزون (تالف، مفقود، فائض جرد)", stockAdjustments, "/inventory?tab=movements"),
                new BalanceLine("تصحيح تكلفة المخزون", CostMath.RoundMoney(costCorrections), "/inventory?tab=movements"),
                new BalanceLine("فروق تقييم المخزون (شراء بعد رصيد سالب، تقريب المتوسط)", CostMath.RoundMoney(revaluationDifferences))),
        };

        var yearLines = new List<BalanceLine>
        {
            new("صافي المبيعات (بعد المرتجعات)", yearSales),
            new("تكلفة البضاعة المباعة", -yearCogs),
            new("المصروفات", -Expenses(false, true)),
            new("الرواتب", -Expenses(true, true) - AdvanceSalaries(true)),
            new("إيرادات أخرى", Money(AccountTransactionCategory.OtherIncome, true)),
            new("مصروفات أخرى (سندات صرف)", Money(AccountTransactionCategory.OtherPayment, true)),
            new("صافي الربح", yearProfit),
        };

        var totalAssets = CostMath.RoundMoney(assets.Sum(s => s.Total));
        var totalLiabilities = CostMath.RoundMoney(liabilities.Sum(s => s.Total));
        var totalEquity = CostMath.RoundMoney(equity.Sum(s => s.Total));
        return new BalanceSheetDto(asOf, yearStart, assets, totalAssets, liabilities, totalLiabilities, equity, totalEquity,
            totalAssets - totalLiabilities - totalEquity, yearLines,
            ["الأصول الثابتة (أثاث، أجهزة، معدات) ومجمع الإهلاك", "المصروفات المدفوعة مقدمًا", "المصروفات المستحقة", "القروض قصيرة وطويلة الأجل"]);
    }
}

/// <summary>What one kind of stock movement did to the stock's value beyond quantity × cost (raw SQL result).</summary>
public sealed class RevaluationRow
{
    public int Type { get; set; }
    public decimal Value { get; set; }
}
