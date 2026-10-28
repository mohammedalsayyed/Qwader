using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Auth;
using Qwader.Application.Customers;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Application.Settings;
using Qwader.Application.Users;
using Qwader.Domain.Identity;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SalesTests(ApiFactory factory)
{
    private static CreateSaleRequest Sale(IReadOnlyList<SaleLineInput> lines, IReadOnlyList<SalePaymentInput> payments,
        string? name = null, string? phone = null, decimal invoiceDiscount = 0, int? customerId = null, Guid? key = null) =>
        new(key ?? Guid.NewGuid(), PriceTier.Retail, customerId, name, phone, invoiceDiscount, lines, payments, null);

    private async Task<int> AccountAsync(HttpClient client, string name) => (await client.LookupsAsync()).Accounts.First(a => a.Name == name).Id;

    private async Task<decimal> BalanceAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.CurrentBalance).FirstAsync();
    }

    private async Task<HttpClient> SalesUserAsync()
    {
        var admin = await factory.LoginAsync();
        var userName = TestData.Unique("seller");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "بائع اختبار", null, "Temp12345", [SystemRoles.Sales]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Seller12345"))).EnsureSuccessStatusCode();
        return await factory.LoginAsync(userName, "Seller12345");
    }

    private static string Phone() => "059" + Random.Shared.Next(1000000, 9999999);

    [Fact]
    public async Task Cash_sale_issues_stock_at_average_cost_and_records_the_money()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 25, openingQuantity: 10, openingCost: 15);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var before = await BalanceAsync(cash);

        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 2, 25)], [new(cash, 50)]))).ReadAsync<SaleDetailDto>();

        sale.Number.Should().MatchRegex(@"^INV-\d{4}-\d{6}$");
        sale.IsWalkIn.Should().BeTrue();
        sale.Total.Should().Be(50);
        sale.TotalCost.Should().Be(30);
        sale.Profit.Should().Be(20);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(8);
        (await BalanceAsync(cash)).Should().Be(before + 50);
    }

    [Fact]
    public async Task Double_submit_with_the_same_key_creates_one_invoice()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 4);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var request = Sale([new(product.Id, 1, 10)], [new(cash, 10)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PostAsJsonAsync("/api/sales", request)));
        var again = await (await admin.PostAsJsonAsync("/api/sales", request)).ReadAsync<SaleDetailDto>();

        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(4, "the sale was applied exactly once");
        var ok = results.Where(r => r.IsSuccessStatusCode).ToList();
        ok.Should().NotBeEmpty();
        (await ok[0].ReadAsync<SaleDetailDto>()).Number.Should().Be(again.Number);
    }

    [Fact]
    public async Task Two_tills_selling_the_last_unit_at_once_only_one_succeeds()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 1, openingCost: 4);
        var cash = await AccountAsync(admin, "الصندوق النقدي");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 10)], [new(cash, 10)]))));

        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        responses.Where(r => !r.IsSuccessStatusCode).Should().AllSatisfy(r => ((int)r.StatusCode).Should().Be(422));
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(0);
    }

    [Fact]
    public async Task Insufficient_stock_rolls_back_everything()
    {
        var admin = await factory.LoginAsync();
        var a = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 4);
        var b = await admin.CreateProductAsync(retail: 10, openingQuantity: 1, openingCost: 4);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var before = await BalanceAsync(cash);

        var response = await admin.PostAsJsonAsync("/api/sales", Sale([new(a.Id, 2, 10), new(b.Id, 3, 10)], [new(cash, 50)]));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await admin.GetProductAsync(a.Id)).Quantity.Should().Be(5, "the first line must not be deducted when the second fails");
        (await BalanceAsync(cash)).Should().Be(before);
    }

    [Fact]
    public async Task Credit_sale_needs_a_named_customer_and_reuses_them_by_phone()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 100, openingQuantity: 10, openingCost: 60);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var phone = Phone();

        (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 100)], [new(cash, 40)]))).StatusCode.Should().Be((HttpStatusCode)422);

        var first = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 100)], [new(cash, 40)], "أبو أحمد", phone))).ReadAsync<SaleDetailDto>();
        var second = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 100)], [], "أبو أحمد", phone))).ReadAsync<SaleDetailDto>();

        second.CustomerId.Should().Be(first.CustomerId);
        var customer = await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{first.CustomerId}", TestJson.Options);
        customer!.Balance.Should().Be(160);
        customer.InvoiceCount.Should().Be(2);
    }

    [Fact]
    public async Task Payment_can_be_split_across_accounts()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 90, openingQuantity: 3, openingCost: 50);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var jawwal = await AccountAsync(admin, "جوال بي");
        var (cashBefore, jawwalBefore) = (await BalanceAsync(cash), await BalanceAsync(jawwal));

        // Store set to «transfers at the till count as confirmed».
        await SetTransfersPendingAsync(admin, false);
        try
        {
            var sale = await (await admin.PostAsJsonAsync("/api/sales",
                Sale([new(product.Id, 1, 90)], [new(cash, 30), new(jawwal, 60, TestData.Unique("TRX"))]))).ReadAsync<SaleDetailDto>();

            sale.Payments.Should().HaveCount(2);
            sale.Remaining.Should().Be(0);
            sale.Transfers.Should().ContainSingle(t => t.Status == Domain.Finance.TransferStatus.Confirmed && t.Amount == 60, "the transfer is still recorded");
            (await BalanceAsync(cash)).Should().Be(cashBefore + 30);
            (await BalanceAsync(jawwal)).Should().Be(jawwalBefore + 60);
        }
        finally
        {
            await SetTransfersPendingAsync(admin, true);
        }
    }

    [Fact]
    public async Task By_default_a_till_transfer_waits_for_verification_and_needs_a_named_customer()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 90, openingQuantity: 3, openingCost: 50);
        var jawwal = await AccountAsync(admin, "جوال بي");
        var before = await BalanceAsync(jawwal);

        (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 90)], [new(jawwal, 90)]))).StatusCode.Should().Be((HttpStatusCode)422);

        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 90)], [new(jawwal, 90, TestData.Unique("TRX"))], "زبون حوالة", Phone()))).ReadAsync<SaleDetailDto>();
        sale.PaidAmount.Should().Be(0);
        sale.Transfers.Should().ContainSingle(t => t.Status == Domain.Finance.TransferStatus.Pending);
        (await BalanceAsync(jawwal)).Should().Be(before, "pending transfers are not money yet");

        // Once the transfer is verified the invoice itself is paid, not only the customer's balance.
        (await admin.PostAsync($"/api/incoming-transfers/{sale.Transfers[0].Id}/confirm", null)).EnsureSuccessStatusCode();
        var paid = await (await admin.GetAsync($"/api/sales/{sale.Id}")).ReadAsync<SaleDetailDto>();
        paid.PaidAmount.Should().Be(90);
        paid.Remaining.Should().Be(0);
        (await BalanceAsync(jawwal)).Should().Be(before + 90);
    }

    private static async Task SetTransfersPendingAsync(HttpClient admin, bool pending)
    {
        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", TestJson.Options);
        (await admin.PutAsJsonAsync("/api/settings", settings! with { SaleTransfersPending = pending })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Sales_role_cannot_go_below_minimum_price_or_beyond_the_discount_limit()
    {
        var admin = await factory.LoginAsync();
        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", TestJson.Options);
        (await admin.PutAsJsonAsync("/api/settings", settings! with { MaxDiscountPercent = 10 })).EnsureSuccessStatusCode();
        var lookups = await admin.LookupsAsync();
        var category = await admin.CreateCategoryAsync();
        var product = await (await admin.PostAsJsonAsync("/api/products", new Application.Catalog.SaveProductRequest(TestData.Unique("محمي"), null, null,
            category.Id, null, lookups.Units[0].Id, null, null, null, null, null, null, 50, 100, 100, 85, 1, null, 10, 50)))
            .ReadAsync<Application.Catalog.ProductDetailDto>();
        var seller = await SalesUserAsync();
        var cash = await AccountAsync(seller, "الصندوق النقدي");

        var belowMin = await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 80)], [new(cash, 80)]));
        belowMin.StatusCode.Should().Be((HttpStatusCode)422);

        var tooMuchDiscount = await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 100)], [new(cash, 88)], invoiceDiscount: 12));
        tooMuchDiscount.StatusCode.Should().Be((HttpStatusCode)422);

        var allowed = await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 100)], [new(cash, 90)], invoiceDiscount: 10));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await allowed.ReadAsync<SaleDetailDto>()).Profit.Should().BeNull("sales staff do not see cost or profit");
    }

    [Fact]
    public async Task Any_seller_may_change_the_price_down_to_the_minimum_and_nobody_below_it()
    {
        var admin = await factory.LoginAsync();
        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", TestJson.Options);
        (await admin.PutAsJsonAsync("/api/settings", settings! with { MaxDiscountPercent = 5 })).EnsureSuccessStatusCode();
        var lookups = await admin.LookupsAsync();
        var category = await admin.CreateCategoryAsync();
        // cost 50, retail 100, minimum 70
        var product = await (await admin.PostAsJsonAsync("/api/products", new Application.Catalog.SaveProductRequest(TestData.Unique("سعر"), null, null,
            category.Id, null, lookups.Units[0].Id, null, null, null, null, null, null, 50, 90, 100, 70, 1, null, 20, 50)))
            .ReadAsync<Application.Catalog.ProductDetailDto>();
        var seller = await SalesUserAsync();
        var cash = await AccountAsync(seller, "الصندوق النقدي");

        // 75 is 25% under the list price, past the 5% limit, yet allowed: only the minimum (70) limits a lowered price.
        // It is recorded as a discount: the line keeps the price 100 with a 25 discount.
        var lowered = await (await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 75)], [new(cash, 75)]))).ReadAsync<SaleDetailDto>();
        lowered.Total.Should().Be(75);
        lowered.GrossTotal.Should().Be(100);
        lowered.LineDiscountTotal.Should().Be(25);
        lowered.Lines[0].UnitPrice.Should().Be(100);
        (await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 120)], [new(cash, 120)]))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Below the minimum: refused for the seller and for the administrator alike.
        var sellerBelow = await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 69)], [new(cash, 69)]));
        sellerBelow.StatusCode.Should().Be((HttpStatusCode)422);
        (await sellerBelow.Content.ReadAsStringAsync()).Should().Contain("أقل من أقل سعر بيع");
        var adminCash = await AccountAsync(admin, "الصندوق النقدي");
        (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 69)], [new(adminCash, 69)]))).StatusCode.Should().Be((HttpStatusCode)422);

        // An invoice discount that pushes the line under the minimum is refused too.
        (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 72)], [new(adminCash, 67)], invoiceDiscount: 5)))
            .StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Returns_restock_sellable_goods_refund_money_and_cannot_exceed_what_was_sold()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 20, openingQuantity: 10, openingCost: 12);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 4, 20)], [new(cash, 80)]))).ReadAsync<SaleDetailDto>();
        var lineId = sale.Lines[0].Id;
        var cashBefore = await BalanceAsync(cash);

        var afterReturn = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, cash, "مقاس غير مناسب",
            [new(lineId, 1, ReturnCondition.Sellable), new(lineId, 1, ReturnCondition.Damaged)]))).ReadAsync<SaleDetailDto>();

        afterReturn.Status.Should().Be(SaleStatus.PartiallyReturned);
        afterReturn.ReturnedTotal.Should().Be(40);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(7, "only the sellable unit goes back on the shelf");
        (await BalanceAsync(cash)).Should().Be(cashBefore - 40);
        afterReturn.Profit.Should().Be(40 - 36, "revenue 40 left, cost of 2 kept units (24) plus the damaged unit's cost (12)");

        var tooMany = await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, cash, null,
            [new(lineId, 3, ReturnCondition.Sellable)]));
        tooMany.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Returning_a_credit_sale_reduces_the_customer_debt()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 5, openingCost: 30);
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 2, 50)], [], "عميل آجل", Phone()))).ReadAsync<SaleDetailDto>();

        await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.ReduceBalance, null, null,
            [new(sale.Lines[0].Id, 1)]))).ReadAsync<SaleDetailDto>();

        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(50);
        var statement = await admin.GetFromJsonAsync<StatementDto>($"/api/customers/{sale.CustomerId}/statement", TestJson.Options);
        statement!.Lines.Select(l => l.Balance).Should().Equal(100, 50);
    }

    [Fact]
    public async Task Voiding_reverses_stock_money_and_debt_and_is_blocked_after_a_return()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 30, openingQuantity: 6, openingCost: 20);
        var cash = await AccountAsync(admin, "الصندوق النقدي");
        var cashBefore = await BalanceAsync(cash);
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 3, 30)], [new(cash, 50)], "عميل", Phone()))).ReadAsync<SaleDetailDto>();

        var voided = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/void", new VoidRequest("خطأ في الإدخال"))).ReadAsync<SaleDetailDto>();

        voided.Status.Should().Be(SaleStatus.Voided);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(6);
        (await BalanceAsync(cash)).Should().Be(cashBefore);
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(0);

        var other = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 2, 30)], [new(cash, 60)]))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync($"/api/sales/{other.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, cash, null,
            [new(other.Lines[0].Id, 1)]))).ReadAsync<SaleDetailDto>();
        (await admin.PostAsJsonAsync($"/api/sales/{other.Id}/void", new VoidRequest("محاولة"))).StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Sales_staff_see_only_their_own_invoices_and_summary()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 15, openingQuantity: 10, openingCost: 5);
        var seller = await SalesUserAsync();
        var cash = await AccountAsync(seller, "الصندوق النقدي");
        var adminSale = await (await admin.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 1, 15)], [new(cash, 15)]))).ReadAsync<SaleDetailDto>();
        await (await seller.PostAsJsonAsync("/api/sales", Sale([new(product.Id, 2, 15)], [new(cash, 30)]))).ReadAsync<SaleDetailDto>();

        (await seller.GetAsync($"/api/sales/{adminSale.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var summary = await seller.GetFromJsonAsync<SalesSummaryDto>("/api/sales/summary", TestJson.Options);
        summary!.OwnSalesOnly.Should().BeTrue();
        summary.InvoiceCount.Should().Be(1);
        summary.NetSales.Should().Be(30);
        summary.Received.Should().Be(30);
        summary.GrossProfit.Should().BeNull();
    }
}
