using Qwader.Domain.Common;

namespace Qwader.Domain.Catalog;

public class Unit : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Counted in whole numbers only (a piece, a set, a carton): 0.5 of it is refused on sales, purchases, returns and stock
    /// movements. Off by default — a metre or a kilo is sold in fractions — and switched on per unit in the settings.
    /// </summary>
    public bool WholeNumbersOnly { get; set; }
}
