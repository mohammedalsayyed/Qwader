using Microsoft.AspNetCore.Hosting;
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
        (await admin.GetFromJsonAsync<List<Application.Payroll.EmployeeListItemDto>>("/api/payroll/employees", TestJson.Options))
            .Should().BeEmpty("nobody stays on the payroll after clearing everything");
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
        // Someone on the payroll with an advance.
        var candidate = (await admin.GetFromJsonAsync<List<Application.Payroll.PayrollCandidateDto>>("/api/payroll/candidates", TestJson.Options))!.FirstOrDefault();
        if (candidate is not null)
        {
            var employee = await (await admin.PostAsJsonAsync("/api/payroll/employees",
                new Application.Payroll.SaveEmployeeRequest(candidate.UserId, null, Domain.Payroll.PayPeriod.Daily, 50, null, null))).ReadAsync<Application.Payroll.EmployeeDto>();
            await (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/entries",
                new Application.Payroll.CreatePayrollEntryRequest(Guid.NewGuid(), Domain.Payroll.PayrollEntryType.Advance, 10, null, cash, null))).ReadAsync<Application.Payroll.EmployeeDto>();
        }
    }

    [Fact]
    public async Task A_copy_with_the_reset_switched_off_refuses_it_before_anything_else_and_hides_it()
    {
        (await (await factory.LoginAsync()).GetFromJsonAsync<JsonElement>("/api/settings/system")).GetProperty("resetAllowed").GetBoolean().Should().BeTrue();

        // The same app with System:AllowReset = false, as in the production settings.
        using var locked = factory.WithWebHostBuilder(b => b.UseSetting("System:AllowReset", "false"));
        var client = locked.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new { userName = ApiFactory.AdminUser, password = ApiFactory.AdminPassword }))
            .ReadAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        (await client.GetFromJsonAsync<JsonElement>("/api/settings/system")).GetProperty("resetAllowed").GetBoolean().Should().BeFalse();
        var products = await CountAsync(client, "/api/products");

        // Refused first: a wrong password gets the same answer (so this can be checked safely), and the right one wipes nothing.
        foreach (var password in new[] { "wrong-password", ApiFactory.AdminPassword })
        {
            var response = await client.PostAsJsonAsync("/api/settings/reset", new SystemResetRequest(SystemResetMode.Everything, password), TestJson.Options);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain("معطّل");
        }
        (await CountAsync(client, "/api/products")).Should().Be(products);
    }

    private static async Task ResetAsync(HttpClient admin, SystemResetMode mode)
    {
        var result = await (await admin.PostAsJsonAsync("/api/settings/reset", new SystemResetRequest(mode, ApiFactory.AdminPassword), TestJson.Options))
            .ReadAsync<SystemResetResult>();
        result.Mode.Should().Be(mode);
        // Every reset first leaves a full backup on the server.
        result.BackupFile.Should().StartWith("before-reset-");
        File.Exists(Path.Combine(ApiFactory.BackupsRoot, result.BackupFile!)).Should().BeTrue();
        new FileInfo(Path.Combine(ApiFactory.BackupsRoot, result.BackupFile!)).Length.Should().BeGreaterThan(0);
    }

    private static async Task<int> CountAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonElement>(path + "?pageSize=1", TestJson.Options)).GetProperty("totalCount").GetInt32();
}
