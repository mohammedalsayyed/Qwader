namespace Qwader.Domain.Inventory;

/// <summary>Moving weighted-average cost rules (see docs/DESIGN.md §4.1).</summary>
public static class CostMath
{
    public const int CostDecimals = 4;
    public const int MoneyDecimals = 2;

    public static decimal RoundCost(decimal value) => Math.Round(value, CostDecimals, MidpointRounding.AwayFromZero);
    public static decimal RoundMoney(decimal value) => Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

    /// <summary>Average after receiving <paramref name="inQuantity"/> units at <paramref name="inUnitCost"/>.</summary>
    public static decimal AverageAfterReceipt(decimal onHand, decimal currentAverage, decimal inQuantity, decimal inUnitCost)
    {
        if (inQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(inQuantity));
        // A negative or zero balance has no meaningful value to blend with.
        if (onHand <= 0) return RoundCost(inUnitCost);
        return RoundCost((onHand * currentAverage + inQuantity * inUnitCost) / (onHand + inQuantity));
    }

    /// <summary>
    /// Average after removing units at a specific cost (purchase return). Removing at the average leaves it unchanged;
    /// emptying the stock keeps the last average.
    /// </summary>
    public static decimal AverageAfterRemovalAtCost(decimal onHand, decimal currentAverage, decimal outQuantity, decimal outUnitCost)
    {
        var remaining = onHand - outQuantity;
        if (remaining <= 0) return currentAverage;
        var value = onHand * currentAverage - outQuantity * outUnitCost;
        return value <= 0 ? 0 : RoundCost(value / remaining);
    }

    /// <summary>Selling price that yields <paramref name="marginPercent"/> markup on cost, rounded up to 0.5 ₪.</summary>
    public static decimal SuggestPrice(decimal cost, decimal marginPercent)
    {
        if (cost <= 0) return 0;
        var raw = cost * (1 + marginPercent / 100m);
        return Math.Ceiling(raw * 2) / 2;
    }

    /// <summary>
    /// Spreads an invoice-level discount over lines in proportion to their totals. The last line absorbs rounding,
    /// so the shares always sum exactly to the discount.
    /// </summary>
    public static decimal[] AllocateDiscount(IReadOnlyList<decimal> lineTotals, decimal discount)
    {
        var shares = new decimal[lineTotals.Count];
        var sum = lineTotals.Sum();
        if (discount == 0 || sum == 0) return shares;

        decimal allocated = 0;
        for (var i = 0; i < lineTotals.Count; i++)
        {
            shares[i] = i == lineTotals.Count - 1 ? discount - allocated : RoundMoney(discount * lineTotals[i] / sum);
            allocated += shares[i];
        }
        return shares;
    }
}
