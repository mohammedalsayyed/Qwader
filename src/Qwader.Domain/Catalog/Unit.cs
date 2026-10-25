using Qwader.Domain.Common;

namespace Qwader.Domain.Catalog;

public class Unit : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
