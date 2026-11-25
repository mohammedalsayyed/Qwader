namespace Qwader.Domain.Inventory;

/// <summary>Moving weighted-average cost rules (see docs/DESIGN.md §4.1).</summary>
public static class CostMath
{
    public const int CostDecimals = 4;
    public const int MoneyDecimals = 2;

    public static decimal RoundCost(decimal value) => Math.Round(value, CostDecimals, MidpointRounding.AwayFromZero);
    public static decimal RoundMoney(decimal value) => Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The store's rule for the average cost: up to the next half shekel (21.3043 → 21.50, 21.50 stays). Only a cost typed in
    /// «تصحيح التكلفة» is kept exactly as typed. Rounded to 4 decimals first so a division's noise does not push it up a step.
    /// </summary>
    public static decimal RoundUpToHalf(decimal value) => value <= 0 ? 0 : Math.Ceiling(RoundCost(value) * 2) / 2;

    /// <summary>Average after receiving <paramref name="inQuantity"/> units at <paramref name="inUnitCost"/> (up to the next half shekel).</summary>
    public static decimal AverageAfterReceipt(decimal onHand, decimal currentAverage, decimal inQuantity, decimal inUnitCost)
    {
        if (inQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(inQuantity));
        // A negative or zero balance has no meaningful value to blend with.
        var exact = onHand <= 0 ? RoundCost(inUnitCost) : RoundCost((onHand * currentAverage + inQuantity * inUnitCost) / (onHand + inQuantity));
        // Goods coming back at the average (a return, a voided sale) leave it as it is — also a cost corrected by hand.
        return exact == currentAverage ? currentAverage : RoundUpToHalf(exact);
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
        if (value <= 0) return 0;
        var exact = RoundCost(value / remaining);
        return exact == currentAverage ? currentAverage : RoundUpToHalf(exact);
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
