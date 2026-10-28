using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Application.Search;
using Qwader.Application.Users;
using Qwader.Domain.Identity;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFactory factory)
{
    [Fact]
    public async Task Product_code_is_generated_when_blank_and_duplicates_are_rejected()
    {
        var admin = await factory.LoginAsync();
        var auto = await admin.CreateProductAsync();
        auto.Sku.Should().MatchRegex(@"^P-\d{5}$");

        var sku = TestData.Unique("SKU");
        await admin.CreateProductAsync(sku: sku);
        var lookups = await admin.LookupsAsync();
        var duplicate = await admin.PostAsJsonAsync("/api/products", new SaveProductRequest("آخر", sku, null, auto.CategoryId, null,
            lookups.Units[0].Id, null, null, null, null, null, null, 1, 5, 5, 0, 1, null));

        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicate.Content.ReadFromJsonAsync<ProblemBody>(TestJson.Options))!.Errors!.Should().ContainKey("sku");
    }

    [Fact]
    public async Task Search_ignores_arabic_spelling_variants_and_finds_by_code()
    {
        var admin = await factory.LoginAsync();
        var marker = TestData.Unique("X");
        var product = await admin.CreateProductAsync(name: $"صحون زجاجيّة {marker}");

        var byVariant = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?search=زجاجيه {marker}", TestJson.Options);
        byVariant!.Items.Should().ContainSingle(p => p.Id == product.Id);

        var global = await admin.GetFromJsonAsync<SearchResult>($"/api/search?q={product.Sku}", TestJson.Options);
        global!.Hits.First().Should().Match<SearchHit>(h => h.Kind == SearchResultKind.Product && h.Id == product.Id);
    }

    [Fact]
    public async Task Expected_profit_and_suggested_price_come_from_cost_and_margin()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 15, openingQuantity: 1, openingCost: 10);

        product.ExpectedProfit.Should().Be(5);
        product.MarginPercent.Should().Be(50);
        product.SuggestedRetailPrice.Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task Sales_role_sees_no_costs_and_cannot_change_products()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(openingQuantity: 1, openingCost: 10);
        var userName = TestData.Unique("sales");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "بائع", null, "Temp12345", [SystemRoles.Sales]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Seller12345"))).EnsureSuccessStatusCode();
        var sales = await factory.LoginAsync(userName, "Seller12345");

        var seen = await sales.GetProductAsync(product.Id);
        seen.AverageCost.Should().BeNull();
        seen.PurchasePrice.Should().BeNull();
        seen.ExpectedProfit.Should().BeNull();
        (await sales.PostAsJsonAsync("/api/categories", new SaveCategoryRequest(null, "ممنوع", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.GetAsync("/api/purchases")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Excel_import_creates_products_with_opening_stock()
    {
        var admin = await factory.LoginAsync();
        var category = TestData.Unique("أواني");
        var file = Workbook(
            ["الكود", "اسم المنتج", "الصنف", "سعر الشراء", "سعر المفرق", "الكمية الافتتاحية"],
            [TestData.Unique("IMP"), "قدر ضغط 7 لتر", category, "80", "120", "4"],
            ["", "مقلاة جرانيت", category, "35", "55", ""]);

        var result = await (await admin.PostAsync("/api/products/import", file)).ReadAsync<ProductImportResult>();

        result.Created.Should().Be(2);
        result.Errors.Should().BeEmpty();
        var list = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>("/api/products?search=قدر ضغط", TestJson.Options);
        list!.Items.Should().Contain(p => p.CategoryName == category && p.Quantity == 4);
    }

    [Fact]
    public async Task The_default_alert_level_fills_imported_products_and_can_be_applied_to_all()
    {
        var admin = await factory.LoginAsync();
        var settings = await admin.GetFromJsonAsync<Application.Settings.SettingsDto>("/api/settings", TestJson.Options);
        (await admin.PutAsJsonAsync("/api/settings", settings! with { DefaultReorderLevel = 10 })).EnsureSuccessStatusCode();
        var custom = await admin.CreateProductAsync(reorderLevel: 25);
        var name = TestData.Unique("بدون حد");
        var file = Workbook(["اسم المنتج", "الصنف", "سعر المفرق"], [name, TestData.Unique("صنف"), "15"]);

        (await (await admin.PostAsync("/api/products/import", file)).ReadAsync<ProductImportResult>()).Created.Should().Be(1);
        var imported = (await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?search={name}", TestJson.Options))!.Items.Single();
        (await admin.GetProductAsync(imported.Id)).ReorderLevel.Should().Be(10, "a blank «حد التنبيه» takes the store default");

        var applied = await (await admin.PostAsync("/api/settings/apply-reorder-level", null)).ReadAsync<System.Text.Json.JsonElement>();
        applied.GetProperty("changed").GetInt32().Should().BeGreaterThan(0);
        (await admin.GetProductAsync(custom.Id)).ReorderLevel.Should().Be(10);
    }

    [Fact]
    public async Task Excel_import_with_any_invalid_row_imports_nothing()
    {
        var admin = await factory.LoginAsync();
        var name = TestData.Unique("لن يُستورد");
        var file = Workbook(
            ["اسم المنتج", "الصنف", "سعر المفرق"],
            [name, "صنف", "10"],
            ["", "صنف", "abc"]);

        var result = await (await admin.PostAsync("/api/products/import", file)).ReadAsync<ProductImportResult>();

        result.Created.Should().Be(0);
        result.Errors.Should().Contain(e => e.Row == 3);
        var list = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?search={name}", TestJson.Options);
        list!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_rejects_a_file_whose_content_is_not_an_image()
    {
        var admin = await factory.LoginAsync();
        var content = new MultipartFormDataContent();
        var fake = new ByteArrayContent("<script>alert(1)</script>"u8.ToArray());
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fake, "file", "photo.jpg");

        var response = await admin.PostAsync("/api/files?purpose=ProductImage", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Uploaded_png_is_served_back()
    {
        var admin = await factory.LoginAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        var content = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "p.png" } };

        var uploaded = await (await admin.PostAsync("/api/files?purpose=ProductImage", content)).ReadAsync<UploadedFile>();
        var served = await factory.CreateClient().GetAsync(uploaded.Url);

        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    }

    private sealed record UploadedFile(Guid Id, string Url);

    private static MultipartFormDataContent Workbook(params string[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("المنتجات");
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 1, c + 1).SetValue(rows[r][c]);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { file, "file", "products.xlsx" } };
    }
}
