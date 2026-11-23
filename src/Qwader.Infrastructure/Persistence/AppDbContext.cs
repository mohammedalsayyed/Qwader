using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Qwader.Application.Abstractions;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Common;
using Qwader.Domain.Files;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;
using Qwader.Domain.Notifications;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, IClock clock)
    : IdentityDbContext<AppUser, AppRole, int, IdentityUserClaim<int>, AppUserRole, IdentityUserLogin<int>,
        IdentityRoleClaim<int>, IdentityUserToken<int>>(options), IAppDbContext
{
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ClosedYear> ClosedYears => Set<ClosedYear>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryGroup> CategoryGroups => Set<CategoryGroup>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
    public DbSet<PurchaseInvoicePayment> PurchaseInvoicePayments => Set<PurchaseInvoicePayment>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceLine> SaleInvoiceLines => Set<SaleInvoiceLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnLine> SaleReturnLines => Set<SaleReturnLine>();

    public DbSet<IncomingTransfer> IncomingTransfers => Set<IncomingTransfer>();
    public DbSet<AccountTransfer> AccountTransfers => Set<AccountTransfer>();
    public DbSet<ReceiptVoucher> ReceiptVouchers => Set<ReceiptVoucher>();
    public DbSet<PaymentVoucher> PaymentVouchers => Set<PaymentVoucher>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ReceiptAllocation> ReceiptAllocations => Set<ReceiptAllocation>();
    public DbSet<OpeningCreditAllocation> OpeningCreditAllocations => Set<OpeningCreditAllocation>();
    public DbSet<Domain.Payroll.Employee> Employees => Set<Domain.Payroll.Employee>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<Domain.Payroll.PayrollEntry> PayrollEntries => Set<Domain.Payroll.PayrollEntry>();
    public DbSet<Domain.Payroll.PayrollSettlement> PayrollSettlements => Set<Domain.Payroll.PayrollSettlement>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Money defaults to 2 decimals; unit costs and quantities override this per property.
        builder.Properties<decimal>().HavePrecision(18, 2);
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        await GuardClosedYearsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        GuardClosedYearsAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges();
    }

    /// <summary>
    /// «إقفال السنة»: while it is on, nothing dated in a closed year (or any year before it) may be added, re-dated, voided,
    /// restored or deleted — every document, ledger entry and stock movement has a <c>Date</c>, so this one check covers them
    /// all, whichever screen saves them. Other changes to an old document (a debt collected today on last year's invoice, a
    /// return made today) are allowed: their own entries carry today's date.
    /// </summary>
    private async Task GuardClosedYearsAsync(CancellationToken ct)
    {
        var dated = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && e.Metadata.FindProperty("Date")?.ClrType == typeof(DateTime))
            .ToList();
        if (dated.Count == 0) return;
        if (await Settings.AsNoTracking().Where(s => s.Key == SettingKeys.YearClosing).Select(s => s.Value).FirstOrDefaultAsync(ct) != "true") return;
        var lockedThrough = await ClosedYears.AsNoTracking().Where(y => y.ReopenedAt == null).MaxAsync(y => (int?)y.Year, ct);
        if (lockedThrough is not { } last) return;

        bool Locked(object? utc) => utc is DateTime d && clock.ToLocal(d).Year <= last;
        static bool IsVoided(object? status) => status?.ToString() == "Voided";
        foreach (var e in dated)
        {
            var date = e.Property("Date");
            var refused = e.State switch
            {
                EntityState.Added => Locked(date.CurrentValue),
                EntityState.Deleted => Locked(date.OriginalValue),
                _ => (date.IsModified && (Locked(date.OriginalValue) || Locked(date.CurrentValue)))
                    || (Locked(date.OriginalValue)
                        && ((e.Metadata.FindProperty("Status") is not null && e.Property("Status") is { IsModified: true } s
                                && (IsVoided(s.OriginalValue) || IsVoided(s.CurrentValue)))
                            || (e.Metadata.FindProperty("VoidedAt") is not null && e.Property("VoidedAt").IsModified))),
            };
            if (refused)
            {
                var year = clock.ToLocal((DateTime)(e.State == EntityState.Added ? date.CurrentValue! : date.OriginalValue!)).Year;
                throw new Application.Common.BusinessRuleException(
                    $"سنة {Math.Min(year, last)} مقفلة: لا يمكن تسجيل أو تعديل أو إلغاء عملية بتاريخ فيها. سجّلها بتاريخ اليوم، أو أعد فتح السنة من الإعدادات.");
            }
        }
    }

    private void StampAuditFields()
    {
        var now = clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedById ??= currentUser.UserId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedById = currentUser.UserId;
                entry.Property(e => e.CreatedAt).IsModified = false;
                entry.Property(e => e.CreatedById).IsModified = false;
            }
        }
    }

    /// <summary>All timestamps are stored as UTC; this restores DateTimeKind.Utc on read so JSON carries the "Z".</summary>
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
