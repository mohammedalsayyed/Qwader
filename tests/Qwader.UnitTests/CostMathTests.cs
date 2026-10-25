using FluentAssertions;
using Qwader.Domain.Inventory;

namespace Qwader.UnitTests;

public sealed class CostMathTests
{
    [Fact]
    public void Receipt_blends_the_new_cost_into_the_average() =>
        CostMath.AverageAfterReceipt(10, 10, 10, 20).Should().Be(15);

    [Fact]
    public void Receipt_into_empty_stock_takes_the_new_cost() =>
        CostMath.AverageAfterReceipt(0, 99, 5, 12.5m).Should().Be(12.5m);

    [Fact]
    public void Removal_at_a_specific_cost_reaverages_the_rest() =>
        CostMath.AverageAfterRemovalAtCost(15, 15, 5, 20).Should().Be(12.5m);

    [Fact]
    public void Removal_at_the_average_keeps_it() =>
        CostMath.AverageAfterRemovalAtCost(10, 7.25m, 4, 7.25m).Should().Be(7.25m);

    [Fact]
    public void Removing_everything_keeps_the_last_average() =>
        CostMath.AverageAfterRemovalAtCost(5, 9, 5, 11).Should().Be(9);

    [Theory]
    [InlineData(10, 25, 12.5)]
    [InlineData(8, 30, 10.5)]   // 10.4 → rounded up to the next half shekel
    [InlineData(0, 30, 0)]
    public void Suggested_price_applies_margin_and_rounds_up_to_half_shekel(decimal cost, decimal margin, decimal expected) =>
        CostMath.SuggestPrice(cost, margin).Should().Be(expected);

    [Fact]
    public void Discount_allocation_is_proportional_and_sums_exactly()
    {
        var shares = CostMath.AllocateDiscount([33.33m, 33.33m, 33.34m], 10);

        shares.Sum().Should().Be(10);
        shares[0].Should().Be(3.33m);
    }

    [Fact]
    public void No_discount_means_zero_shares() =>
        CostMath.AllocateDiscount([5m, 7m], 0).Should().AllBeEquivalentTo(0m);
}
