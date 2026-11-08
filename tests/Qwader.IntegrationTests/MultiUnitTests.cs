using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Catalog;
using Qwader.Application.Purchasing;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>One product sold by the piece and by the dozen: stock and cost stay in pieces.</summary>
[Collection(ApiCollection.Name)]
public sealed class MultiUnitTests(ApiFactory factory)
{
    private static async Task<int> UnitAsync(HttpClient client, string name)
    {
        var existing = (await client.LookupsAsync()).Units.FirstOrDefault(u => u.Name == name);
        if (existing is not null) return existing.Id;
        return (await (await client.PostAsJsonAsync("/api/lookups/units", new { name })).ReadAsync<Application.Lookups.LookupItem>()).Id;
    }

    /// <summary>A piece at 10 (cost 6 × 24 in stock) that is also sold as a dozen at 110 (floor 100).</summary>
    private async Task<(HttpClient Admin, ProductDetailDto Product, ProductUnitDto Dozen, int Cash)> SetupAsync(decimal opening = 24)
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: opening, openingCost: 6);
        var dozenUnit = await UnitAsync(admin, "دزينة");
        var lookups = await admin.LookupsAsync();
        var request = new SaveProductRequest(product.Name, product.Sku, null, product.CategoryId, null, product.UnitId, null, null,
            null, null, null, null, 6, 10, 10, 0, 2, null, Units: [new SaveProductUnit(dozenUnit, 12, 110, 105, 100)]);
        var updated = await (await admin.PutAsJsonAsync($"/api/products/{product.Id}", request)).ReadAsync<ProductDetailDto>();
        updated.Units.Should().ContainSingle();
        var cash = lookups.Accounts.First(a => a.Name == "الصندوق النقدي").Id;
        return (admin, updated, updated.Units![0], cash);
    }

    private static CreateSaleRequest Sale(IReadOnlyList<SaleLineInput> lines, IReadOnlyList<SalePaymentInput> payments) =>
        new(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0, lines, payments, null);

    [Fact]
    public async Task A_dozen_takes_twelve_pieces_at_their_cost_and_returns_and_voids_put_them_back()
    {
        var (admin, product, dozen, cash) = await SetupAsync();

        var sale = await (await admin.PostAsJsonAsync("/api/sales",
            Sale([new(product.Id, 1, 110, 0, dozen.Id), new(product.Id, 2, 10)], [new(cash, 130)]))).ReadAsync<SaleDetailDto>();

        sale.Total.Should().Be(130);
        sale.TotalCost.Should().Be(14 * 6);
        sale.Lines[0].UnitName.Should().Be("دزينة");
        sale.Lines[0].UnitFactor.Should().Be(12);
        sale.Lines[0].UnitCost.Should().Be(72);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(10);

        // The dozen comes back whole: 12 pieces at the piece's cost.
        var returned = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, cash, null,
            [new(sale.Lines[0].Id, 1, ReturnCondition.Sellable)]))).ReadAsync<SaleDetailDto>();
        returned.ReturnedTotal.Should().Be(110);
        var afterReturn = await admin.GetProductAsync(product.Id);
        afterReturn.Quantity.Should().Be(22);
        afterReturn.AverageCost.Should().Be(6);

        var second = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 110, 0, dozen.Id)], [new(cash, 110)]))).ReadAsync<SaleDetailDto>();
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(10);
        await (await admin.PostAsJsonAsync($"/api/sales/{second.Id}/void", new VoidRequest("خطأ"))).ReadAsync<SaleDetailDto>();
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(22);
    }

    [Fact]
    public async Task The_dozen_has_its_own_floor_and_cannot_exceed_stock_in_pieces()
    {
        var (admin, product, dozen, cash) = await SetupAsync(opening: 20);

        var belowFloor = await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 90, 0, dozen.Id)], [new(cash, 90)]));
        belowFloor.StatusCode.Should().Be((HttpStatusCode)422);

        var tooMany = await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 2, 110, 0, dozen.Id)], [new(cash, 220)]));
        tooMany.StatusCode.Should().Be((HttpStatusCode)422, "two dozens are 24 pieces and only 20 are in stock");
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(20);
    }

    [Fact]
    public async Task Buying_by_the_dozen_adds_pieces_at_the_piece_cost_and_best_sellers_count_pieces()
    {
        var (admin, product, dozen, cash) = await SetupAsync(opening: 0);
        var supplier = await admin.CreateSupplierAsync();

        var purchase = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 2, 96, 0, dozen.Id)]);
        purchase.Lines[0].UnitName.Should().Be("دزينة");
        var afterPurchase = await admin.GetProductAsync(product.Id);
        afterPurchase.Quantity.Should().Be(24);
        afterPurchase.AverageCost.Should().Be(8);
        afterPurchase.PurchasePrice.Should().Be(8);

        // Half a dozen back to the supplier: 6 pieces.
        await (await admin.PostAsJsonAsync($"/api/purchases/{purchase.Id}/returns", new PurchaseReturnRequest(Guid.NewGuid(), null,
            ReturnSettlement.ReduceBalance, null, null, [new ReturnLineInput(purchase.Lines[0].Id, 0.5m)]))).ReadAsync<PurchaseDetailDto>();
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(18);

        await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 110, 0, dozen.Id), new(product.Id, 1, 10)], [new(cash, 120)])))
            .ReadAsync<SaleDetailDto>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var rows = await (await admin.GetAsync($"/api/reports/products?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}&take=500"))
            .ReadAsync<List<ProductReportRow>>();
        rows.Single(r => r.ProductId == product.Id).Quantity.Should().Be(13);
    }
}
