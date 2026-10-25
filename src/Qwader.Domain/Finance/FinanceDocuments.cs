using Qwader.Domain.Common;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Domain.Finance;

public enum TransferStatus
{
    /// <summary>Recorded but not yet seen in the bank/wallet: not money received.</summary>
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
}

/// <summary>A bank/wallet transfer someone says they sent. It only becomes money once a reviewer confirms it.</summary>
public class IncomingTransfer : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string NormalizedSenderName { get; set; } = string.Empty;
    public string? SenderPhone { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Bank or wallet the sender used (free text, e.g. «بنك فلسطين»).</summary>
    public string? Channel { get; set; }
    public int FinancialAccountId { get; set; }
    public string? ReferenceNo { get; set; }
    public DateTime TransferDate { get; set; }
    public Guid? ProofFileId { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;

    /// <summary>When set, confirmation reduces this customer's debt.</summary>
    public int? CustomerId { get; set; }
    /// <summary>Set when the transfer was taken as (part of) payment at the till.</summary>
    public int? SaleInvoiceId { get; set; }
    public string? Notes { get; set; }

    public int? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
    /// <summary>The receipt voucher created on confirmation.</summary>
    public int? ReceiptVoucherId { get; set; }

    public FinancialAccount FinancialAccount { get; set; } = null!;
    public Customer? Customer { get; set; }
}

/// <summary>Money moved between two of the store's own accounts: neither income nor expense.</summary>
public class AccountTransfer : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public int FromAccountId { get; set; }
    public int ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }

    public FinancialAccount FromAccount { get; set; } = null!;
    public FinancialAccount ToAccount { get; set; } = null!;
}

public enum VoucherStatus
{
    Posted = 1,
    Voided = 2,
}

public enum ReceiptPurpose
{
    /// <summary>A customer paying (part of) what they owe — not a new sale.</summary>
    DebtCollection = 1,
    OtherIncome = 2,
}

public class ReceiptVoucher : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string PayerName { get; set; } = string.Empty;
    public string? PayerPhone { get; set; }
    public int? CustomerId { get; set; }
    public int? SaleInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public int FinancialAccountId { get; set; }
    public ReceiptPurpose Purpose { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? AttachmentFileId { get; set; }
    public int? IncomingTransferId { get; set; }
    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }

    public FinancialAccount FinancialAccount { get; set; } = null!;
    public Customer? Customer { get; set; }
}

public enum PaymentPurpose
{
    SupplierPayment = 1,
    /// <summary>Created by an expense; the expense is what reports count.</summary>
    Expense = 2,
    Other = 3,
}

public class PaymentVoucher : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int FinancialAccountId { get; set; }
    public PaymentPurpose Purpose { get; set; }
    public int? SupplierId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? AttachmentFileId { get; set; }
    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }

    public FinancialAccount FinancialAccount { get; set; } = null!;
    public Supplier? Supplier { get; set; }
}

public class Expense : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int ExpenseCategoryId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int FinancialAccountId { get; set; }
    public Guid? AttachmentFileId { get; set; }
    /// <summary>Every expense is paid through exactly one payment voucher (no double counting).</summary>
    public int PaymentVoucherId { get; set; }
    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public ExpenseCategory Category { get; set; } = null!;
    public FinancialAccount FinancialAccount { get; set; } = null!;
    public PaymentVoucher PaymentVoucher { get; set; } = null!;
}
