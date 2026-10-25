using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Catalog;
using Qwader.Domain.Files;
using Qwader.Domain.Inventory;

namespace Qwader.Infrastructure.Persistence.Configurations;

internal static class Precision
{
    public static PropertyBuilder<decimal> AsCost(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 4);
    public static PropertyBuilder<decimal> AsQuantity(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 3);
    public static PropertyBuilder<decimal?> AsQuantity(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 3);
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(c => c.Code).HasMaxLength(30).IsRequired();
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.NormalizedName).HasMaxLength(100).IsRequired();
        b.Property(c => c.Description).HasMaxLength(500);
        b.HasIndex(c => c.Code).IsUnique();
        b.HasIndex(c => c.Name).IsUnique();
        b.HasIndex(c => c.NormalizedName);
    }
}

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        b.Property(p => p.Barcode).HasMaxLength(50);
        b.Property(p => p.Name).HasMaxLength(150).IsRequired();
        b.Property(p => p.NormalizedName).HasMaxLength(500).IsRequired();
        b.Property(p => p.Description).HasMaxLength(1000);
        b.Property(p => p.Size).HasMaxLength(50);
        b.Property(p => p.Color).HasMaxLength(50);
        b.Property(p => p.Model).HasMaxLength(50);
        b.Property(p => p.PurchasePrice).AsCost();
        b.Property(p => p.AverageCost).AsCost();
        b.Property(p => p.ReorderLevel).AsQuantity();
        b.Ignore(p => p.DisplayName);

        b.HasIndex(p => p.Sku).IsUnique();
        b.HasIndex(p => p.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        b.HasIndex(p => p.NormalizedName);
        b.HasIndex(p => new { p.CategoryId, p.IsArchived });

        b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Brand).WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Unit).WithMany().HasForeignKey(p => p.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.DefaultSupplier).WithMany().HasForeignKey(p => p.DefaultSupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.ParentProduct).WithMany(p => p.Variants).HasForeignKey(p => p.ParentProductId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_Prices",
                "[RetailPrice] >= 0 AND [WholesalePrice] >= 0 AND [MinSalePrice] >= 0 AND [PurchasePrice] >= 0 AND [AverageCost] >= 0");
            t.HasCheckConstraint("CK_Products_ReorderLevel", "[ReorderLevel] >= 0");
        });
    }
}

public sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.HasKey(s => new { s.ProductId, s.WarehouseId });
        b.Property(s => s.Quantity).AsQuantity();
        b.HasOne(s => s.Product).WithMany(p => p.StockBalances).HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(s => s.Warehouse).WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        // Last line of defence against negative stock, whatever code path writes it.
        b.ToTable(t => t.HasCheckConstraint("CK_StockBalances_NonNegative", "[Quantity] >= 0"));
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.Property(m => m.Quantity).AsQuantity();
        b.Property(m => m.BalanceAfter).AsQuantity();
        b.Property(m => m.UnitCost).AsCost();
        b.Property(m => m.AverageCostAfter).AsCost();
        b.Property(m => m.SourceType).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(m => m.SourceNumber).HasMaxLength(30);
        b.Property(m => m.Notes).HasMaxLength(500);
        b.HasIndex(m => new { m.ProductId, m.OccurredAt });
        b.HasIndex(m => new { m.SourceType, m.SourceId });
        b.HasIndex(m => new { m.Type, m.OccurredAt });
        b.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(m => m.Warehouse).WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> b)
    {
        b.Property(c => c.Number).HasMaxLength(30).IsRequired();
        b.Property(c => c.Notes).HasMaxLength(500);
        b.HasIndex(c => c.Number).IsUnique();
        b.HasOne(c => c.Warehouse).WithMany().HasForeignKey(c => c.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Lines).WithOne(l => l.StockCount).HasForeignKey(l => l.StockCountId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> b)
    {
        b.Property(l => l.ExpectedQuantity).AsQuantity();
        b.Property(l => l.CountedQuantity).AsQuantity();
        b.HasIndex(l => new { l.StockCountId, l.ProductId }).IsUnique();
        b.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.OriginalName).HasMaxLength(255).IsRequired();
        b.Property(f => f.ContentType).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(f => f.StoragePath).HasMaxLength(300).IsRequired();
    }
}
