using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(s => s.Code).HasMaxLength(30).IsRequired();
        b.Property(s => s.Name).HasMaxLength(150).IsRequired();
        b.Property(s => s.NormalizedName).HasMaxLength(150).IsRequired();
        b.Property(s => s.Phone).HasMaxLength(20);
        b.Property(s => s.Address).HasMaxLength(200);
        b.Property(s => s.Notes).HasMaxLength(500);
        b.HasIndex(s => s.Code).IsUnique();
        b.HasIndex(s => s.NormalizedName);
        b.HasIndex(s => s.Phone);
    }
}

public sealed class SupplierLedgerEntryConfiguration : IEntityTypeConfiguration<SupplierLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplierLedgerEntry> b)
    {
        b.Property(e => e.SourceType).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(e => e.SourceNumber).HasMaxLength(30);
        b.Property(e => e.Description).HasMaxLength(300);
        b.HasIndex(e => new { e.SupplierId, e.Date });
        b.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_SupplierLedger_OneSided", "[Debit] >= 0 AND [Credit] >= 0"));
    }
}

public sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> b)
    {
        b.Property(p => p.Number).HasMaxLength(30).IsRequired();
        b.Property(p => p.SupplierInvoiceNo).HasMaxLength(50);
        b.Property(p => p.Notes).HasMaxLength(500);
        b.Property(p => p.VoidReason).HasMaxLength(300);
        b.HasIndex(p => p.Number).IsUnique();
        b.HasIndex(p => new { p.SupplierId, p.Date });
        b.HasIndex(p => new { p.Status, p.Date });
        b.HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Warehouse).WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.PaymentAccount).WithMany().HasForeignKey(p => p.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(p => p.Lines).WithOne(l => l.PurchaseInvoice).HasForeignKey(l => l.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("CK_PurchaseInvoices_Amounts", "[Discount] >= 0 AND [Total] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Total]"));
    }
}

public sealed class PurchaseInvoiceLineConfiguration : IEntityTypeConfiguration<PurchaseInvoiceLine>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLine> b)
    {
        b.Property(l => l.Quantity).AsQuantity();
        b.Property(l => l.ReturnedQuantity).AsQuantity();
        b.Property(l => l.UnitCost).AsCost();
        b.Property(l => l.NetUnitCost).AsCost();
        b.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => l.ProductId);
        b.ToTable(t => t.HasCheckConstraint("CK_PurchaseLines_Quantities", "[Quantity] > 0 AND [ReturnedQuantity] >= 0 AND [ReturnedQuantity] <= [Quantity]"));
    }
}

public sealed class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> b)
    {
        b.Property(r => r.Number).HasMaxLength(30).IsRequired();
        b.Property(r => r.Reason).HasMaxLength(300);
        b.HasIndex(r => r.Number).IsUnique();
        b.HasOne(r => r.PurchaseInvoice).WithMany().HasForeignKey(r => r.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinancialAccount>().WithMany().HasForeignKey(r => r.RefundAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Lines).WithOne(l => l.PurchaseReturn).HasForeignKey(l => l.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PurchaseReturnLineConfiguration : IEntityTypeConfiguration<PurchaseReturnLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnLine> b)
    {
        b.Property(l => l.Quantity).AsQuantity();
        b.Property(l => l.UnitCost).AsCost();
        b.HasOne(l => l.PurchaseInvoiceLine).WithMany().HasForeignKey(l => l.PurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountTransactionConfiguration : IEntityTypeConfiguration<AccountTransaction>
{
    public void Configure(EntityTypeBuilder<AccountTransaction> b)
    {
        b.Property(t => t.SourceType).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(t => t.SourceNumber).HasMaxLength(30);
        b.Property(t => t.Description).HasMaxLength(300);
        b.HasIndex(t => new { t.FinancialAccountId, t.Date });
        b.HasIndex(t => new { t.SourceType, t.SourceId });
        b.HasIndex(t => new { t.Category, t.Date });
        b.HasOne(t => t.FinancialAccount).WithMany().HasForeignKey(t => t.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_AccountTransactions_Positive", "[Amount] > 0"));
    }
}
