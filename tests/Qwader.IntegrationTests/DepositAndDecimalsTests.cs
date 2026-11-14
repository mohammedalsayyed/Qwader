using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Sales;
using Qwader.Application.Purchasing;
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

/// <summary>The server's line totals are the till's: quantity × price charged, rounded once.</summary>
[Collection(ApiCollection.Name)]
public sealed class LineCentsTests(ApiFactory factory)
{
    [Fact]
    public async Task Lowered_prices_on_fractional_quantities_total_exactly_what_the_till_shows()
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var products = new List<(int Id, decimal Quantity, decimal Price)>();
        foreach (var (q, list, price) in new[] { (0.845m, 35m, 34.99m), (0.84m, 35m, 34.995m), (0.685m, 35m, 35m), (0.485m, 35m, 34.97m), (0.333m, 7m, 6.95m) })
        {
            var p = await admin.CreateProductAsync(retail: list, openingQuantity: 5, openingCost: 1);
            products.Add((p.Id, q, Math.Round(price, 2)));
        }
        // What the till computes: each line quantity × price rounded once, then the whole shekels (agorot as discount).
        var lines = products.Select(p => Math.Round(p.Quantity * p.Price, 2, MidpointRounding.AwayFromZero)).ToList();
        var subtotal = lines.Sum();
        var total = decimal.Floor(subtotal);
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, subtotal - total,
            products.Select(p => new SaleLineInput(p.Id, p.Quantity, p.Price)).ToList(), [new(cash, total)], null))).ReadAsync<SaleDetailDto>();
        sale.Total.Should().Be(total);
        sale.Lines.Sum(l => l.LineTotal).Should().Be(subtotal);
    }
}

/// <summary>A till whose lines came out a cent above the server's still gets the whole total it showed and was paid.</summary>
[Collection(ApiCollection.Name)]
public sealed class CentApartTests(ApiFactory factory)
{
    [Fact]
    public async Task A_whole_payment_a_few_agorot_above_the_count_keeps_the_total_the_till_showed()
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var product = await admin.CreateProductAsync(retail: 284.52m, openingQuantity: 5, openingCost: 1);
        // The till counted 284.53 and discounted 4.53 to show 280; the server counts 284.52.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 4.53m,
            [new(product.Id, 1, 284.52m)], [new(cash, 280)], null))).ReadAsync<SaleDetailDto>();
        sale.Total.Should().Be(280);
        sale.InvoiceDiscount.Should().Be(4.52m);
        sale.Remaining.Should().Be(0);
    }
}

/// <summary>An account's logo: uploaded, set on the account, listed with it, and shown to the till.</summary>
[Collection(ApiCollection.Name)]
public sealed class AccountLogoTests(ApiFactory factory)
{
    private sealed record Uploaded(Guid Id, string Url);

