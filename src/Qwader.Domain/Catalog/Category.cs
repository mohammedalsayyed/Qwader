using Qwader.Domain.Common;

namespace Qwader.Domain.Catalog;

public class Category : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Brand : Entity
{
    public string Name { get; set; } = string.Empty;
}
