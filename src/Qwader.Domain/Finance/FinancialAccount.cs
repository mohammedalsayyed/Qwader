using Qwader.Domain.Common;

namespace Qwader.Domain.Finance;

public enum FinancialAccountType
{
    Cash = 1,
    Bank = 2,
    Wallet = 3,
}

public class FinancialAccount : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; }
    public string? ProviderName { get; set; }
    public string? HolderName { get; set; }
    public string? AccountNumber { get; set; }
    public decimal OpeningBalance { get; set; }

    /// <summary>Cached: OpeningBalance + Σ transactions. Only ever changed by the ledger service inside a transaction.</summary>
    public decimal CurrentBalance { get; set; }

    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public class ExpenseCategory : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