    [Fact]
    public async Task An_account_takes_a_logo_that_the_lists_carry_and_anyone_at_the_till_can_load()
    {
        var admin = await factory.LoginAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        var logo = await (await admin.PostAsync("/api/files?purpose=AccountLogo",
            new MultipartFormDataContent { { new ByteArrayContent(png), "file", "palpay.png" } })).ReadAsync<Uploaded>();

        var account = await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("بال بي"), FinancialAccountType.Wallet, null, null, null, true, logo.Id))).ReadAsync<AccountDto>();
        account.LogoFileId.Should().Be(logo.Id);
        (await admin.LookupsAsync()).Accounts.Single(a => a.Id == account.Id).LogoFileId.Should().Be(logo.Id);
        (await factory.CreateClient().GetAsync(logo.Url)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Not just any file: a transfer proof cannot be an account's logo.
        var proof = await (await admin.PostAsync("/api/files?purpose=TransferProof",
            new MultipartFormDataContent { { new ByteArrayContent(png), "file", "proof.png" } })).ReadAsync<Uploaded>();
        (await admin.PutAsJsonAsync($"/api/financial-accounts/{account.Id}",
            new SaveAccountRequest(account.Name, FinancialAccountType.Wallet, null, null, null, true, proof.Id))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // And it can be removed.
        (await (await admin.PutAsJsonAsync($"/api/financial-accounts/{account.Id}",
            new SaveAccountRequest(account.Name, FinancialAccountType.Wallet, null, null, null, true, null))).ReadAsync<AccountDto>()).LogoFileId.Should().BeNull();
    }

    [Fact]
    public async Task A_day_shows_each_account_at_its_start_what_came_in_and_out_and_at_its_end()
    {
        var admin = await factory.LoginAsync();
        var drawer = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("درج يوم"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        await admin.FundAsync(drawer, 100);
        await admin.FundAsync(drawer, -30);

        var today = await (await admin.GetAsync("/api/financial-accounts/day")).ReadAsync<AccountsDayDto>();
        var row = today.Accounts.Single(a => a.AccountId == drawer);
        (row.Opening, row.In, row.Out, row.Closing).Should().Be((0m, 100m, 30m, 70m));
        row.Closing.Should().Be((await admin.GetFromJsonAsync<AccountDto>($"/api/financial-accounts/{drawer}", TestJson.Options))!.CurrentBalance);
        today.In.Should().Be(today.Accounts.Sum(a => a.In));
        today.Closing.Should().Be(today.Opening + today.In - today.Out);

        // Yesterday the drawer held nothing; tomorrow cannot be asked yet.
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var before = await (await admin.GetAsync($"/api/financial-accounts/day?date={yesterday:yyyy-MM-dd}")).ReadAsync<AccountsDayDto>();
        before.Accounts.Single(a => a.AccountId == drawer).Closing.Should().Be(0);
        (await admin.GetAsync($"/api/financial-accounts/day?date={yesterday.AddDays(3):yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>An old balance from before the system, added to an existing customer: on them or theirs.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerOldBalanceTests(ApiFactory factory)
{
    [Fact]
    public async Task An_existing_customer_takes_an_old_debt_or_an_old_credit_without_moving_any_account()
    {
        var admin = await factory.LoginAsync();
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("محمد فايز"), null, null, null))).ReadAsync<CustomerDto>();
        var cashBefore = (await admin.GetFromJsonAsync<List<AccountDto>>("/api/financial-accounts", TestJson.Options))!.Sum(a => a.CurrentBalance);

        (await (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/old-balance", new CustomerOldBalanceRequest(150, true, "دفتر قديم")))
            .ReadAsync<CustomerDto>()).Balance.Should().Be(150);
        (await (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/old-balance", new CustomerOldBalanceRequest(200, false, null)))
            .ReadAsync<CustomerDto>()).Balance.Should().Be(-50);

        var statement = await (await admin.GetAsync($"/api/customers/{customer.Id}/statement")).ReadAsync<StatementDto>();
        statement.Lines.Should().HaveCount(2).And.OnlyContain(l => l.Type == "رصيد سابق");
        statement.Lines[0].Description.Should().Contain("دفتر قديم");
        (await admin.GetFromJsonAsync<List<AccountDto>>("/api/financial-accounts", TestJson.Options))!.Sum(a => a.CurrentBalance).Should().Be(cashBefore);

        (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/old-balance", new CustomerOldBalanceRequest(0, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var walkIn = (await (await admin.GetAsync("/api/customers?search=زبون نقدي")).ReadAsync<Application.Common.PagedResult<CustomerListItemDto>>()).Items.Single(c => c.IsWalkIn);
        (await admin.PostAsJsonAsync($"/api/customers/{walkIn.Id}/old-balance", new CustomerOldBalanceRequest(10, true, null))).IsSuccessStatusCode.Should().BeFalse();

        // Found by name, each word anywhere in it.
        var found = await (await admin.GetAsync($"/api/customers?search={Uri.EscapeDataString("فائز محمد")}")).ReadAsync<Application.Common.PagedResult<CustomerListItemDto>>();
        found.Items.Should().Contain(c => c.Id == customer.Id);
    }

    [Fact]
    public async Task A_name_typed_with_Persian_letters_is_found_by_its_Arabic_spelling_even_when_stored_before()
    {
        var admin = await factory.LoginAsync();
        var tag = TestData.Unique("x");
        var typed = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest($"محمد فایز {tag}", null, null, null))).ReadAsync<CustomerDto>();
        var old = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest($"سامی کمال {tag}", null, null, null))).ReadAsync<CustomerDto>();
        // As stored before the letters were folded: the search column kept the Persian ی and ک.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Customers.Where(c => c.Id == old.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.NormalizedName, $"سامی کمال {tag}".ToLowerInvariant()));
        }
        async Task<IEnumerable<int>> Find(string q) =>
            (await (await admin.GetAsync($"/api/customers?search={Uri.EscapeDataString(q)}")).ReadAsync<Application.Common.PagedResult<CustomerListItemDto>>()).Items.Select(c => c.Id);
        (await Find($"محمد فايز {tag}")).Should().Contain(typed.Id);
        (await Find($"سامي كمال {tag}")).Should().Contain(old.Id);
        (await Find($"سامی {tag}")).Should().Contain(old.Id);
    }
}
