using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Application.Settings;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SystemResetTests(ApiFactory factory)
{
    [Fact]
    public async Task Reset_needs_the_admins_password()
    {
        var admin = await factory.LoginAsync();
        var response = await admin.PostAsJsonAsync("/api/settings/reset", new SystemResetRequest(SystemResetMode.TransactionsOnly, "wrong-password"), TestJson.Options);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("كلمة المرور غير صحيحة");
    }

    [Fact]
    public async Task Clearing_transactions_keeps_products_and_people_with_zero_stock_and_balances()
    {
        var admin = await factory.LoginAsync();
        await CreateActivityAsync(admin);
        var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 5, openingCost: 30);
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("عميل"), "0591112233", null, null, true, 40)))
            .ReadAsync<CustomerDto>();

        await ResetAsync(admin, SystemResetMode.TransactionsOnly);

        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(0);
        (await (await admin.GetAsync($"/api/customers/{customer.Id}")).ReadAsync<CustomerDto>()).Balance.Should().Be(0);
        var accounts = await (await admin.GetAsync("/api/financial-accounts")).ReadAsync<List<AccountDto>>();
        accounts.Should().OnlyContain(a => a.CurrentBalance == 0 && a.OpeningBalance == 0 && a.PendingTransfers == 0);
        (await CountAsync(admin, "/api/sales")).Should().Be(0);
        (await CountAsync(admin, "/api/purchases")).Should().Be(0);
        (await CountAsync(admin, "/api/incoming-transfers")).Should().Be(0);

        // The store works straight away and document numbers start again.
        await admin.PurchaseAsync((await admin.CreateSupplierAsync()).Id, [new PurchaseLineInput(product.Id, 2, 30)]);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 50)], [new(cash, 50)], null))).ReadAsync<SaleDetailDto>();
        sale.Number.Should().EndWith("-000001");
    }

    [Fact]
    public async Task Clearing_everything_leaves_only_users_settings_and_reference_lists()
    {
        var admin = await factory.LoginAsync();
        await CreateActivityAsync(admin);

        await ResetAsync(admin, SystemResetMode.Everything);

        (await CountAsync(admin, "/api/products")).Should().Be(0);
        (await CountAsync(admin, "/api/suppliers")).Should().Be(0);
        var lookups = await admin.LookupsAsync();
        lookups.Accounts.Should().Contain(a => a.Name == "الصندوق النقدي", "default accounts are seeded again");
        lookups.Units.Should().NotBeEmpty();
        (await admin.CreateProductAsync()).Sku.Should().Be("P-00001");

        // The admin is still signed in and the reset itself is on record.
        var log = await admin.GetFromJsonAsync<JsonElement>("/api/audit-log?area=System", TestJson.Options);
        log.GetProperty("items")[0].GetProperty("actionName").GetString().Should().Be("تصفير النظام");
    }

    /// <summary>A sale on credit, a pending till transfer, a purchase and an expense — every kind of document that must go.</summary>
    private static async Task CreateActivityAsync(HttpClient admin)
    {
        var lookups = await admin.LookupsAsync();
        var cash = lookups.Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var wallet = lookups.Accounts.First(a => a.Type != FinancialAccountType.Cash).Id;
        var product = await admin.CreateProductAsync(retail: 100);
        await admin.PurchaseAsync((await admin.CreateSupplierAsync()).Id, [new PurchaseLineInput(product.Id, 10, 60)], paid: 0);
        await admin.FundAsync(cash, 500);
        await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, TestData.Unique("زبون"), "0597654321", 0,
            [new(product.Id, 2, 100)], [new(cash, 50), new(wallet, 100)], null))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), lookups.ExpenseCategories[0].Id, "اختبار تصفير", 10, cash, null, null)))
            .ReadAsync<ExpenseDto>();
    }

    private static async Task ResetAsync(HttpClient admin, SystemResetMode mode) =>
        (await (await admin.PostAsJsonAsync("/api/settings/reset", new SystemResetRequest(mode, ApiFactory.AdminPassword), TestJson.Options))
            .ReadAsync<SystemResetResult>()).Mode.Should().Be(mode);

    private static async Task<int> CountAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonElement>(path + "?pageSize=1", TestJson.Options)).GetProperty("totalCount").GetInt32();
}
