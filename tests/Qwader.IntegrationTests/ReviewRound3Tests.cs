using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Settings;
using Qwader.Domain.Finance;

namespace Qwader.IntegrationTests;

/// <summary>Third round of the store's test report: inputs, opening balances, drafts, the audit log, push addresses, variants.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReviewRound3Tests(ApiFactory factory)
{
    private async Task<int> DrawerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("درج"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;

    [Fact]
    public async Task An_opening_balance_must_be_given_and_cannot_take_the_account_below_zero()
    {
        var admin = await factory.LoginAsync();
        var drawer = await DrawerAsync(admin);
        await admin.FundAsync(drawer, 100);

        (await admin.PostAsJsonAsync($"/api/financial-accounts/{drawer}/opening-balance", new { notes = "ناقص" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "a missing amount never zeroes the opening balance");
        (await admin.GetFromJsonAsync<AccountDto>($"/api/financial-accounts/{drawer}", TestJson.Options))!.OpeningBalance.Should().Be(100);

        // 80 of the 100 moved away: the opening balance cannot go down to 0 (the account would be −80).
        var other = await DrawerAsync(admin);
        (await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), drawer, other, 80, null))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/financial-accounts/{drawer}/opening-balance", new SetOpeningBalanceRequest(0, "تصفير"))).StatusCode
            .Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task A_draft_with_a_product_archived_since_cannot_be_posted()
    {
        var admin = await factory.LoginAsync();
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 10);
        var draft = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 2, 5)], post: false);
        (await admin.PostAsync($"/api/products/{product.Id}/archive", null)).EnsureSuccessStatusCode();

        var post = await admin.PostAsJsonAsync($"/api/purchases/{draft.Id}/post", new PostPurchaseRequest(Guid.NewGuid()));
        post.StatusCode.Should().Be((HttpStatusCode)422);
        (await post.Content.ReadAsStringAsync()).Should().Contain("مؤرشف");
    }

    [Fact]
    public async Task The_audit_log_reads_every_page_including_purchase_entries()
    {
        var admin = await factory.LoginAsync();
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 10);
        await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 1, 5)]);
        var page = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/audit-log?pageSize=50", TestJson.Options);
        page!.Items.Should().Contain(a => a.EntityType == "PurchaseInvoice" && a.EntityLabel != null && a.EntityLabel.Contains(supplier.Name));
    }

    [Fact]
    public async Task Push_subscriptions_accept_the_browsers_push_services_only()
    {
        var admin = await factory.LoginAsync();
        object Body(string endpoint) => new { endpoint, p256dh = "key", auth = "auth", topics = new { }, device = "test" };
        (await admin.PutAsJsonAsync("/api/push/subscription", Body("https://localhost/x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/push/subscription", Body("https://10.0.0.5/x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/push/subscription", Body("https://evil.fcm.googleapis.com.example/x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Two_variants_of_one_product_cannot_share_size_colour_and_model()
    {
        var admin = await factory.LoginAsync();
        var parent = await admin.CreateProductAsync(retail: 10);
        SaveProductRequest Variant(string size) => new(parent.Name, null, null, parent.CategoryId, null, parent.UnitId, null, parent.Id,
            size, "أحمر", null, null, 0, 10, 10, 0, 2, null);
        (await admin.PostAsJsonAsync("/api/products", Variant("كبير"))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/products", Variant("كبير"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/products", Variant("صغير"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hidden_characters_are_removed_and_huge_numbers_refused()
    {
        var admin = await factory.LoginAsync();
        var name = "زبون‮" + TestData.Unique("مخفي") + "​\u0000";
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(name, "0591" + Random.Shared.Next(100000, 999999), null, null)))
            .ReadAsync<CustomerDto>();
        customer.Name.Should().NotContain("‮").And.NotContain("​").And.NotContain("\u0000").And.StartWith("زبون");

        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 2);
        var account = (await admin.LookupsAsync()).Accounts[0].Id;
        var huge = await admin.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = Guid.NewGuid(), priceTier = "Retail", invoiceDiscount = 0,
            lines = new[] { new { productId = product.Id, quantity = 1e15m, unitPrice = 10m, lineDiscount = 0m } },
            payments = new[] { new { accountId = account, amount = 10m } },
        });
        huge.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_category_written_with_two_spaces_is_the_same_category_and_api_answers_are_not_cached()
    {
        var admin = await factory.LoginAsync();
        var words = TestData.Unique("صنف");
        (await admin.PostAsJsonAsync("/api/categories", new SaveCategoryRequest(null, $"{words} مطبخ", null, true))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/categories", new SaveCategoryRequest(null, $"{words}  مطبخ", null, true))).StatusCode
            .Should().NotBe(HttpStatusCode.OK);

        var response = await admin.GetAsync("/api/lookups");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
}
