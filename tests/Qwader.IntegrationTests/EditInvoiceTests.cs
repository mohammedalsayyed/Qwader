using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Application.Settings;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

/// <summary>Editing a sale or a purchase: the old one voided and the new one made in one go — stock, money and balances exact.</summary>
[Collection(ApiCollection.Name)]
public sealed class EditInvoiceTests(ApiFactory factory)
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
        await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("تعديل"), "056" + Random.Shared.Next(1000000, 9999999), null, null)))
            .ReadAsync<CustomerDto>();

    private static async Task<decimal> CustomerBalanceAsync(HttpClient admin, int id) =>
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{id}", TestJson.Options))!.Balance;

    private static async Task IntegrityOkAsync(HttpClient admin)
    {
        var check = await admin.GetFromJsonAsync<IntegrityReport>("/api/settings/integrity", TestJson.Options);
        check!.Ok.Should().BeTrue(string.Join(" | ", check.Checks.Where(c => c.IssueCount > 0)
            .SelectMany(c => c.Issues.Select(i => $"{c.Name}: {i.Label} {i.Stored} ≠ {i.Computed}"))));
    }

    [Fact]
    public async Task Editing_a_sale_puts_stock_money_and_debt_exactly_where_the_new_invoice_says()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var customer = await CustomerAsync(admin);
        var kettle = await admin.CreateProductAsync(retail: 50, openingQuantity: 10, openingCost: 20);
        var cup = await admin.CreateProductAsync(retail: 30, openingQuantity: 10, openingCost: 10);

        // 3 kettles = 150, paid 100: the customer owes 50.
        var original = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(kettle.Id, 3, 50)], [new(drawer, 100)], null))).ReadAsync<SaleDetailDto>();
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(50);

        // Edited: 2 kettles + 1 cup = 130, paid 130.
        var edited = await (await admin.PostAsJsonAsync($"/api/sales/{original.Id}/replace", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(kettle.Id, 2, 50), new(cup.Id, 1, 30)], [new(drawer, 130)], null))).ReadAsync<SaleDetailDto>();

        edited.Total.Should().Be(130);
        edited.Number.Should().NotBe(original.Number);
        edited.Date.Should().Be(original.Date, "an edited invoice stays on its day");
        edited.Notes.Should().Contain(original.Number);
        var old = await admin.GetFromJsonAsync<SaleDetailDto>($"/api/sales/{original.Id}", TestJson.Options);
        old!.Status.Should().Be(SaleStatus.Voided);
        old.VoidReason.Should().Contain(edited.Number);

        (await admin.GetProductAsync(kettle.Id)).Quantity.Should().Be(8);
        (await admin.GetProductAsync(cup.Id)).Quantity.Should().Be(9);
        (await BalanceAsync(drawer)).Should().Be(130);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(0);
        var summary = await admin.GetFromJsonAsync<SalesSummaryDto>("/api/sales/summary", TestJson.Options);
        summary!.ReceivedByAccount.Single(a => a.AccountId == drawer).Amount.Should().Be(130, "the first payment and its reversal cancel out");
        await IntegrityOkAsync(admin);
    }

    [Fact]
    public async Task An_edit_that_fails_changes_nothing_and_a_repeated_or_simultaneous_edit_happens_once()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 4);
        var original = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 2, 10)], [new(drawer, 20)], null))).ReadAsync<SaleDetailDto>();

        // 9 is more than the 5 there are (the 2 sold come back first): refused, and nothing moved.
        (await admin.PostAsJsonAsync($"/api/sales/{original.Id}/replace", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 9, 10)], [new(drawer, 90)], null))).StatusCode.Should().Be((HttpStatusCode)422);
        (await admin.GetFromJsonAsync<SaleDetailDto>($"/api/sales/{original.Id}", TestJson.Options))!.Status.Should().Be(SaleStatus.Posted);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(3);
        (await BalanceAsync(drawer)).Should().Be(20);

        // The same edit sent several times at once: one new invoice.
        var request = new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0, [new(product.Id, 1, 10)], [new(drawer, 10)], null);
        var codes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => admin.PostAsJsonAsync($"/api/sales/{original.Id}/replace", request))));
        codes.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(4);
        (await BalanceAsync(drawer)).Should().Be(10);
        await IntegrityOkAsync(admin);
    }

    [Fact]
    public async Task A_sale_with_a_return_is_not_edited_and_a_walk_in_invoice_can_be_given_to_a_customer()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 4);
        var returned = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 2, 10)], [new(drawer, 20)], null))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync($"/api/sales/{returned.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(returned.Lines[0].Id, 1)]))).ReadAsync<SaleDetailDto>();
        (await admin.PostAsJsonAsync($"/api/sales/{returned.Id}/replace", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 10)], [new(drawer, 10)], null))).StatusCode.Should().Be((HttpStatusCode)422);

        var walkIn = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 10)], [new(drawer, 10)], null))).ReadAsync<SaleDetailDto>();
        var customer = await CustomerAsync(admin);
        var linked = await (await admin.PatchAsJsonAsync($"/api/sales/{walkIn.Id}/details", new UpdateSaleDetailsRequest("مسلّمة", null, null, customer.Id)))
            .ReadAsync<SaleDetailDto>();
        linked.CustomerId.Should().Be(customer.Id);
        linked.Notes.Should().Be("مسلّمة");
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(0, "paid in full: on the statement, no debt");
        var statement = await admin.GetFromJsonAsync<StatementDto>($"/api/customers/{customer.Id}/statement", TestJson.Options);
        statement!.Lines.Should().Contain(l => l.Reference == walkIn.Number);
        (await BalanceAsync(drawer)).Should().Be(20, "20 + 10 − 10 refunded; linking moves no money");
        await IntegrityOkAsync(admin);
    }

    [Fact]
    public async Task Editing_a_purchase_after_part_was_sold_keeps_stock_cost_supplier_and_money_exact()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        await admin.FundAsync(drawer, 100);
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 10);

        // 10 at 5, paid 50; 4 sold at 10 (cash 40).
        var original = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 10, 5)], paid: 50, accountId: drawer);
        await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 4, 10)], [new(drawer, 40)], null))).ReadAsync<SaleDetailDto>();
        (await BalanceAsync(drawer)).Should().Be(90);

        // The price was 6, not 5: 10 at 6, paid 60.
        var edited = await (await admin.PostAsJsonAsync($"/api/purchases/{original.Id}/replace", new SavePurchaseRequest(Guid.NewGuid(), supplier.Id, null, null, null,
            0, 60, drawer, null, [new PurchaseLineInput(product.Id, 10, 6)], true))).ReadAsync<PurchaseDetailDto>();
        edited.Total.Should().Be(60);

        var after = await admin.GetProductAsync(product.Id);
        after.Quantity.Should().Be(6);
        after.AverageCost.Should().Be(6, "what is left is at the corrected cost");
        (await BalanceAsync(drawer)).Should().Be(80, "90 + 50 back − 60 paid");
        (await admin.GetFromJsonAsync<SupplierDto>($"/api/suppliers/{supplier.Id}", TestJson.Options))!.Balance.Should().Be(0);
        (await admin.GetFromJsonAsync<PurchaseDetailDto>($"/api/purchases/{original.Id}", TestJson.Options))!.Status.Should().Be(PurchaseStatus.Voided);
        await IntegrityOkAsync(admin);

        // Down to 5 when 4 were sold from the 10 is fine; down to 3 is not (4 already sold): refused, nothing moves.
        (await admin.PostAsJsonAsync($"/api/purchases/{edited.Id}/replace", new SavePurchaseRequest(Guid.NewGuid(), supplier.Id, null, null, null,
            0, 0, null, null, [new PurchaseLineInput(product.Id, 3, 6)], true))).StatusCode.Should().Be((HttpStatusCode)422);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(6);
        (await BalanceAsync(drawer)).Should().Be(80);
        (await admin.GetFromJsonAsync<PurchaseDetailDto>($"/api/purchases/{edited.Id}", TestJson.Options))!.Status.Should().Be(PurchaseStatus.Posted);
        await IntegrityOkAsync(admin);
    }
}
