using Qwader.Domain.Catalog;
using Qwader.Domain.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Inventory;

namespace Qwader.Domain.Purchasing;

public class Supplier : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    /// <summary>Cached amount the store owes the supplier (Σ credit − Σ debit of the ledger).</summary>
    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum PurchaseStatus
{
    Draft = 1,
    Posted = 2,
    Voided = 3,
}

public class PurchaseInvoice : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public string? SupplierInvoiceNo { get; set; }
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime Date { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    /// <summary>Σ line totals (after line discounts), before the invoice discount.</summary>
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    /// <summary>Paid when the invoice was posted. Later payments are payment vouchers.</summary>
    public decimal PaidAmount { get; set; }
    public int? PaymentAccountId { get; set; }
    public string? Notes { get; set; }

    public DateTime? PostedAt { get; set; }
    public int? PostedById { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }

    public Supplier Supplier { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public FinancialAccount? PaymentAccount { get; set; }
    public ICollection<PurchaseInvoiceLine> Lines { get; set; } = new List<PurchaseInvoiceLine>();
}

public class PurchaseInvoiceLine : Entity
{
    public int PurchaseInvoiceId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Unit cost after this line's share of the invoice discount — the cost that enters stock.</summary>
    public decimal NetUnitCost { get; set; }
    public decimal ReturnedQuantity { get; set; }

    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public enum ReturnSettlement
{
    /// <summary>Reduce what we owe the supplier.</summary>
    ReduceBalance = 1,
    /// <summary>Money comes back into one of our accounts.</summary>
    Refund = 2,
}

public class PurchaseReturn : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public int PurchaseInvoiceId { get; set; }
    public int SupplierId { get; set; }
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public ReturnSettlement Settlement { get; set; }
    public int? RefundAccountId { get; set; }
    public string? Reason { get; set; }

    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public ICollection<PurchaseReturnLine> Lines { get; set; } = new List<PurchaseReturnLine>();
}

public class PurchaseReturnLine : Entity
{
    public int PurchaseReturnId { get; set; }
    public int PurchaseInvoiceLineId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public PurchaseReturn PurchaseReturn { get; set; } = null!;
    public PurchaseInvoiceLine PurchaseInvoiceLine { get; set; } = null!;
}

public enum SupplierEntryType
{
    Opening = 1,
    Purchase = 2,
    Payment = 3,
    Return = 4,
    Reversal = 5,
    Refund = 6,
}

/// <summary>Supplier sub-ledger. Credit = we owe more; Debit = we owe less.</summary>
public class SupplierLedgerEntry
{
    public long Id { get; set; }
    public int SupplierId { get; set; }
    public DateTime Date { get; set; }
    public SupplierEntryType Type { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BalanceAfter { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public string? Description { get; set; }
    public int? UserId { get; set; }

    public Supplier Supplier { get; set; } = null!;
}
