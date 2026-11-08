using Qwader.Domain.Common;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;

namespace Qwader.Domain.Catalog;

public class Product : AuditableEntity
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalized name + variant attributes, used for searching.</summary>
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public int? DefaultSupplierId { get; set; }
    public Supplier? DefaultSupplier { get; set; }

    /// <summary>Set when this product is a variant (size/colour/model) of another product.</summary>
    public int? ParentProductId { get; set; }
    public Product? ParentProduct { get; set; }
    public ICollection<Product> Variants { get; set; } = new List<Product>();
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Model { get; set; }

    /// <summary>Last purchase unit cost (informational).</summary>
    public decimal PurchasePrice { get; set; }

    /// <summary>Moving weighted-average cost. Only the inventory ledger changes it.</summary>
    public decimal AverageCost { get; set; }

    public decimal WholesalePrice { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal MinSalePrice { get; set; }
    public decimal ReorderLevel { get; set; }

    public Guid? ImageFileId { get; set; }
    public bool IsArchived { get; set; }

    public ICollection<StockBalance> StockBalances { get; set; } = new List<StockBalance>();

    /// <summary>Larger packs the product is also sold or bought in (a dozen of a piece). Stock stays in the base unit.</summary>
    public ICollection<ProductUnit> Units { get; set; } = new List<ProductUnit>();

    public string DisplayName => BuildDisplayName(Name, Size, Color, Model);

    public static string BuildDisplayName(string name, string? size, string? color, string? model)
    {
        var attributes = new[] { model, color, size }.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        return attributes.Length == 0 ? name : $"{name} — {string.Join(" / ", attributes)}";
    }
}

/// <summary>
/// Another unit the same product is sold or bought in: «دزينة» = 12 of the base unit. Stock, cost and reports stay in
/// the base unit; a line in this unit moves Quantity × Factor of it. Prices are its own (typed, with a suggestion of Factor × base price).
/// </summary>
public class ProductUnit : Entity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    /// <summary>How many base units one of this unit holds (greater than 1).</summary>
    public decimal Factor { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal WholesalePrice { get; set; }
    public decimal MinSalePrice { get; set; }
}
