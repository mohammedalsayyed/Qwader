using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Qwader.Application.Catalog;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Domain.Finance;

namespace Qwader.IntegrationTests;

/// <summary>Guards found in the manual test-case sheet that had no automated check yet.</summary>
[Collection(ApiCollection.Name)]
public sealed class BusinessRuleTests(ApiFactory factory)
{
    private static string Phone() => "057" + Random.Shared.Next(1000000, 9999999);

    [Fact]
    public async Task A_barcode_belongs_to_one_product_only()
    {
        var admin = await factory.LoginAsync();
        var lookups = await admin.LookupsAsync();
        var category = await admin.CreateCategoryAsync();
        var barcode = "629" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999);
        SaveProductRequest Product() => new(TestData.Unique("باركود"), null, barcode, category.Id, null, lookups.Units[0].Id, null, null,
            null, null, null, null, 10, 20, 20, 0, 1, null);

        (await admin.PostAsJsonAsync("/api/products", Product())).EnsureSuccessStatusCode();
        var duplicate = await admin.PostAsJsonAsync("/api/products", Product());

        duplicate.IsSuccessStatusCode.Should().BeFalse();
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("barcode");
    }

    [Fact]
    public async Task A_price_change_is_kept_in_the_audit_log()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 50);
        var request = new SaveProductRequest(product.Name, product.Sku, product.Barcode, product.CategoryId, null, product.UnitId, null, null,
            null, null, null, null, null, 60, 65, 0, 1, null);

        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", request)).EnsureSuccessStatusCode();

        var log = await admin.GetFromJsonAsync<JsonElement>("/api/audit-log?area=Product&pageSize=50", TestJson.Options);
        log.GetProperty("items").EnumerateArray()
            .Should().Contain(e => e.GetProperty("entityId").GetString() == product.Id.ToString() && e.GetProperty("newValues").GetString()!.Contains("65"));
    }

    [Fact]
    public async Task A_transfer_reference_cannot_be_used_twice_on_the_same_account()
    {
        var admin = await factory.LoginAsync();
        var wallet = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Wallet).Id;
        var reference = TestData.Unique("REF");
        CreateIncomingTransferRequest Transfer() => new(Guid.NewGuid(), "مرسل اختبار", Phone(), 75, null, wallet, reference, null, null, null, null);

        var first = await (await admin.PostAsJsonAsync("/api/incoming-transfers", Transfer())).ReadAsync<IncomingTransferDto>();
        var second = await admin.PostAsJsonAsync("/api/incoming-transfers", Transfer());

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync()).Should().Contain(first.Number);
    }

    [Fact]
    public async Task Collecting_more_than_the_customer_owes_is_refused()
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("مدين"), Phone(), null, null, true, 100)))
            .ReadAsync<CustomerDto>();

        var response = await admin.PostAsJsonAsync("/api/receipt-vouchers",
            new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id, null, null, 150, cash, null, null, null));

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Contain("أكبر من دين");
    }

    [Fact]
    public async Task Customer_statement_runs_the_balance_line_by_line_and_ends_at_the_balance()
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("كشف"), Phone(), null, null, true, 300)))
            .ReadAsync<CustomerDto>();
        (await admin.PostAsJsonAsync("/api/receipt-vouchers",
            new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id, null, null, 120, cash, null, null, null))).EnsureSuccessStatusCode();

        var statement = await admin.GetFromJsonAsync<StatementDto>($"/api/customers/{customer.Id}/statement", TestJson.Options);
        var current = await (await admin.GetAsync($"/api/customers/{customer.Id}")).ReadAsync<CustomerDto>();

        var running = statement!.OpeningBalance;
        foreach (var line in statement.Lines)
        {
            running += line.Debit - line.Credit;
            line.Balance.Should().Be(running);
        }
        statement.ClosingBalance.Should().Be(180).And.Be(current.Balance);
    }

    [Fact]
    public async Task Transfer_proofs_are_not_served_without_signing_in()
    {
        var admin = await factory.LoginAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        var content = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "proof.png" } };
        var uploaded = await (await admin.PostAsync("/api/files?purpose=TransferProof", content)).ReadAsync<JsonElement>();
        var url = uploaded.GetProperty("url").GetString()!;

        (await factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
