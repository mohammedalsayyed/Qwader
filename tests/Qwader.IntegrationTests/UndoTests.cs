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

/// <summary>A void or a return on an earlier day; restoring a voided invoice; cancelling a return.</summary>
[Collection(ApiCollection.Name)]
public sealed class UndoTests(ApiFactory factory)
{
    private static string Phone() => "057" + Random.Shared.Next(1000000, 9999999);

    private async Task<(HttpClient Admin, int Drawer, CustomerDto Customer, int ProductId)> SetupAsync()
    {
        var admin = await factory.LoginAsync();
        // A drawer of its own, so other tests' money does not mix with it.
        var drawer = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("درج"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("استعادة"), Phone(), null, null)))
            .ReadAsync<CustomerDto>();
        var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 10, openingCost: 20);
        return (admin, drawer, customer, product.Id);
    }

    private static CreateSaleRequest Sale(int customerId, int productId, decimal quantity, int drawer, decimal paid, DateTime? soldAt = null) =>
        new(Guid.NewGuid(), PriceTier.Retail, customerId, null, null, 0, [new(productId, quantity, 50)], paid > 0 ? [new(drawer, paid)] : [], null, soldAt);

    private async Task<decimal> BalanceAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.CurrentBalance).FirstAsync();
    }

    private async Task<decimal> CustomerBalanceAsync(HttpClient admin, int id) =>
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{id}", TestJson.Options))!.Balance;

    private static DateOnly LocalDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));

    private static readonly TimeZoneInfo Zone = new[] { "Asia/Hebron", "West Bank Standard Time" }
        .Select(id => { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return null; } })
        .First(z => z is not null)!;

    [Fact]
    public async Task A_void_and_a_return_take_the_day_chosen_today_by_default_never_later_or_before_the_invoice()
    {
        var (admin, drawer, customer, productId) = await SetupAsync();
        var today = LocalDay(DateTime.UtcNow);

        // Sold two days ago (without internet), so earlier days are open to it.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(customer.Id, productId, 2, drawer, 100, DateTime.UtcNow.AddDays(-2)))).ReadAsync<SaleDetailDto>();
        var saleDay = LocalDay(sale.Date);

        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 1)], today.AddDays(1)))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not after today");
        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 1)], saleDay.AddDays(-1)))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not before the invoice");

        var returned = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 1)], today.AddDays(-1)))).ReadAsync<SaleDetailDto>();
        LocalDay(returned.Returns.Single().Date).Should().Be(today.AddDays(-1));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refund = await db.AccountTransactions.SingleAsync(t => t.FinancialAccountId == drawer && t.Category == AccountTransactionCategory.SaleRefund);
            LocalDay(refund.Date).Should().Be(today.AddDays(-1));
        }

        // A second invoice voided «yesterday»; without a day it is today.
        var other = await (await admin.PostAsJsonAsync("/api/sales", Sale(customer.Id, productId, 1, drawer, 50, DateTime.UtcNow.AddDays(-2)))).ReadAsync<SaleDetailDto>();
        var voided = await (await admin.PostAsJsonAsync($"/api/sales/{other.Id}/void", new VoidRequest("خطأ", today.AddDays(-1)))).ReadAsync<SaleDetailDto>();
        LocalDay(voided.VoidedAt!.Value).Should().Be(today.AddDays(-1));
    }

    [Fact]
    public async Task Restoring_a_voided_invoice_puts_goods_money_and_debt_back_once_and_it_can_be_voided_again()
    {
        var (admin, drawer, customer, productId) = await SetupAsync();
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(customer.Id, productId, 2, drawer, 60))).ReadAsync<SaleDetailDto>();
        (await BalanceAsync(drawer)).Should().Be(60);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(40);

        await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/void", new VoidRequest("خطأ"))).ReadAsync<SaleDetailDto>();
        (await BalanceAsync(drawer)).Should().Be(0);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(0);
        (await admin.GetProductAsync(productId)).Quantity.Should().Be(10);

        var restored = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/restore", new VoidRequest("أُلغيت بالغلط"))).ReadAsync<SaleDetailDto>();
        restored.Status.Should().Be(SaleStatus.Posted);
        restored.RestoredAt.Should().NotBeNull();
        restored.VoidedAt.Should().BeNull();
        (await BalanceAsync(drawer)).Should().Be(60, "the money is back in the account it was paid into");
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(40, "the customer owes the rest again");
        (await admin.GetProductAsync(productId)).Quantity.Should().Be(8);

        // Money received today: 60 once (the first payment and its reversal cancel out; the restore counts).
        var summary = await admin.GetFromJsonAsync<SalesSummaryDto>("/api/sales/summary", TestJson.Options);
        summary!.ReceivedByAccount.Single(a => a.AccountId == drawer).Amount.Should().Be(60);

        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/restore", new VoidRequest("مرة ثانية"))).StatusCode
            .Should().Be((HttpStatusCode)422, "an invoice that is not voided cannot be restored");

        // Voided again: everything comes off again, and the money received is 0.
        await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/void", new VoidRequest("نهائي"))).ReadAsync<SaleDetailDto>();
        (await BalanceAsync(drawer)).Should().Be(0);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(0);
        (await admin.GetProductAsync(productId)).Quantity.Should().Be(10);
        summary = await admin.GetFromJsonAsync<SalesSummaryDto>("/api/sales/summary", TestJson.Options);
        summary!.ReceivedByAccount.Should().NotContain(a => a.AccountId == drawer);
        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/restore", new VoidRequest("ثالثة"))).StatusCode
            .Should().Be((HttpStatusCode)422, "an invoice is restored once only");
    }

    [Fact]
    public async Task Restoring_needs_the_goods_in_stock()
    {
        var (admin, drawer, customer, productId) = await SetupAsync();
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(customer.Id, productId, 10, drawer, 500))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/void", new VoidRequest("خطأ"))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync("/api/sales", Sale(customer.Id, productId, 5, drawer, 250))).ReadAsync<SaleDetailDto>();

        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/restore", new VoidRequest("أُلغيت بالغلط"))).StatusCode.Should().Be((HttpStatusCode)422);
        (await admin.GetProductAsync(productId)).Quantity.Should().Be(5);
        (await BalanceAsync(drawer)).Should().Be(250);
    }

    [Fact]
    public async Task Cancelling_a_return_takes_the_goods_out_again_and_brings_the_refund_back_to_its_account()
    {
        var (admin, drawer, customer, productId) = await SetupAsync();
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(customer.Id, productId, 3, drawer, 150))).ReadAsync<SaleDetailDto>();
        var afterReturn = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 2)]))).ReadAsync<SaleDetailDto>();
        (await BalanceAsync(drawer)).Should().Be(50);
        (await admin.GetProductAsync(productId)).Quantity.Should().Be(9);
        afterReturn.Status.Should().Be(SaleStatus.PartiallyReturned);

        var ret = afterReturn.Returns.Single();
        var cancelled = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns/{ret.Id}/void", new VoidRequest("مرتجع بالغلط"))).ReadAsync<SaleDetailDto>();
        cancelled.Status.Should().Be(SaleStatus.Posted);
        cancelled.ReturnedTotal.Should().Be(0);
        cancelled.Returns.Single().VoidedAt.Should().NotBeNull("it stays in the invoice's history, marked");
        (await BalanceAsync(drawer)).Should().Be(150, "the refund is back in the drawer it left");
        (await admin.GetProductAsync(productId)).Quantity.Should().Be(7);
        (await CustomerBalanceAsync(admin, customer.Id)).Should().Be(0);

        var summary = await admin.GetFromJsonAsync<SalesSummaryDto>("/api/sales/summary", TestJson.Options);
        summary!.ReceivedByAccount.Single(a => a.AccountId == drawer).Amount.Should().Be(150);

        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns/{ret.Id}/void", new VoidRequest("مرة ثانية"))).StatusCode.Should().Be((HttpStatusCode)422);
        // The whole quantity can be returned again, and the invoice voided once no return stands.
        var again = await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(sale.Lines[0].Id, 3)]))).ReadAsync<SaleDetailDto>();
        again.Status.Should().Be(SaleStatus.Returned);
        (await BalanceAsync(drawer)).Should().Be(0);

        // The integrity check agrees with all of it.
        var check = await admin.GetFromJsonAsync<Application.Settings.IntegrityReport>("/api/settings/integrity", TestJson.Options);
        check!.Ok.Should().BeTrue(string.Join(" | ", check.Checks.Where(c => c.IssueCount > 0).SelectMany(c => c.Issues.Select(i => $"{c.Name}: {i.Label} {i.Stored} ≠ {i.Computed}"))));
    }
}
