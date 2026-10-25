using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Ledgers;

public sealed record LedgerSource(string SourceType, int? SourceId, string? SourceNumber, string? Description);

public interface IAccountLedger
{
    /// <summary>Moves money in/out of a financial account and records the transaction. Returns the balance after.</summary>
    Task<decimal> PostAsync(int accountId, MoneyDirection direction, decimal amount, AccountTransactionCategory category,
        LedgerSource source, DateTime date, CancellationToken ct = default);
}

public interface ISupplierLedger
{
    /// <summary>Credit increases what the store owes the supplier; debit decreases it. Returns the balance after.</summary>
    Task<decimal> PostAsync(int supplierId, SupplierEntryType type, decimal debit, decimal credit,
        LedgerSource source, DateTime date, CancellationToken ct = default);
}

public sealed class AccountLedger(IAppDbContext db, ICurrentUser currentUser) : IAccountLedger
{
    public async Task<decimal> PostAsync(int accountId, MoneyDirection direction, decimal amount, AccountTransactionCategory category,
        LedgerSource source, DateTime date, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Account postings must run inside a database transaction.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        var delta = direction == MoneyDirection.In ? amount : -amount;

        // Atomic increment; the row lock is held until the caller commits.
        var balances = await db.Database.SqlQuery<decimal>($"""
            UPDATE FinancialAccounts SET CurrentBalance = CurrentBalance + {delta}
            OUTPUT inserted.CurrentBalance AS Value
            WHERE Id = {accountId} AND IsActive = 1;
            """).ToListAsync(ct);
        if (balances.Count == 0) throw new BusinessRuleException("الحساب المالي المختار غير موجود أو معطّل");

        db.AccountTransactions.Add(new AccountTransaction
        {
            FinancialAccountId = accountId,
            Date = date,
            Direction = direction,
            Amount = amount,
            BalanceAfter = balances[0],
            Category = category,
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceNumber = source.SourceNumber,
            Description = source.Description,
            UserId = currentUser.UserId,
        });
        return balances[0];
    }
}

public sealed class SupplierLedger(IAppDbContext db, ICurrentUser currentUser) : ISupplierLedger
{
    public async Task<decimal> PostAsync(int supplierId, SupplierEntryType type, decimal debit, decimal credit,
        LedgerSource source, DateTime date, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Supplier postings must run inside a database transaction.");
        if (debit < 0 || credit < 0 || debit + credit == 0) throw new ArgumentException("Exactly a positive debit or credit is required.");

        var delta = credit - debit;
        var balances = await db.Database.SqlQuery<decimal>($"""
            UPDATE Suppliers SET Balance = Balance + {delta}
            OUTPUT inserted.Balance AS Value
            WHERE Id = {supplierId};
            """).ToListAsync(ct);
        if (balances.Count == 0) throw new NotFoundException("المورد غير موجود");

        db.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            SupplierId = supplierId,
            Date = date,
            Type = type,
            Debit = debit,
            Credit = credit,
            BalanceAfter = balances[0],
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceNumber = source.SourceNumber,
            Description = source.Description,
            UserId = currentUser.UserId,
        });
        return balances[0];
    }
}

public interface ICustomerLedger
{
    /// <summary>Debit increases what the customer owes; credit decreases it. Returns the balance after.</summary>
    Task<decimal> PostAsync(int customerId, Domain.Sales.CustomerEntryType type, decimal debit, decimal credit,
        LedgerSource source, DateTime date, CancellationToken ct = default);
}

public sealed class CustomerLedger(IAppDbContext db, ICurrentUser currentUser) : ICustomerLedger
{
    public async Task<decimal> PostAsync(int customerId, Domain.Sales.CustomerEntryType type, decimal debit, decimal credit,
        LedgerSource source, DateTime date, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Customer postings must run inside a database transaction.");
        if (debit < 0 || credit < 0 || debit + credit == 0) throw new ArgumentException("Exactly a positive debit or credit is required.");

        var delta = debit - credit;
        // The walk-in customer never carries a balance; the guard makes that a database-level guarantee.
        var balances = await db.Database.SqlQuery<decimal>($"""
            UPDATE Customers SET Balance = Balance + {delta}
            OUTPUT inserted.Balance AS Value
            WHERE Id = {customerId} AND IsWalkIn = 0;
            """).ToListAsync(ct);
        if (balances.Count == 0) throw new BusinessRuleException("لا يمكن تسجيل دين على «زبون نقدي»؛ اختر عميلًا أو أضف اسمه ورقم جواله");

        db.CustomerLedgerEntries.Add(new Domain.Sales.CustomerLedgerEntry
        {
            CustomerId = customerId,
            Date = date,
            Type = type,
            Debit = debit,
            Credit = credit,
            BalanceAfter = balances[0],
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceNumber = source.SourceNumber,
            Description = source.Description,
            UserId = currentUser.UserId,
        });
        return balances[0];
    }
}
