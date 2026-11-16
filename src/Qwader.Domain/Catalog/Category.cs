using Qwader.Domain.Common;

namespace Qwader.Domain.Catalog;

public class Category : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>The general category it belongs to («أجهزة كهربائية», «أدوات منزلية»); its products follow it.</summary>
    public int? GroupId { get; set; }

    public CategoryGroup? Group { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>«الصنف العام»: a general category above the categories, for filtering and stock totals.</summary>
public class CategoryGroup : Entity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<Category> Categories { get; set; } = new List<Category>();
}

public class Brand : Entity
{
    public string Name { get; set; } = string.Empty;
}
