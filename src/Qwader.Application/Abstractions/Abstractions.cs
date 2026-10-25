using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Files;
using Qwader.Domain.Finance;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Setting> Settings { get; }
    DbSet<DocumentSequence> DocumentSequences { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Unit> Units { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<FinancialAccount> FinancialAccounts { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<AccountTransaction> AccountTransactions { get; }

    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountLine> StockCountLines { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<SupplierLedgerEntry> SupplierLedgerEntries { get; }
    DbSet<PurchaseInvoice> PurchaseInvoices { get; }
    DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines { get; }
    DbSet<PurchaseReturn> PurchaseReturns { get; }
    DbSet<PurchaseReturnLine> PurchaseReturnLines { get; }

    DbSet<StoredFile> StoredFiles { get; }

    DbSet<Customer> Customers { get; }
    DbSet<CustomerLedgerEntry> CustomerLedgerEntries { get; }
    DbSet<SaleInvoice> SaleInvoices { get; }
    DbSet<SaleInvoiceLine> SaleInvoiceLines { get; }
    DbSet<SalePayment> SalePayments { get; }
    DbSet<SaleReturn> SaleReturns { get; }
    DbSet<SaleReturnLine> SaleReturnLines { get; }

    DbSet<IncomingTransfer> IncomingTransfers { get; }
    DbSet<AccountTransfer> AccountTransfers { get; }
    DbSet<ReceiptVoucher> ReceiptVouchers { get; }
    DbSet<PaymentVoucher> PaymentVouchers { get; }
    DbSet<Expense> Expenses { get; }

    /// <summary>Read-only use (names for history lists); user management goes through UserManager.</summary>
    DbSet<Domain.Identity.AppUser> Users { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    int? UserId { get; }
    string? UserName { get; }
    string? IpAddress { get; }
    bool HasPermission(string permission);

    int RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");
}

public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>Palestine local time zone (Asia/Hebron).</summary>
    TimeZoneInfo LocalZone { get; }

    DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), LocalZone);

    DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));

    /// <summary>UTC half-open range [start, end) covering local days <paramref name="from"/> through <paramref name="to"/> inclusive.</summary>
    (DateTime StartUtc, DateTime EndUtc) LocalDaysToUtcRange(DateOnly from, DateOnly to)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), LocalZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), LocalZone);
        return (start, end);
    }
}

public interface IDocumentNumberGenerator
{
    /// <summary>Allocates the next number for the type. Must run inside the caller's database transaction so a rollback releases nothing out of order.</summary>
    Task<string> NextAsync(DocumentType type, CancellationToken ct = default);
}

public interface IAuditService
{
    /// <summary>Queues an audit entry on the current unit of work; it is persisted by the same SaveChanges as the change itself.</summary>
    void Log(string action, string entityType, string? entityId, object? oldValues = null, object? newValues = null, string? reason = null);
}

public sealed record FileUpload(Stream Content, string FileName, string ContentType, long Length);

public interface IFileStorage
{
    /// <summary>Validates (type by content, size) and stores the file; the metadata row is queued on the current unit of work.</summary>
    Task<StoredFile> SaveAsync(FileUpload upload, FilePurpose purpose, CancellationToken ct = default);
    Task<(StoredFile File, Stream Content)?> OpenAsync(Guid id, CancellationToken ct = default);
}

public sealed record ExcelColumn(string Header, int Width = 16);

public interface IExcelService
{
    byte[] Write(string sheetName, IReadOnlyList<ExcelColumn> columns, IEnumerable<object?[]> rows);

    /// <summary>Reads the first sheet; header row values become the dictionary keys of every following row.</summary>
    IReadOnlyList<(int RowNumber, IReadOnlyDictionary<string, string?> Cells)> Read(Stream content);
}

public interface IIdempotencyService
{
    Task<IdempotencyRecord?> FindAsync(Guid key, string operation, CancellationToken ct = default);

    /// <summary>Queues the record on the current unit of work. A unique key guarantees a concurrent duplicate fails to commit.</summary>
    void Record(Guid key, string operation, string resultType, string resultId);
}
