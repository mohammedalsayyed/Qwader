using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;
using Qwader.Application.Purchasing;

namespace Qwader.IntegrationTests;

/// <summary>
/// A customer's old credit («رصيد سابق — له») pays their credit purchases as an advance would: the invoice shows paid, no money
/// moves; a void gives it back; an invoice made before the credit can be paid from it later.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpeningCreditTests(ApiFactory factory)
{
    [Fact]
    public async Task An_old_credit_pays_the_customers_credit_purchases()
    {
        var admin = await factory.LoginAsync();
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("عميل رصيد"), "0599111222", null, null))).ReadAsync<CustomerDto>();
        var product = await admin.CreateProductAsync(retail: 24, openingQuantity: 10, openingCost: 10);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        async Task<decimal> Cash() => (await (await admin.GetAsync($"/api/financial-accounts/{cash}")).ReadAsync<AccountDto>()).CurrentBalance;
        async Task<SaleDetailDto> Sale(decimal price) => await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail,
            customer.Id, null, null, 0, [new(product.Id, 1, price)], [], null))).ReadAsync<SaleDetailDto>();
        async Task<SaleDetailDto> Get(int id) => await (await admin.GetAsync($"/api/sales/{id}")).ReadAsync<SaleDetailDto>();

        // A sale on credit before any credit: owed.
        var early = await Sale(24);
        early.Remaining.Should().Be(24);
        early.CreditAvailable.Should().Be(0);

        // Then their old balance «له» 30 is recorded: the earlier invoice can now be paid from it (24 of it).
        (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/old-balance", new CustomerOldBalanceRequest(30, false, "دفع قبل النظام"))).EnsureSuccessStatusCode();
        (await Get(early.Id)).CreditAvailable.Should().Be(24);
        var cashBefore = await Cash();
        var paid = await (await admin.PostAsync($"/api/sales/{early.Id}/apply-credit", null)).ReadAsync<SaleDetailDto>();
        (paid.Remaining, paid.PaidAmount, paid.CreditAvailable).Should().Be((0m, 24m, 0m));
        (await Cash()).Should().Be(cashBefore, "no money moves");

        // 6 left: a new 24 sale takes the 6 and leaves 18 owed — with no button to press.
        var next = await Sale(24);
        (next.PaidAmount, next.Remaining).Should().Be((6m, 18m));
        (await admin.PostAsync($"/api/sales/{early.Id}/apply-credit", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Voiding the first gives its 24 back: the second can take it.
        (await admin.PostAsJsonAsync($"/api/sales/{early.Id}/void", new VoidRequest("اختبار"))).EnsureSuccessStatusCode();
        (await Get(next.Id)).CreditAvailable.Should().Be(18);
        (await (await admin.PostAsync($"/api/sales/{next.Id}/apply-credit", null)).ReadAsync<SaleDetailDto>()).Remaining.Should().Be(0);

        var integrity = await (await admin.GetAsync("/api/settings/integrity")).ReadAsync<Application.Settings.IntegrityReport>();
        integrity.Checks.SelectMany(c => c.Issues).Where(i => i.Link == $"/sales/{early.Id}" || i.Link == $"/sales/{next.Id}").Should().BeEmpty();
    }

    [Fact]
    public async Task With_two_open_invoices_the_old_credit_pays_what_it_covers()
    {
        // As on production (customer #14): 70 and 65 open, then an old credit «له» of 50 → 50 of the 135 is paid, 85 still owed.
        var admin = await factory.LoginAsync();
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("عميل فاتورتين"), "0599111333", null, null))).ReadAsync<CustomerDto>();
        var product = await admin.CreateProductAsync(retail: 5, openingQuantity: 100, openingCost: 1);
        async Task<SaleDetailDto> Sale(decimal amount) => await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail,
            customer.Id, null, null, 0, [new(product.Id, amount / 5, 5)], [], null))).ReadAsync<SaleDetailDto>();
        async Task<SaleDetailDto> Get(int id) => await (await admin.GetAsync($"/api/sales/{id}")).ReadAsync<SaleDetailDto>();
        var a = await Sale(70);
        var b = await Sale(65);
        (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/old-balance", new CustomerOldBalanceRequest(50, false, null))).EnsureSuccessStatusCode();

        (await Get(a.Id)).CreditAvailable.Should().Be(50);
        (await Get(b.Id)).CreditAvailable.Should().Be(50);
        (await (await admin.PostAsync($"/api/sales/{a.Id}/apply-credit", null)).ReadAsync<SaleDetailDto>()).Remaining.Should().Be(20);
        (await Get(b.Id)).CreditAvailable.Should().Be(0, "the 50 is used");
        var owed = (await (await admin.GetAsync($"/api/customers/{customer.Id}")).ReadAsync<CustomerDto>()).Balance;
        owed.Should().Be(85);
        ((await Get(a.Id)).Remaining + (await Get(b.Id)).Remaining).Should().Be(owed, "the invoices now show what the customer owes");
    }
}
