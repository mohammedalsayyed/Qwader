using Qwader.Domain.Common;

namespace Qwader.Domain.Inventory;

public class Warehouse : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
