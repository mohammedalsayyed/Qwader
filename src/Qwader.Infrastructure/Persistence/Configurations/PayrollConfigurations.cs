using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Finance;
using Qwader.Domain.Payroll;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.Property(e => e.Notes).HasMaxLength(500);
        b.HasIndex(e => e.UserId).IsUnique();
        b.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_Employees_Rate", "[Rate] >= 0"));
    }
}

public sealed class PayrollEntryConfiguration : IEntityTypeConfiguration<PayrollEntry>
{
    public void Configure(EntityTypeBuilder<PayrollEntry> b)
    {
        b.Property(e => e.Notes).HasMaxLength(300);
        b.Property(e => e.VoidReason).HasMaxLength(300);
        b.HasIndex(e => new { e.EmployeeId, e.SettlementId, e.Status });
        b.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Expense>().WithMany().HasForeignKey(e => e.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollSettlement>().WithMany().HasForeignKey(e => e.SettlementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollSettlement>().WithMany().HasForeignKey(e => e.CarriedFromSettlementId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_PayrollEntries_Amount", "[Amount] > 0"));
    }
}

public sealed class PayrollSettlementConfiguration : IEntityTypeConfiguration<PayrollSettlement>
{
    public void Configure(EntityTypeBuilder<PayrollSettlement> b)
    {
        b.Property(s => s.Number).HasMaxLength(30).IsRequired();
        b.Property(s => s.Notes).HasMaxLength(300);
        b.Property(s => s.VoidReason).HasMaxLength(300);
        b.Property(s => s.Units).HasPrecision(18, 3);
        b.HasIndex(s => s.Number).IsUnique();
        b.HasIndex(s => new { s.EmployeeId, s.PeriodFrom });
        b.HasOne(s => s.Employee).WithMany().HasForeignKey(s => s.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinancialAccount>().WithMany().HasForeignKey(s => s.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Expense>().WithMany().HasForeignKey(s => s.ExpenseId).OnDelete(DeleteBehavior.Restrict);
    }
}
