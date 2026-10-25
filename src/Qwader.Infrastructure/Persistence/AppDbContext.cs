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
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
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

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
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
