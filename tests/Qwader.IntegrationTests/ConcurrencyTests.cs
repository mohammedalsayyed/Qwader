using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Finance;
using Qwader.Application.Inventory;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

/// <summary>The same request several times at the same moment (double taps, two tabs, retries): done once, the others refused.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyTests(ApiFactory factory)
{
    private const int Copies = 5;

    private async Task<int> DrawerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("درج"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;

    private async Task<decimal> BalanceAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.CurrentBalance).FirstAsync();
    }

    private static async Task<HttpStatusCode[]> AllAtOnceAsync(Func<Task<HttpResponseMessage>> send) =>
        (await Task.WhenAll(Enumerable.Range(0, Copies).Select(_ => Task.Run(send)))).Select(r => r.StatusCode).ToArray();

    private static CreateSaleRequest Sale(int productId, decimal quantity, int account, decimal paid) =>
        new(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0, [new(productId, quantity, 5)], [new(account, paid)], null);

    [Fact]
    public async Task A_sale_voided_from_several_places_at_once_is_reversed_once()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 5, openingQuantity: 10, openingCost: 2);
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 1, drawer, 5))).ReadAsync<SaleDetailDto>();

        var codes = await AllAtOnceAsync(() => admin.PostAsJsonAsync($"/api/sales/{sale.Id}/void", new VoidRequest("خطأ")));
        codes.Count(c => c == HttpStatusCode.OK).Should().Be(1);
        (await BalanceAsync(drawer)).Should().Be(0);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(10);
    }

    [Fact]
    public async Task Returns_sent_at_once_never_take_back_more_than_was_sold()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 5, openingQuantity: 10, openingCost: 2);
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 3, drawer, 15))).ReadAsync<SaleDetailDto>();

        var codes = await AllAtOnceAsync(() => admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 2)])));
        codes.Count(c => c == HttpStatusCode.OK).Should().Be(1, "two of the three units can be returned once only");
        (await BalanceAsync(drawer)).Should().Be(5);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(9);
    }

    [Fact]
    public async Task A_purchase_posted_or_voided_at_once_happens_once()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        await admin.FundAsync(drawer, 100);
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 10);
        var draft = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 24, 5)], paid: 50, accountId: drawer, post: false);

        var posted = await AllAtOnceAsync(() => admin.PostAsJsonAsync($"/api/purchases/{draft.Id}/post", new PostPurchaseRequest(Guid.NewGuid())));
        posted.Count(c => c == HttpStatusCode.OK).Should().Be(1);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(24);
        (await BalanceAsync(drawer)).Should().Be(50);

        var voided = await AllAtOnceAsync(() => admin.PostAsJsonAsync($"/api/purchases/{draft.Id}/void", new VoidRequest("خطأ")));
        voided.Count(c => c == HttpStatusCode.OK).Should().Be(1);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(0);
        (await BalanceAsync(drawer)).Should().Be(100);
    }

    [Fact]
    public async Task A_count_posted_at_once_applies_its_difference_once_and_a_receipt_is_voided_once()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 24, openingCost: 3);
        var count = await (await admin.PostAsJsonAsync("/api/inventory/counts", new CreateStockCountRequest(null, null, [product.Id], null))).ReadAsync<StockCountDto>();
        await (await admin.PutAsJsonAsync($"/api/inventory/counts/{count.Id}", new SaveCountsRequest([new(count.Lines[0].Id, 20)]))).ReadAsync<StockCountDto>();
        var posted = await AllAtOnceAsync(() => admin.PostAsJsonAsync($"/api/inventory/counts/{count.Id}/post", new { }));
        posted.Count(c => c == HttpStatusCode.OK).Should().Be(1);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(20);

        var drawer = await DrawerAsync(admin);
        var receipt = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.OtherIncome, null,
            "دخل", null, 40, drawer, null, null, null))).ReadAsync<VoucherDto>();
        await admin.FundAsync(drawer, 200);
        var voided = await AllAtOnceAsync(() => admin.PostAsJsonAsync($"/api/receipt-vouchers/{receipt.Id}/void", new VoidDocumentRequest("خطأ")));
        voided.Count(c => c == HttpStatusCode.OK).Should().Be(1);
        (await BalanceAsync(drawer)).Should().Be(200);
    }
}
