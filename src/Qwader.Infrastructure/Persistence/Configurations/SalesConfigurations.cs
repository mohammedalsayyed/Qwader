using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(c => c.Code).HasMaxLength(30).IsRequired();
        b.Property(c => c.Name).HasMaxLength(150).IsRequired();
        b.Property(c => c.NormalizedName).HasMaxLength(150).IsRequired();
        b.Property(c => c.Phone).HasMaxLength(20);
        b.Property(c => c.Address).HasMaxLength(200);
        b.Property(c => c.Notes).HasMaxLength(500);
        b.HasIndex(c => c.Code).IsUnique();
        b.HasIndex(c => c.NormalizedName);
        // One customer per phone number, so the till can find or create customers by phone.
        b.HasIndex(c => c.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL");
        b.HasIndex(c => c.IsWalkIn).IsUnique().HasFilter("[IsWalkIn] = 1");
    }
}

public sealed class CustomerLedgerEntryConfiguration : IEntityTypeConfiguration<CustomerLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CustomerLedgerEntry> b)
    {
        b.Property(e => e.SourceType).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(e => e.SourceNumber).HasMaxLength(30);
        b.Property(e => e.Description).HasMaxLength(300);
        b.HasIndex(e => new { e.CustomerId, e.Date });
        b.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_CustomerLedger_OneSided", "[Debit] >= 0 AND [Credit] >= 0"));
    }
}

public sealed class SaleInvoiceConfiguration : IEntityTypeConfiguration<SaleInvoice>
{
    public void Configure(EntityTypeBuilder<SaleInvoice> b)
    {
        b.Property(s => s.Number).HasMaxLength(30).IsRequired();
        b.Property(s => s.CustomerName).HasMaxLength(150).IsRequired();
        b.Property(s => s.CustomerPhone).HasMaxLength(20);
        b.Property(s => s.Notes).HasMaxLength(500);
        b.Property(s => s.VoidReason).HasMaxLength(300);
        b.Property(s => s.TotalCost).AsCostMoney();
        b.HasIndex(s => s.Number).IsUnique();
        b.HasIndex(s => s.Date);
        b.HasIndex(s => new { s.CustomerId, s.Date });
        b.HasIndex(s => new { s.SalespersonId, s.Date });
        b.HasIndex(s => new { s.Status, s.Date });
        b.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(s => s.Warehouse).WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(s => s.SalespersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Lines).WithOne(l => l.SaleInvoice).HasForeignKey(l => l.SaleInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(s => s.Payments).WithOne(p => p.SaleInvoice).HasForeignKey(p => p.SaleInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("CK_SaleInvoices_Amounts",
            "[GrossTotal] >= 0 AND [LineDiscountTotal] >= 0 AND [InvoiceDiscount] >= 0 AND [Total] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Total]"));
    }
}

public sealed class SaleInvoiceLineConfiguration : IEntityTypeConfiguration<SaleInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SaleInvoiceLine> b)
    {
        b.Property(l => l.ProductName).HasMaxLength(300).IsRequired();
        b.Property(l => l.Quantity).AsQuantity();
        b.Property(l => l.ReturnedQuantity).AsQuantity();
        b.Property(l => l.UnitCost).AsCost();
        b.Ignore(l => l.NetUnitPrice);
        b.HasIndex(l => l.ProductId);
        b.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_SaleLines_Quantities", "[Quantity] > 0 AND [ReturnedQuantity] >= 0 AND [ReturnedQuantity] <= [Quantity]"));
    }
}

public sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        b.Property(p => p.Reference).HasMaxLength(100);
        b.HasOne(p => p.FinancialAccount).WithMany().HasForeignKey(p => p.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_SalePayments_Positive", "[Amount] > 0"));
    }
}

public sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> b)
    {
        b.Property(r => r.Number).HasMaxLength(30).IsRequired();
        b.Property(r => r.Reason).HasMaxLength(300);
        b.Property(r => r.TotalCost).AsCostMoney();
        b.HasIndex(r => r.Number).IsUnique();
        b.HasIndex(r => r.Date);
        b.HasOne(r => r.SaleInvoice).WithMany().HasForeignKey(r => r.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinancialAccount>().WithMany().HasForeignKey(r => r.RefundAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Lines).WithOne(l => l.SaleReturn).HasForeignKey(l => l.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SaleReturnLineConfiguration : IEntityTypeConfiguration<SaleReturnLine>
{
    public void Configure(EntityTypeBuilder<SaleReturnLine> b)
    {
        b.Property(l => l.Quantity).AsQuantity();
        b.Property(l => l.UnitCost).AsCost();
        b.HasOne(l => l.SaleInvoiceLine).WithMany().HasForeignKey(l => l.SaleInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
