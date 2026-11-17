using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Catalog;
using Qwader.Application.Inventory;
using Qwader.Application.Reports;

namespace Qwader.IntegrationTests;

/// <summary>«تفكيك / تجميع»: a set becomes its pieces (and back) at the same value; nothing is gained or lost.</summary>
[Collection(ApiCollection.Name)]
public sealed class StockConversionTests(ApiFactory factory)
{
    [Fact]
    public async Task Sets_become_pieces_at_the_same_value_and_the_books_do_not_move()
    {
        var admin = await factory.LoginAsync();
        var sets = await admin.CreateProductAsync(name: TestData.Unique("طقم فناجين"));
        var cups = await admin.CreateProductAsync(name: TestData.Unique("فنجان"));
        var supplier = await admin.CreateSupplierAsync();
        await admin.PurchaseAsync(supplier.Id, [new(sets.Id, 20, 60)]); // 20 sets at 60 = 1200
        async Task<ProductDetailDto> Get(int id) => await admin.GetProductAsync(id);
        async Task<BalanceSheetDto> Sheet() => await (await admin.GetAsync("/api/reports/balance-sheet")).ReadAsync<BalanceSheetDto>();
        var before = await Sheet();

        // 5 sets → 30 cups: 300 of value moves, each cup at 10.
        var result = await (await admin.PostAsJsonAsync("/api/inventory/conversions",
            new StockConversionRequest(Guid.NewGuid(), sets.Id, 5, cups.Id, 30, "بيع بالمفرد", ToRetailPrice: 18, ToWholesalePrice: 16, ToMinSalePrice: 12)))
            .ReadAsync<StockConversionResult>();
        result.Value.Should().Be(300);
        result.ToUnitCost.Should().Be(10);
        (await Get(sets.Id)).Quantity.Should().Be(15);
        var c = await Get(cups.Id);
        c.Quantity.Should().Be(30);
        c.AverageCost.Should().Be(10);
        // Its prices follow: the last purchase price is the set's ÷ 6, the selling prices as given.
        (c.PurchasePrice, c.RetailPrice, c.WholesalePrice, c.MinSalePrice).Should().Be((10m, 18m, 16m, 12m));
        // A minimum above the retail price is refused.
        (await admin.PostAsJsonAsync("/api/inventory/conversions",
            new StockConversionRequest(Guid.NewGuid(), sets.Id, 1, cups.Id, 6, null, ToRetailPrice: 10, ToMinSalePrice: 12))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await Sheet();
        after.TotalAssets.Should().Be(before.TotalAssets, "the stock's value does not change");
        after.Difference.Should().Be(before.Difference);

        // Back: 6 cups → 1 set at 60.
        await (await admin.PostAsJsonAsync("/api/inventory/conversions",
            new StockConversionRequest(Guid.NewGuid(), cups.Id, 6, sets.Id, 1, null))).ReadAsync<StockConversionResult>();
        (await Get(sets.Id)).Quantity.Should().Be(16);
        (await Get(sets.Id)).AverageCost.Should().Be(60);
        (await Get(cups.Id)).Quantity.Should().Be(24);

        // More than there is, or the same product, is refused and moves nothing.
        (await admin.PostAsJsonAsync("/api/inventory/conversions", new StockConversionRequest(Guid.NewGuid(), sets.Id, 100, cups.Id, 600, null)))
            .IsSuccessStatusCode.Should().BeFalse();
        (await admin.PostAsJsonAsync("/api/inventory/conversions", new StockConversionRequest(Guid.NewGuid(), sets.Id, 1, sets.Id, 6, null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Get(sets.Id)).Quantity.Should().Be(16);
    }
}
