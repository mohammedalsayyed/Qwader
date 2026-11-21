namespace Qwader.Domain.Administration;

public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}

public static class SettingKeys
{
    public const string StoreName = "Store.Name";
    public const string StoreAddress = "Store.Address";
    public const string StorePhone = "Store.Phone";
    public const string InvoiceFooter = "Invoice.Footer";
    public const string DefaultMarginPercent = "Pricing.DefaultMarginPercent";
    public const string MaxDiscountPercent = "Sales.MaxDiscountPercent";
    public const string LowStockDefault = "Inventory.DefaultReorderLevel";
    /// <summary>"true": transfers taken at the till wait for verification (stay as debt); "false": they count as paid at once.</summary>
    public const string SaleTransfersPending = "Sales.TransfersPending";
    /// <summary>How long a device may keep selling without internet on the product data it last downloaded.</summary>
    public const string OfflineSaleMaxHours = "Sales.OfflineMaxHours";
    /// <summary>Pay period suggested for a new employee: Daily, Weekly or Monthly.</summary>
    public const string DefaultPayPeriod = "Payroll.DefaultPayPeriod";
    /// <summary>The business owners («أصحاب العمل»), one per line: «مصاريف صاحب العمل» are recorded and totalled per owner.</summary>
    public const string Owners = "Finance.Owners";
    /// <summary>Where a customer pays by transfer, printed on sale invoices: the bank or wallet, the holder, the account and phone.</summary>
    public const string PaymentBank = "Invoice.PaymentBank";
    public const string PaymentHolder = "Invoice.PaymentHolder";
    public const string PaymentAccount = "Invoice.PaymentAccount";
    public const string PaymentPhone = "Invoice.PaymentPhone";
    /// <summary>"true" (default): profits show as ••••• until the user's own password; "false": shown at once to those allowed.</summary>
    public const string ProfitsLocked = "Profits.Locked";
}

public enum DocumentType
{
    SaleInvoice = 1,
    SaleReturn = 2,
    PurchaseInvoice = 3,
    PurchaseReturn = 4,
    ReceiptVoucher = 5,
    PaymentVoucher = 6,
    IncomingTransfer = 7,
    AccountTransfer = 8,
    Expense = 9,
    StockCount = 10,
    StockAdjustment = 11,
    PayrollSettlement = 12,
    Customer = 20,
    Supplier = 21,
    Product = 22,
    Category = 23,
}

/// <summary>Per-type, per-year counter. Incremented under an update lock inside the caller's transaction.</summary>
public class DocumentSequence
{
    public DocumentType DocumentType { get; set; }
    public int Year { get; set; }
    public long LastNumber { get; set; }
}

/// <summary>Remembers the outcome of a state-changing request so a retried/double-clicked request returns the same result.</summary>
public class IdempotencyRecord
{
    public Guid Key { get; set; }
    public int UserId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? ResultType { get; set; }
    public string? ResultId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }
}
