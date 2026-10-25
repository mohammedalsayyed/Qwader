using Qwader.Domain.Catalog;
using Qwader.Domain.Common;

namespace Qwader.Domain.Inventory;

public class StockBalance
{
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public decimal Quantity { get; set; }

    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}

public enum StockMovementType
{
    OpeningBalance = 1,
    Purchase = 2,
    PurchaseReturn = 3,
    PurchaseVoid = 4,
    Sale = 10,
    SaleReturn = 11,
    SaleVoid = 12,
    Damaged = 20,
    Lost = 21,
    Found = 22,
    CountIncrease = 23,
    CountDecrease = 24,
}

public static class StockMovementTypes
{
    /// <summary>Movements whose value is a stock loss (or, for gains, a negative loss) in the P&amp;L.</summary>
    public static readonly StockMovementType[] Shrinkage =
        [StockMovementType.Damaged, StockMovementType.Lost, StockMovementType.Found, StockMovementType.CountIncrease, StockMovementType.CountDecrease];
}

/// <summary>Immutable record of one change to a product's stock. Quantity is signed: + in, − out.</summary>
public class StockMovement
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal AverageCostAfter { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime OccurredAt { get; set; }
    public int? UserId { get; set; }

    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}

public enum StockCountStatus
{
    Open = 1,
    Posted = 2,
    Cancelled = 3,
}

public class StockCount : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public StockCountStatus Status { get; set; } = StockCountStatus.Open;
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}

public class StockCountLine : Entity
{
    public int StockCountId { get; set; }
    public int ProductId { get; set; }

    /// <summary>System quantity captured when posting (the expected balance at that moment).</summary>
    public decimal? ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }

    public StockCount StockCount { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
