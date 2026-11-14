using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Sales;
using Qwader.Application.Settings;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

/// <summary>A customer's advance («دفعة مقدمة») and its use; an invoice's agorot become a discount.</summary>
[Collection(ApiCollection.Name)]
public sealed class DepositAndDecimalsTests(ApiFactory factory)
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

    private static async Task<decimal> CustomerBalanceAsync(HttpClient admin, int id) =>
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{id}", TestJson.Options))!.Balance;

    private static async Task IntegrityOkAsync(HttpClient admin)
    {
        var check = await admin.GetFromJsonAsync<IntegrityReport>("/api/settings/integrity", TestJson.Options);
        check!.Ok.Should().BeTrue(string.Join(" | ", check.Checks.Where(c => c.IssueCount > 0)
            .SelectMany(c => c.Issues.Select(i => $"{c.Name}: {i.Label} {i.Stored} ≠ {i.Computed}"))));
    }

    [Fact]
    public async Task An_advance_becomes_the_customers_credit_and_pays_their_next_purchase_on_credit()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("مقدمة"), "053" + Random.Shared.Next(1000000, 9999999), null, null)))
            .ReadAsync<CustomerDto>();
        var product = await admin.CreateProductAsync(retail: 30, openingQuantity: 10, openingCost: 10);

        // 45 left with the store: into the drawer, the customer is owed 45 («له»).
        var deposit = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.CustomerDeposit, customer.Id,
            null, null, 45, drawer, null, null, null))).ReadAsync<VoucherDto>();
        deposit.PurposeName.Should().Contain("دفعة مقدمة");
        (await BalanceAsync(drawer)).Should().Be(45);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(-45);

        // Buys 30 on credit: the credit pays it — nothing owed on the invoice, 15 still theirs.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(product.Id, 1, 30)], [], null))).ReadAsync<SaleDetailDto>();
        sale.Remaining.Should().Be(0);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(-15);
        (await BalanceAsync(drawer)).Should().Be(45, "no money moves at the sale: it came in with the advance");

        // Buys 30 more: 15 from the credit, 15 owed.
        var second = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(product.Id, 1, 30)], [], null))).ReadAsync<SaleDetailDto>();
        second.Remaining.Should().Be(15);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(15);
        await IntegrityOkAsync(admin);
    }

    [Fact]
    public async Task An_advance_needs_a_registered_customer_and_its_void_puts_everything_back()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.CustomerDeposit, null,
            "بدون عميل", null, 10, drawer, null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("مقدمة"), "052" + Random.Shared.Next(1000000, 9999999), null, null)))
            .ReadAsync<CustomerDto>();
        var deposit = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.CustomerDeposit, customer.Id,
            null, null, 20, drawer, null, null, null))).ReadAsync<VoucherDto>();
        await (await admin.PostAsJsonAsync($"/api/receipt-vouchers/{deposit.Id}/void", new VoidDocumentRequest("خطأ"))).ReadAsync<VoucherDto>();
        (await BalanceAsync(drawer)).Should().Be(0);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(0);
        await IntegrityOkAsync(admin);
    }

    [Fact]
    public async Task The_agorot_of_a_total_become_a_discount_without_a_discount_permission()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 2.5m, openingQuantity: 10, openingCost: 1);
        // A seller with no discount permission sells 3 at 2.50 = 7.50: the invoice is 7, the 0.50 a discount.
        var userName = TestData.Unique("seller");
        (await admin.PostAsJsonAsync("/api/users", new Application.Users.CreateUserRequest(userName, "بائع", null, "Temp12345", [Domain.Identity.SystemRoles.Sales])))
            .EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new Application.Auth.ChangePasswordRequest("Temp12345", "Seller12345"))).EnsureSuccessStatusCode();
        var seller = await factory.LoginAsync(userName, "Seller12345");

        var cash = (await seller.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var sale = await (await seller.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0.5m,
            [new(product.Id, 3, 2.5m)], [new(cash, 7)], null))).ReadAsync<SaleDetailDto>();
        sale.Total.Should().Be(7);
        sale.InvoiceDiscount.Should().Be(0.5m);

        // An older app (or a sale saved offline) still pays the agorot: they are change, the sale goes through.
        var old = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 2.5m)], [new(drawer, 2.5m)], null))).ReadAsync<SaleDetailDto>();
        old.Total.Should().Be(2);
        old.InvoiceDiscount.Should().Be(0.5m);
        (await BalanceAsync(drawer)).Should().Be(2);
        await IntegrityOkAsync(admin);
    }
}
