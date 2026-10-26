using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class IncomingTransferConfiguration : IEntityTypeConfiguration<IncomingTransfer>
{
    public void Configure(EntityTypeBuilder<IncomingTransfer> b)
    {
        b.Property(t => t.Number).HasMaxLength(30).IsRequired();
        b.Property(t => t.SenderName).HasMaxLength(150).IsRequired();
        b.Property(t => t.NormalizedSenderName).HasMaxLength(150).IsRequired();
        b.Property(t => t.SenderPhone).HasMaxLength(20);
        b.Property(t => t.Channel).HasMaxLength(100);
        b.Property(t => t.ReferenceNo).HasMaxLength(100);
        b.Property(t => t.Notes).HasMaxLength(500);
        b.Property(t => t.RejectReason).HasMaxLength(300);
        b.HasIndex(t => t.Number).IsUnique();
        b.HasIndex(t => new { t.Status, t.TransferDate });
        b.HasIndex(t => t.NormalizedSenderName);
        // The same reference can never be counted twice in one account (rejected attempts don't block a correct re-entry).
        b.HasIndex(t => new { t.FinancialAccountId, t.ReferenceNo }).IsUnique()
            .HasFilter("[ReferenceNo] IS NOT NULL AND [Status] <> 3").HasDatabaseName("UX_IncomingTransfers_Reference");
        b.HasOne(t => t.FinancialAccount).WithMany().HasForeignKey(t => t.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(t => t.Customer).WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SaleInvoice>().WithMany().HasForeignKey(t => t.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_IncomingTransfers_Amount", "[Amount] > 0"));
    }
}

public sealed class AccountTransferConfiguration : IEntityTypeConfiguration<AccountTransfer>
{
    public void Configure(EntityTypeBuilder<AccountTransfer> b)
    {
        b.Property(t => t.Number).HasMaxLength(30).IsRequired();
        b.Property(t => t.Notes).HasMaxLength(300);
        b.HasIndex(t => t.Number).IsUnique();
        b.HasOne(t => t.FromAccount).WithMany().HasForeignKey(t => t.FromAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(t => t.ToAccount).WithMany().HasForeignKey(t => t.ToAccountId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_AccountTransfers_Valid", "[Amount] > 0 AND [FromAccountId] <> [ToAccountId]"));
    }
}

public sealed class ReceiptAllocationConfiguration : IEntityTypeConfiguration<ReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<ReceiptAllocation> b)
    {
        b.Property(a => a.Amount).HasPrecision(18, 2);
        b.HasOne<ReceiptVoucher>().WithMany().HasForeignKey(a => a.ReceiptVoucherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Qwader.Domain.Sales.Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Qwader.Domain.Sales.SaleInvoice>().WithMany().HasForeignKey(a => a.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => new { a.CustomerId, a.SaleInvoiceId });
    }
}

public sealed class ReceiptVoucherConfiguration : IEntityTypeConfiguration<ReceiptVoucher>
{
    public void Configure(EntityTypeBuilder<ReceiptVoucher> b)
    {
        b.Property(v => v.Number).HasMaxLength(30).IsRequired();
        b.Property(v => v.PayerName).HasMaxLength(150).IsRequired();
        b.Property(v => v.PayerPhone).HasMaxLength(20);
        b.Property(v => v.Reference).HasMaxLength(100);
        b.Property(v => v.Notes).HasMaxLength(500);
        b.Property(v => v.VoidReason).HasMaxLength(300);
        b.HasIndex(v => v.Number).IsUnique();
        b.HasIndex(v => v.Date);
        b.HasIndex(v => new { v.CustomerId, v.Date });
        b.HasOne(v => v.FinancialAccount).WithMany().HasForeignKey(v => v.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(v => v.Customer).WithMany().HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SaleInvoice>().WithMany().HasForeignKey(v => v.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_ReceiptVouchers_Amount", "[Amount] > 0"));
    }
}

public sealed class PaymentVoucherConfiguration : IEntityTypeConfiguration<PaymentVoucher>
{
    public void Configure(EntityTypeBuilder<PaymentVoucher> b)
    {
        b.Property(v => v.Number).HasMaxLength(30).IsRequired();
        b.Property(v => v.BeneficiaryName).HasMaxLength(150).IsRequired();
        b.Property(v => v.Reference).HasMaxLength(100);
        b.Property(v => v.Notes).HasMaxLength(500);
        b.Property(v => v.VoidReason).HasMaxLength(300);
        b.HasIndex(v => v.Number).IsUnique();
        b.HasIndex(v => v.Date);
        b.HasIndex(v => new { v.SupplierId, v.Date });
        b.HasOne(v => v.FinancialAccount).WithMany().HasForeignKey(v => v.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(v => v.Supplier).WithMany().HasForeignKey(v => v.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_PaymentVouchers_Amount", "[Amount] > 0"));
    }
}

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.Property(e => e.Number).HasMaxLength(30).IsRequired();
        b.Property(e => e.Description).HasMaxLength(300).IsRequired();
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => new { e.Date, e.ExpenseCategoryId });
        b.HasIndex(e => e.PaymentVoucherId).IsUnique();
        b.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.FinancialAccount).WithMany().HasForeignKey(e => e.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.PaymentVoucher).WithMany().HasForeignKey(e => e.PaymentVoucherId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_Expenses_Amount", "[Amount] > 0"));
    }
}
