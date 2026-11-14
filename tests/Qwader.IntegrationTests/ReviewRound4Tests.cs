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

/// <summary>Round 4, the rest: units, archived products, user names, accounts, transfers, statements.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReviewRound4MoreTests(ApiFactory factory)
{
    private async Task<int> AccountAsync(HttpClient admin, FinancialAccountType type = FinancialAccountType.Cash) =>
        (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("حساب"), type, null, null, null))).ReadAsync<AccountDto>()).Id;

    [Fact]
    public async Task A_products_unit_stays_once_stock_moved_and_wholesale_is_not_above_retail()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 20, openingCost: 4);
        var other = (await admin.LookupsAsync()).Units.First(u => u.Id != product.UnitId).Id;
        Application.Catalog.SaveProductRequest With(int unitId, decimal wholesale) => new(product.Name, product.Sku, null, product.CategoryId, null, unitId, null, null,
            null, null, null, null, 4, wholesale, 10, 0, 2, null);
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", With(other, 10))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", With(product.UnitId, 20))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", With(product.UnitId, 9))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nothing_moves_on_an_archived_product()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 3, openingCost: 4);
        (await admin.PostAsync($"/api/products/{product.Id}/archive", null)).EnsureSuccessStatusCode();
        (await admin.AdjustAsync(product.Id, Application.Inventory.AdjustmentKind.Found, 5)).StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task A_user_name_takes_no_hidden_or_direction_characters()
    {
        var admin = await factory.LoginAsync();
        // The direction character is taken out as the request comes in; the name stored is plain.
        var created = await (await admin.PostAsJsonAsync("/api/users", new Application.Users.CreateUserRequest("qa‮test" + Random.Shared.Next(100000), "مخفي", null, "Temp12345",
            [Domain.Identity.SystemRoles.Sales]))).ReadAsync<Application.Users.UserDto>();
        created.UserName.Should().NotContain("‮").And.StartWith("qatest");
        (await admin.PostAsJsonAsync("/api/users", new Application.Users.CreateUserRequest("qa test<b>" + Random.Shared.Next(100000), "مسافة", null, "Temp12345",
            [Domain.Identity.SystemRoles.Sales]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "letters, digits, . _ - only");
    }

    [Fact]
    public async Task Accounts_keep_their_type_once_used_and_stay_active_with_pending_transfers_and_cash_takes_no_transfers()
    {
        var admin = await factory.LoginAsync();
        var wallet = await AccountAsync(admin, FinancialAccountType.Wallet);
        var cash = await AccountAsync(admin);
        (await admin.PostAsJsonAsync("/api/incoming-transfers", new CreateIncomingTransferRequest(Guid.NewGuid(), "مرسل", null, 30, null, cash, null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a transfer reaches a bank or a wallet");
        (await admin.PostAsJsonAsync("/api/incoming-transfers", new CreateIncomingTransferRequest(Guid.NewGuid(), "مرسل", null, 30, null, wallet, null, null, null, null, null)))
            .EnsureSuccessStatusCode();
        var name = (await admin.GetFromJsonAsync<AccountDto>($"/api/financial-accounts/{wallet}", TestJson.Options))!.Name;
        (await admin.PutAsJsonAsync($"/api/financial-accounts/{wallet}", new SaveAccountRequest(name, FinancialAccountType.Wallet, null, null, null, false)))
            .StatusCode.Should().Be((HttpStatusCode)422, "a pending transfer waits on it");

        await admin.FundAsync(cash, 50);
        (await admin.PutAsJsonAsync($"/api/financial-accounts/{cash}", new SaveAccountRequest(TestData.Unique("حساب"), FinancialAccountType.Bank, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "money has moved through it");
    }

    [Fact]
    public async Task Vouchers_refuse_a_third_decimal_or_a_missing_account_with_a_clear_message()
    {
        var admin = await factory.LoginAsync();
        var cash = await AccountAsync(admin);
        await admin.FundAsync(cash, 50);
        (await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.Other, null, "مستفيد", 0.001m, cash, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var missing = await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.Other, null, "مستفيد", 5, 999999, null, null, null));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("الحساب المالي غير موجود");
    }

    [Fact]
    public async Task The_account_statement_runs_its_balance_in_date_order()
    {
        var admin = await factory.LoginAsync();
        var cash = await AccountAsync(admin);
        await admin.FundAsync(cash, 100);
        var category = (await admin.LookupsAsync()).ExpenseCategories[0].Id;
        await (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "اليوم", 10, cash, null, null))).ReadAsync<ExpenseDto>();

        var statement = await admin.GetFromJsonAsync<AccountStatementDto>($"/api/financial-accounts/{cash}/statement", TestJson.Options);
        var lines = statement!.Movements.Items;
        lines[0].BalanceAfter.Should().Be(statement.ClosingBalance, "the newest line ends at the closing balance");
        for (var i = 0; i < lines.Count - 1; i++)
            lines[i + 1].BalanceAfter.Should().Be(lines[i].BalanceAfter - (lines[i].Direction == MoneyDirection.In ? lines[i].Amount : -lines[i].Amount));
    }
}
