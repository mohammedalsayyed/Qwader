using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;
using Qwader.Application.Sales;

namespace Qwader.IntegrationTests;

/// <summary>Money a customer pays later shows on the invoices it paid.</summary>
[Collection(ApiCollection.Name)]
public sealed class CollectionsTests(ApiFactory factory)
{
    private static string Phone() => "058" + Random.Shared.Next(1000000, 9999999);

    private async Task<(HttpClient Admin, int Cash, CustomerDto Customer, int ProductId)> SetupAsync(decimal openingDebt)
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("تحصيل"), Phone(), null, null, true, openingDebt)))
            .ReadAsync<CustomerDto>();
        var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 20, openingCost: 20);
        return (admin, cash, customer, product.Id);
    }

    private static async Task<SaleDetailDto> CreditSaleAsync(HttpClient admin, int customerId, int productId, int quantity) =>
        await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customerId, null, null, 0,
            [new(productId, quantity, 50)], [], null))).ReadAsync<SaleDetailDto>();

    private static async Task<decimal> RemainingAsync(HttpClient admin, int saleId) =>
        (await (await admin.GetAsync($"/api/sales/{saleId}")).ReadAsync<SaleDetailDto>()).Remaining;

    [Fact]
    public async Task A_collection_pays_the_opening_balance_then_the_oldest_invoices_and_voiding_it_gives_them_back()
    {
        var (admin, cash, customer, productId) = await SetupAsync(openingDebt: 30);
        var older = await CreditSaleAsync(admin, customer.Id, productId, 2); // 100
        var newer = await CreditSaleAsync(admin, customer.Id, productId, 1); // 50

        var receipt = await (await admin.PostAsJsonAsync("/api/receipt-vouchers",
            new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id, null, null, 150, cash, null, null, null))).ReadAsync<VoucherDto>();

        // 150 = 30 opening + 100 oldest invoice + 20 of the newer one.
        (await RemainingAsync(admin, older.Id)).Should().Be(0);
        (await RemainingAsync(admin, newer.Id)).Should().Be(30);
        var unpaid = await admin.GetFromJsonAsync<Application.Common.PagedResult<SaleListItemDto>>($"/api/sales?unpaid=true&search={customer.Phone}", TestJson.Options);
        unpaid!.Items.Should().ContainSingle(s => s.Id == newer.Id && s.Remaining == 30);

        (await admin.PostAsJsonAsync($"/api/receipt-vouchers/{receipt.Id}/void", new { reason = "خطأ" })).EnsureSuccessStatusCode();
        (await RemainingAsync(admin, older.Id)).Should().Be(100);
        (await RemainingAsync(admin, newer.Id)).Should().Be(50);
    }

    [Fact]
    public async Task Goods_returned_against_the_balance_lower_what_the_invoice_still_owes()
    {
        var (admin, _, customer, productId) = await SetupAsync(openingDebt: 0);
        var sale = await CreditSaleAsync(admin, customer.Id, productId, 3); // 150

        await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.ReduceBalance, null, null,
            [new(sale.Lines[0].Id, 1)]))).ReadAsync<SaleDetailDto>();

        (await RemainingAsync(admin, sale.Id)).Should().Be(100);
    }

    [Fact]
    public async Task Confirming_a_transfer_larger_than_the_debt_asks_first_and_names_a_likely_duplicate()
    {
        var (admin, _, customer, productId) = await SetupAsync(openingDebt: 0);
        await CreditSaleAsync(admin, customer.Id, productId, 2); // owes 100
        var wallet = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Wallet).Id;
        async Task<IncomingTransferDto> TransferAsync() =>
            await (await admin.PostAsJsonAsync("/api/incoming-transfers", new CreateIncomingTransferRequest(Guid.NewGuid(), customer.Name, null, 100, null, wallet,
                TestData.Unique("DUP"), null, null, customer.Id, null))).ReadAsync<IncomingTransferDto>();
        var first = await TransferAsync();
        var second = await TransferAsync(); // the same payment entered twice

        (await admin.PostAsync($"/api/incoming-transfers/{first.Id}/confirm", null)).EnsureSuccessStatusCode(); // pays the 100

        var again = await admin.PostAsync($"/api/incoming-transfers/{second.Id}/confirm", null);
        again.StatusCode.Should().Be((HttpStatusCode)428);
        (await again.Content.ReadAsStringAsync()).Should().Contain(first.Number);

        (await admin.PostAsync($"/api/incoming-transfers/{second.Id}/confirm?acceptCredit=true", null)).EnsureSuccessStatusCode();
        (await (await admin.GetAsync($"/api/customers/{customer.Id}")).ReadAsync<CustomerDto>()).Balance.Should().Be(-100, "the reviewer accepted the credit");
    }
}
