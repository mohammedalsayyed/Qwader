using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Sales;
using Qwader.Application.Users;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>The points of the external review (phase 1): headers, API 404s, the version, the offline changes, and a cashier's reach.</summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityTests(ApiFactory factory)
{
    private async Task<HttpClient> CashierAsync()
    {
        var admin = await factory.LoginAsync();
        var userName = TestData.Unique("cashier");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "كاشير اختبار", null, "Temp12345", [SystemRoles.Sales]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Cashier12345"))).EnsureSuccessStatusCode();
        return await factory.LoginAsync(userName, "Cashier12345");
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/products")]
    [InlineData("/")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        var client = path == "/api/products" ? await factory.LoginAsync() : factory.CreateClient();
        var response = await client.GetAsync(path);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("strict-origin-when-cross-origin");
        response.Headers.GetValues("Permissions-Policy").Single().Should().Contain("camera=(self)").And.Contain("microphone=()");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("default-src 'self'").And.Contain("frame-ancestors 'none'").And.Contain("script-src 'self'").And.NotContain("unsafe-eval");
        response.Headers.Contains("X-Powered-By").Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_api_address_is_a_json_404_not_the_app_page()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist/at-all");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        // The app's own pages still open (the SPA fallback).
        var page = await factory.CreateClient().GetAsync("/products/123");
        page.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_version_is_told_only_after_sign_in()
    {
        (await factory.CreateClient().GetAsync("/api/app-info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var info = await (await factory.LoginAsync()).GetFromJsonAsync<JsonElement>("/api/app-info");
        info.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Offline_changes_are_empty_when_nothing_changed_and_carry_what_did()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10, openingQuantity: 5, openingCost: 4);
        var data = await admin.GetFromJsonAsync<OfflineDataDto>("/api/sales/offline-data", TestJson.Options);
        var cursor = data!.Cursor;
        // Asked a little later with nothing new: the last minute's overlap is behind the client's own time, so nothing comes back.
        string Changes(OfflineCursor c, DateTime at) =>
            $"/api/sales/offline-changes?movementId={c.MovementId}&ledgerEntryId={c.LedgerEntryId}&at={Uri.EscapeDataString(at.ToString("O"))}";
        var idle = await admin.GetFromJsonAsync<OfflineChangesDto>(Changes(cursor, DateTime.UtcNow.AddMinutes(2)), TestJson.Options);
        idle!.Products.Should().BeEmpty();
        idle.Customers.Should().BeEmpty();

        // A sale after the cursor brings that product back with its new stock.
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Name == "الصندوق النقدي").Id;
        (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new SaleLineInput(product.Id, 1, 10)], [new SalePaymentInput(cash, 10)], null))).EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<OfflineChangesDto>(Changes(cursor, DateTime.UtcNow.AddMinutes(2)), TestJson.Options);
        after!.Products.Should().ContainSingle(p => p.Id == product.Id).Which.Quantity.Should().Be(4);
    }

    [Fact]
    public async Task A_cashier_gets_no_cost_and_cannot_open_another_sellers_invoice()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 20, openingQuantity: 10, openingCost: 7);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Name == "الصندوق النقدي").Id;
        var adminsSale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new SaleLineInput(product.Id, 1, 20)], [new SalePaymentInput(cash, 20)], null))).ReadAsync<SaleDetailDto>();

        var cashier = await CashierAsync();

        // The offline copy and the product list carry no cost for a seller.
        var data = await cashier.GetFromJsonAsync<OfflineDataDto>("/api/sales/offline-data", TestJson.Options);
        data!.Products.Should().OnlyContain(p => p.AverageCost == null);
        var raw = await cashier.GetStringAsync("/api/sales/offline-data");
        raw.Should().NotContain("\"averageCost\":7");
        var item = (await cashier.GetFromJsonAsync<Application.Common.PagedResult<Application.Catalog.ProductListItemDto>>($"/api/products?search={product.Sku}", TestJson.Options))!
            .Items.Single();
        item.AverageCost.Should().BeNull();

        // Another seller's invoice does not exist for a cashier (IDOR), and its own invoices show no cost or profit.
        (await cashier.GetAsync($"/api/sales/{adminsSale.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var own = await (await cashier.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new SaleLineInput(product.Id, 1, 20)], [new SalePaymentInput(cash, 20)], null))).ReadAsync<SaleDetailDto>();
        var ownDetail = await cashier.GetFromJsonAsync<SaleDetailDto>($"/api/sales/{own.Id}", TestJson.Options);
        ownDetail!.TotalCost.Should().BeNull();
        ownDetail.Profit.Should().BeNull();
        ownDetail.Lines.Should().OnlyContain(l => l.UnitCost == null);

        // Admin-only screens answer 403.
        foreach (var path in new[] { "/api/settings/integrity", "/api/audit-log", "/api/users", "/api/reports/period", "/api/settings/backup" })
            (await cashier.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var admin = await factory.LoginAsync();
        var userName = TestData.Unique("lock");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "قفل", null, "Temp12345", [SystemRoles.Sales]))).EnsureSuccessStatusCode();
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            (await client.PostAsJsonAsync("/api/auth/login", new { userName, password = "wrong-" + i })).IsSuccessStatusCode.Should().BeFalse();
        // The right password is refused now: the account is locked for a while.
        (await client.PostAsJsonAsync("/api/auth/login", new { userName, password = "Temp12345" })).IsSuccessStatusCode.Should().BeFalse();
    }
}
