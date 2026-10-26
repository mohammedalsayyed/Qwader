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
