using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Application.Finance;
using Qwader.Application.Inventory;
using Qwader.Application.Purchasing;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;

using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

/// <summary>Fixes from the store's test report: reports, empty keys, amounts, accounts below zero, stock counts, date ranges.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReviewFixesTests(ApiFactory factory)
{
    private async Task<int> DrawerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("درج"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;

    private static CreateSaleRequest Sale(int productId, decimal quantity, decimal price, int account, decimal paid) =>
        new(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0, [new(productId, quantity, price)], [new(account, paid)], null);

    private async Task<decimal> BalanceAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.CurrentBalance).FirstAsync();
    }

    private static async Task<decimal> ReceivedTodayAsync(HttpClient admin)
    {
        var rows = await admin.GetFromJsonAsync<List<DailyRowDto>>("/api/reports/daily", TestJson.Options);
        return rows!.Last().Received ?? 0;
    }

    [Fact]
    public async Task The_daily_report_leaves_voided_receipts_and_invoices_out_of_money_received_and_takes_off_refunds()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 10, openingCost: 20);
        var before = await ReceivedTodayAsync(admin);

        await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 1, 50, drawer, 50))).ReadAsync<SaleDetailDto>();
        var voided = await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 1, 50, drawer, 50))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync($"/api/sales/{voided.Id}/void", new VoidRequest("خطأ"))).ReadAsync<SaleDetailDto>();
        var returned = await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 2, 50, drawer, 100))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync($"/api/sales/{returned.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, drawer, null,
            [new(returned.Lines[0].Id, 1)]))).ReadAsync<SaleDetailDto>();
        var receipt = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.OtherIncome, null,
            "دخل", null, 1000, drawer, null, null, null))).ReadAsync<VoucherDto>();
        await (await admin.PostAsJsonAsync($"/api/receipt-vouchers/{receipt.Id}/void", new VoidDocumentRequest("خطأ"))).ReadAsync<VoucherDto>();

        (await ReceivedTodayAsync(admin) - before).Should().Be(100, "50 kept + 100 − 50 refunded; the voided invoice and receipt are not money received");
    }

    [Fact]
    public async Task Voiding_a_receipt_or_a_sale_never_takes_the_account_below_zero()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var other = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 15, openingQuantity: 5, openingCost: 5);

        var receipt = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.OtherIncome, null,
            "دخل", null, 15, drawer, null, null, null))).ReadAsync<VoucherDto>();
        var sale = await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 1, 15, drawer, 15))).ReadAsync<SaleDetailDto>();
        // All the money moved to another account.
        (await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), drawer, other, 30, null)))
            .EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync($"/api/receipt-vouchers/{receipt.Id}/void", new VoidDocumentRequest("خطأ"))).StatusCode.Should().Be((HttpStatusCode)422);
        (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/void", new VoidRequest("خطأ"))).StatusCode.Should().Be((HttpStatusCode)422);
        (await BalanceAsync(drawer)).Should().Be(0);
    }

    [Fact]
    public async Task An_empty_operation_key_is_refused_instead_of_answering_success()
    {
        var admin = await factory.LoginAsync();
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 20);
        var draft = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 2, 10)], post: false);

        var noKey = await admin.PostAsJsonAsync($"/api/purchases/{draft.Id}/post", new PostPurchaseRequest(Guid.Empty));
        noKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 1, 20, (await admin.LookupsAsync()).Accounts[0].Id, 0) with { IdempotencyKey = Guid.Empty }))
            .Content.ReadAsStringAsync()).Should().NotContain("must not be empty", "validation messages are in Arabic");
    }

    [Fact]
    public async Task Amounts_take_two_decimals_and_stay_under_the_ceiling()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.OtherIncome, null,
            "دخل", null, 0.001m, drawer, null, null, null))).StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, (HttpStatusCode)422);
        (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.OtherIncome, null,
            "دخل", null, 1_000_000_000_000_000m, drawer, null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "refused as input");
        (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.OtherIncome, null,
            "دخل", null, 200_000_000m, drawer, null, null, null))).StatusCode.Should().Be((HttpStatusCode)422, "over the 100M ceiling of one amount");
        (await BalanceAsync(drawer)).Should().Be(0);
    }

    [Fact]
    public async Task A_count_takes_its_difference_from_the_balance_when_counted_and_a_product_is_in_one_open_count_only()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 9, openingCost: 4);

        var count = await (await admin.PostAsJsonAsync("/api/inventory/counts", new CreateStockCountRequest(null, null, [product.Id], null))).ReadAsync<StockCountDto>();
        (await admin.PostAsJsonAsync("/api/inventory/counts", new CreateStockCountRequest(null, null, [product.Id], null))).StatusCode
            .Should().Be((HttpStatusCode)422, "the product is already in an open count");

        // Counted 8 while the system said 9 (one missing); then one sold before posting → 7, not 8.
        await (await admin.PutAsJsonAsync($"/api/inventory/counts/{count.Id}", new SaveCountsRequest([new(count.Lines[0].Id, 8)]))).ReadAsync<StockCountDto>();
        await (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 1, 10, drawer, 10))).ReadAsync<SaleDetailDto>();
        await (await admin.PostAsJsonAsync($"/api/inventory/counts/{count.Id}/post", new { })).ReadAsync<StockCountDto>();
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(7);

        // Posted: a new count of it may open.
        (await admin.PostAsJsonAsync("/api/inventory/counts", new CreateStockCountRequest(null, null, [product.Id], null))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_date_range_ending_before_it_starts_is_refused()
    {
        var admin = await factory.LoginAsync();
        (await admin.GetAsync("/api/sales?from=2026-12-01&to=2026-11-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/reports/daily?from=2026-12-01&to=2026-11-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/sales?from=2026-11-01&to=2026-11-01")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_period_report_gives_each_account_its_opening_so_opening_plus_in_minus_out_is_the_closing()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        await admin.FundAsync(drawer, 500);
        // An advance to an employee and an expense dated long ago both leave the drawer.
        await (await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), Domain.Finance.PaymentPurpose.Other, null, "مصروف",
            120, drawer, null, null, null))).ReadAsync<VoucherDto>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));
        var report = await admin.GetFromJsonAsync<PeriodReportDto>($"/api/reports/period?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}", TestJson.Options);
        var row = report!.Accounts!.Single(a => a.AccountId == drawer);
        (row.OpeningBalance + row.In - row.Out).Should().Be(row.ClosingBalance);
        row.ClosingBalance.Should().Be(380);
    }

    [Fact]
    public async Task The_same_product_on_two_lines_gets_one_message_with_the_whole_quantity()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 3, openingCost: 4);
        var response = await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 3, 10), new(product.Id, 3, 10)], [new(drawer, 60)], null));
        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("المطلوب 6").And.Contain("المتوفر 3");
    }

    [Fact]
    public async Task A_unit_counted_in_whole_numbers_refuses_fractions_everywhere_and_others_still_take_them()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        // A unit of its own, so switching it does not touch other tests' products.
        var unit = await (await admin.PostAsJsonAsync("/api/lookups/units", new { name = TestData.Unique("طقم") })).ReadAsync<Application.Lookups.LookupItem>();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 4);
        var request = new SaveProductRequest(product.Name, product.Sku, null, product.CategoryId, null, unit.Id, null, null,
            null, null, null, null, 4, 10, 10, 0, 2, null);
        await (await admin.PutAsJsonAsync($"/api/products/{product.Id}", request)).ReadAsync<ProductDetailDto>();

        // Fractions allowed until the unit is switched (the default keeps today's behaviour).
        (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 0.5m, 10, drawer, 5))).StatusCode.Should().Be(HttpStatusCode.OK);

        var set = await (await admin.PutAsJsonAsync($"/api/lookups/units/{unit.Id}", new { wholeNumbersOnly = true })).ReadAsync<Application.Lookups.UnitLookupItem>();
        set.WholeNumbersOnly.Should().BeTrue();
        (await admin.LookupsAsync()).Units.Single(u => u.Id == unit.Id).WholeNumbersOnly.Should().BeTrue();

        var sale = await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 0.5m, 10, drawer, 5));
        sale.StatusCode.Should().Be((HttpStatusCode)422);
        (await sale.Content.ReadAsStringAsync()).Should().Contain("بدون كسور");
        (await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 0.5m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var supplier = await admin.CreateSupplierAsync();
        (await admin.PostAsJsonAsync("/api/purchases", new SavePurchaseRequest(Guid.NewGuid(), supplier.Id, null, null, null, 0, 0, null, null,
            [new PurchaseLineInput(product.Id, 1.25m, 4)], true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/sales", Sale(product.Id, 2, 10, drawer, 20))).StatusCode.Should().Be(HttpStatusCode.OK, "whole numbers still sell");
    }

    [Fact]
    public async Task Cancelling_a_transfer_puts_the_money_back_and_needs_it_still_in_the_receiving_account()
    {
        var admin = await factory.LoginAsync();
        var from = await DrawerAsync(admin);
        var to = await DrawerAsync(admin);
        await admin.FundAsync(from, 100);
        var transfer = await (await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), from, to, 60, null)))
            .ReadAsync<AccountTransferDto>();
        (await BalanceAsync(from)).Should().Be(40);
        (await BalanceAsync(to)).Should().Be(60);

        var voided = await (await admin.PostAsJsonAsync($"/api/account-transfers/{transfer.Id}/void", new VoidDocumentRequest("مبلغ غلط"))).ReadAsync<AccountTransferDto>();
        voided.VoidedAt.Should().NotBeNull();
        voided.VoidReason.Should().Be("مبلغ غلط");
        (await BalanceAsync(from)).Should().Be(100);
        (await BalanceAsync(to)).Should().Be(0);
        (await admin.PostAsJsonAsync($"/api/account-transfers/{transfer.Id}/void", new VoidDocumentRequest("مرة ثانية"))).StatusCode.Should().Be((HttpStatusCode)422);

        // Spent from the receiving account: it cannot be cancelled.
        var second = await (await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), from, to, 50, null)))
            .ReadAsync<AccountTransferDto>();
        await (await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), to, from, 30, null))).ReadAsync<AccountTransferDto>();
        (await admin.PostAsJsonAsync($"/api/account-transfers/{second.Id}/void", new VoidDocumentRequest("غلط"))).StatusCode.Should().Be((HttpStatusCode)422);
        (await BalanceAsync(to)).Should().Be(20);
    }

    [Fact]
    public async Task A_document_cannot_be_dated_before_the_store_started_on_the_system()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        await admin.FundAsync(drawer, 100);
        var category = (await admin.LookupsAsync()).ExpenseCategories[0].Id;
        var old = await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "قديم", 5, drawer, new DateOnly(1990, 1, 1), null));
        old.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await old.Content.ReadAsStringAsync()).Should().Contain("بداية العمل على النظام");
        (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "اليوم", 5, drawer, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_product_whose_cost_is_zero_is_flagged_for_the_till_warning_without_telling_its_cost()
    {
        var admin = await factory.LoginAsync();
        var free = await admin.CreateProductAsync(retail: 12, openingQuantity: 3, openingCost: 0);
        var costed = await admin.CreateProductAsync(retail: 12, openingQuantity: 3, openingCost: 5);
        (await admin.GetProductAsync(free.Id)).NoCost.Should().BeTrue();
        (await admin.GetProductAsync(costed.Id)).NoCost.Should().BeFalse();
        var list = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?search={Uri.EscapeDataString(free.Name)}", TestJson.Options);
        list!.Items.Single(p => p.Id == free.Id).NoCost.Should().BeTrue();
    }

    [Fact]
    public async Task The_product_list_finds_products_whose_cost_is_above_their_price()
    {
        var admin = await factory.LoginAsync();
        var loss = await admin.CreateProductAsync(retail: 30, openingQuantity: 2, openingCost: 45);
        var fine = await admin.CreateProductAsync(retail: 30, openingQuantity: 2, openingCost: 10);
        var list = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>("/api/products?costAbovePrice=true&pageSize=200", TestJson.Options);
        list!.Items.Should().Contain(p => p.Id == loss.Id).And.NotContain(p => p.Id == fine.Id);
    }
}
