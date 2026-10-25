using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Catalog;
using Qwader.Domain.Finance;
using Qwader.Domain.Inventory;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.Property(u => u.Name).HasMaxLength(30).IsRequired();
        b.HasIndex(u => u.Name).IsUnique();
    }
}

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.Property(w => w.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(w => w.Name).IsUnique();
        // At most one default warehouse.
        b.HasIndex(w => w.IsDefault).IsUnique().HasFilter("[IsDefault] = 1");
    }
}

public sealed class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> b)
    {
        b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        b.Property(a => a.ProviderName).HasMaxLength(100);
        b.Property(a => a.HolderName).HasMaxLength(100);
        b.Property(a => a.AccountNumber).HasMaxLength(50);
        b.Property(a => a.RowVersion).IsRowVersion();
        b.HasIndex(a => a.Name).IsUnique();
    }
}

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.Property(c => c.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(c => c.Name).IsUnique();
    }
}
