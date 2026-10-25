namespace Qwader.Domain.Finance;

public enum MoneyDirection
{
    In = 1,
    Out = 2,
}

public enum AccountTransactionCategory
{
    SaleReceipt = 1,
    DebtCollection = 2,
    OtherIncome = 3,
    SupplierPayment = 10,
    SupplierRefund = 11,
    Expense = 12,
    SaleRefund = 13,
    TransferIn = 20,
    TransferOut = 21,
    Reversal = 30,
}

/// <summary>The only record of money moving in or out of a financial account. Never edited or deleted.</summary>
public class AccountTransaction
{
    public long Id { get; set; }
    public int FinancialAccountId { get; set; }
    public DateTime Date { get; set; }
    public MoneyDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public AccountTransactionCategory Category { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public string? Description { get; set; }
    public int? UserId { get; set; }

    public FinancialAccount FinancialAccount { get; set; } = null!;
}
