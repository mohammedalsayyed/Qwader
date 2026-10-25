using Qwader.Domain.Catalog;
using Qwader.Domain.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;

namespace Qwader.Domain.Sales;

public class Customer : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    /// <summary>Cached amount the customer owes the store (Σ debit − Σ credit of the ledger).</summary>
    public decimal Balance { get; set; }

    /// <summary>The system «زبون نقدي» used for anonymous cash sales; it can never carry a balance.</summary>
    public bool IsWalkIn { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum CustomerEntryType
{
    Opening = 1,
    Sale = 2,
    Payment = 3,
    Return = 4,
    Refund = 5,
    Reversal = 6,
}

/// <summary>Customer sub-ledger. Debit = owes more; Credit = owes less.</summary>
public class CustomerLedgerEntry
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime Date { get; set; }
    public CustomerEntryType Type { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BalanceAfter { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public string? Description { get; set; }
    public int? UserId { get; set; }

    public Customer Customer { get; set; } = null!;
}

public enum PriceTier
{
    Retail = 1,
    Wholesale = 2,
}

public enum SaleStatus
{
    Posted = 1,
    PartiallyReturned = 2,
    Returned = 3,
    Voided = 4,
}

public class SaleInvoice : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public int WarehouseId { get; set; }
    public PriceTier PriceTier { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Posted;

    /// <summary>Σ quantity × unit price, before any discount («إجمالي المبيعات»).</summary>
    public decimal GrossTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal InvoiceDiscount { get; set; }
    /// <summary>What the customer owes for the invoice: gross − all discounts.</summary>
    public decimal Total { get; set; }
    /// <summary>Paid at the till (confirmed money only).</summary>
    public decimal PaidAmount { get; set; }
    /// <summary>Σ quantity × unit cost at the moment of sale.</summary>
    public decimal TotalCost { get; set; }

    public int SalespersonId { get; set; }
    public string? Notes { get; set; }

    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }

    public Customer Customer { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<SaleInvoiceLine> Lines { get; set; } = new List<SaleInvoiceLine>();
    public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
}

public class SaleInvoiceLine : Entity
{
    public int SaleInvoiceId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    /// <summary>Quantity × price − line discount.</summary>
    public decimal LineTotal { get; set; }
    /// <summary>LineTotal minus this line's share of the invoice discount — the revenue really earned (used for returns).</summary>
    public decimal NetTotal { get; set; }
    /// <summary>Weighted-average cost when sold; never changes afterwards.</summary>
    public decimal UnitCost { get; set; }
    public decimal ReturnedQuantity { get; set; }
    /// <summary>True when the price was below the minimum or the cost and a user with override permission allowed it.</summary>
    public bool PriceOverridden { get; set; }

    public SaleInvoice SaleInvoice { get; set; } = null!;
    public Product Product { get; set; } = null!;

    public decimal NetUnitPrice => Quantity == 0 ? 0 : NetTotal / Quantity;
}

public class SalePayment : Entity
{
    public int SaleInvoiceId { get; set; }
    public int FinancialAccountId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Transfer/wallet reference when the money did not arrive as cash.</summary>
    public string? Reference { get; set; }

    public SaleInvoice SaleInvoice { get; set; } = null!;
    public FinancialAccount FinancialAccount { get; set; } = null!;
}

public enum ReturnCondition
{
    /// <summary>Goes back on the shelf at its original cost.</summary>
    Sellable = 1,
    /// <summary>Not restocked; its cost is a loss.</summary>
    Damaged = 2,
}

public class SaleReturn : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public int SaleInvoiceId { get; set; }
    public int CustomerId { get; set; }
    public DateTime Date { get; set; }
    /// <summary>Revenue reversed (Σ net line values returned).</summary>
    public decimal Total { get; set; }
    /// <summary>Cost of the returned units (restocked or written off).</summary>
    public decimal TotalCost { get; set; }
    public ReturnSettlement Settlement { get; set; }
    public int? RefundAccountId { get; set; }
    public string? Reason { get; set; }

    public SaleInvoice SaleInvoice { get; set; } = null!;
    public ICollection<SaleReturnLine> Lines { get; set; } = new List<SaleReturnLine>();
}

public class SaleReturnLine : Entity
{
    public int SaleReturnId { get; set; }
    public int SaleInvoiceLineId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public ReturnCondition Condition { get; set; }
    public decimal LineTotal { get; set; }
    public decimal UnitCost { get; set; }

    public SaleReturn SaleReturn { get; set; } = null!;
    public SaleInvoiceLine SaleInvoiceLine { get; set; } = null!;
}
