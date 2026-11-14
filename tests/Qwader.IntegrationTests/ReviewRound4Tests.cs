using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

/// <summary>Fourth round of the store's test report: money that must never move twice or more than was paid.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReviewRound4Tests(ApiFactory factory)
{
    private async Task<int> DrawerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("درج"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;

    private async Task<decimal> BalanceAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.CurrentBalance).FirstAsync();
    }

    private static async Task<CustomerDto> CustomerAsync(HttpClient admin) =>
        await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("جولة4"), "055" + Random.Shared.Next(1000000, 9999999), null, null)))
            .ReadAsync<CustomerDto>();

    [Fact]
    public async Task A_used_key_with_other_goods_is_refused_and_the_same_sale_at_once_answers_its_invoice()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 15, openingQuantity: 10, openingCost: 5);
        var key = Guid.NewGuid();
        var first = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(key, PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 15)], [new(drawer, 15)], null))).ReadAsync<SaleDetailDto>();

        // Another tab with the same cart key sells two: refused (never answered with the first tab's invoice).
        var other = await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(key, PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 2, 15)], [new(drawer, 30)], null));
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(9);

        // The very same sale again (a retry): its invoice.
        var again = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(key, PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 15)], [new(drawer, 15)], null))).ReadAsync<SaleDetailDto>();
        again.Id.Should().Be(first.Id);

        // Sent three times at the same moment: every answer is the one invoice, none is an error.
        var request = new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0, [new(product.Id, 1, 15)], [new(drawer, 15)], null);
        var answers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => admin.PostAsJsonAsync("/api/sales", request))));
        answers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.OK);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(8);
    }

    [Fact]
    public async Task A_refund_never_gives_back_more_than_was_paid_on_the_invoice()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var customer = await CustomerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 15, openingQuantity: 10, openingCost: 5);
        // 45, paid 20, owes 25.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(product.Id, 3, 15)], [new(drawer, 20)], null))).ReadAsync<SaleDetailDto>();

        var all = await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 3)]));
        all.StatusCode.Should().Be((HttpStatusCode)422);
        (await BalanceAsync(drawer)).Should().Be(20);

        // One unit (15) back in cash is within the 20 paid; a second (15 more) is not.
        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 1)]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 1)]))).StatusCode.Should().Be((HttpStatusCode)422);
        // Against the debt it goes through.
        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.ReduceBalance, null, null,
            [new(sale.Lines[0].Id, 2)]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BalanceAsync(drawer)).Should().Be(5);
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{customer.Id}", TestJson.Options))!.Balance.Should().Be(-5, "paid 20, kept goods worth 0, got 15 back");
    }

    [Fact]
    public async Task A_supplier_refunds_cash_only_when_nothing_is_owed_to_them()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 20);
        var unpaid = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 3, 6)]);
        (await admin.PostAsJsonAsync($"/api/purchases/{unpaid.Id}/returns", new PurchaseReturnRequest(Guid.NewGuid(), null, ReturnSettlement.Refund, drawer, null,
            [new ReturnLineInput(unpaid.Lines[0].Id, 1)]))).StatusCode.Should().Be((HttpStatusCode)422);
        (await BalanceAsync(drawer)).Should().Be(0);
        (await admin.PostAsJsonAsync($"/api/purchases/{unpaid.Id}/returns", new PurchaseReturnRequest(Guid.NewGuid(), null, ReturnSettlement.ReduceBalance, null, null,
            [new ReturnLineInput(unpaid.Lines[0].Id, 1)]))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receipts_sent_at_once_never_collect_more_than_the_debt()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("دين"), "054" + Random.Shared.Next(1000000, 9999999), null, null, true, 31)))
            .ReadAsync<CustomerDto>();
        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => admin.PostAsJsonAsync("/api/receipt-vouchers",
            new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id, null, null, 31, drawer, null, null, null, ConfirmDuplicate: true)))));
        answers.Count(a => a.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await BalanceAsync(drawer)).Should().Be(31);
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{customer.Id}", TestJson.Options))!.Balance.Should().Be(0);
    }

    [Fact]
    public async Task A_free_invoice_for_a_customer_is_saved_and_amounts_keep_two_decimals()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var customer = await CustomerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 6, openingQuantity: 10, openingCost: 2);
        var free = await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(product.Id, 1, 0)], [], null));
        free.StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0.005m,
            [new(product.Id, 0.333m, 6)], [new(drawer, 1.99m)], null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // With two decimals the total is the lines less the discount, exactly.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0.01m,
            [new(product.Id, 0.333m, 6)], [new(drawer, 1.99m)], null))).ReadAsync<SaleDetailDto>();
        sale.Total.Should().Be(sale.GrossTotal - sale.LineDiscountTotal - sale.InvoiceDiscount);
    }
}
