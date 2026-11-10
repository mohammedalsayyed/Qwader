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

    [Fact]
    public async Task A_new_unit_can_be_added_from_the_form_or_an_import_and_the_template_lists_units()
    {
        var admin = await factory.LoginAsync();
        var box = "كرتونة " + Guid.NewGuid().ToString("N")[..6];
        var added = await (await admin.PostAsJsonAsync("/api/lookups/units", new { name = "  " + box + " " })).ReadAsync<Application.Lookups.LookupItem>();
        added.Name.Should().Be(box);
        (await (await admin.PostAsJsonAsync("/api/lookups/units", new { name = box })).ReadAsync<Application.Lookups.LookupItem>()).Id.Should().Be(added.Id, "the same name is not added twice");
        (await admin.LookupsAsync()).Units.Should().Contain(u => u.Id == added.Id);

        // An import naming a unit the store does not have yet adds it, instead of refusing the row.
        var roll = "لفة " + Guid.NewGuid().ToString("N")[..6];
        var name = TestData.Unique("سلك");
        var file = Workbook(["اسم المنتج", "الصنف", "الوحدة", "سعر المفرق"], [name, TestData.Unique("صنف"), roll, "15"]);
        (await (await admin.PostAsync("/api/products/import", file)).ReadAsync<ProductImportResult>()).Created.Should().Be(1);
        var imported = (await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?search={name}", TestJson.Options))!.Items.Single();
        imported.UnitName.Should().Be(roll);

        // The template offers the units as a drop-down in its unit column.
        var template = await admin.GetByteArrayAsync("/api/products/import-template");
        using var wb = new XLWorkbook(new MemoryStream(template));
        var sheet = wb.Worksheet("المنتجات");
        var unitColumn = sheet.Row(1).CellsUsed().First(c => c.GetString() == "الوحدة").Address.ColumnNumber;
        sheet.DataValidations.Should().Contain(v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == unitColumn));
        var lists = wb.Worksheet("الأصناف والوحدات");
        lists.Visibility.Should().Be(XLWorksheetVisibility.Visible);
        lists.Column(2).CellsUsed().Select(c => c.GetString()).Should().Contain([box, roll]);
    }

    [Fact]
    public async Task Search_ignores_spaces_typed_or_left_out()
    {
        var admin = await factory.LoginAsync();
        var tag = Guid.NewGuid().ToString("N")[..5];
        var product = await admin.CreateProductAsync(name: $"صحن بورسولان {tag}");

        foreach (var term in new[] { $"صحنبورسولان {tag}", $"صحن بورسو لان {tag}", $"صحنبورسولان{tag}" })
        {
            var found = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"/api/products?search={Uri.EscapeDataString(term)}", TestJson.Options);
            found!.Items.Should().ContainSingle(p => p.Id == product.Id, term);
        }
    }

    [Fact]
    public async Task Excel_export_holds_what_the_filters_show_and_lists_the_categories()
    {
        var admin = await factory.LoginAsync();
        var category = await admin.CreateCategoryAsync();
        var lookups = await admin.LookupsAsync();
        async Task<ProductDetailDto> Create(string name, decimal qty) => await (await admin.PostAsJsonAsync("/api/products", new SaveProductRequest(name, null, null,
            category.Id, null, lookups.Units[0].Id, null, null, null, null, null, null, 1, 5, 5, 0, 2, null, qty, 1))).ReadAsync<ProductDetailDto>();
        var inStock = await Create(TestData.Unique("متوفر"), 20);
        var empty = await Create(TestData.Unique("نافد"), 0);

        var file = await admin.GetByteArrayAsync($"/api/products/export?categoryId={category.Id}&stock=Out");
        using var wb = new XLWorkbook(new MemoryStream(file));
        var names = wb.Worksheet("المنتجات").Column(3).CellsUsed().Skip(1).Select(c => c.GetString()).ToList();
        names.Should().Equal(empty.Name);
        wb.Worksheet("الأصناف والوحدات").Column(1).CellsUsed().Select(c => c.GetString()).Should().Contain(category.Name);
        _ = inStock;
    }

    [Fact]
    public async Task Excel_import_without_a_code_updates_the_product_with_that_name()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 10);
        var file = Workbook(["اسم المنتج", "الصنف", "سعر المفرق"], [product.Name, product.CategoryName, "12.5"]);

        var result = await (await admin.PostAsync("/api/products/import", file)).ReadAsync<ProductImportResult>();

        result.Should().BeEquivalentTo(new { Created = 0, Updated = 1 });
        (await admin.GetProductAsync(product.Id)).RetailPrice.Should().Be(12.5m);

        // Two variants with that name: the row must say which (by size here), or give the code.
        var name = TestData.Unique("كاسة");
        var lookups = await admin.LookupsAsync();
        foreach (var size in new[] { "صغير", "كبير" })
            await admin.PostAsJsonAsync("/api/products", new SaveProductRequest(name, null, null, product.CategoryId, null, lookups.Units[0].Id, null, null,
                size, null, null, null, 1, 5, 5, 0, 2, null));
        var ambiguous = await (await admin.PostAsync("/api/products/import", Workbook(["اسم المنتج", "الصنف", "سعر المفرق"], [name, product.CategoryName, "7"])))
            .ReadAsync<ProductImportResult>();
        ambiguous.Errors.Should().ContainSingle().Which.Message.Should().Contain("لأكثر من منتج");
        var bySize = await (await admin.PostAsync("/api/products/import", Workbook(["اسم المنتج", "الصنف", "المقاس", "سعر المفرق"], [name, product.CategoryName, "كبير", "7"])))
            .ReadAsync<ProductImportResult>();
        bySize.Should().BeEquivalentTo(new { Created = 0, Updated = 1 });
    }

    [Fact]
    public async Task Excel_actual_quantity_counts_existing_products_and_opens_those_never_stocked()
    {
        var admin = await factory.LoginAsync();
        var stocked = await admin.CreateProductAsync(retail: 10, openingQuantity: 10, openingCost: 4);
        var neverStocked = await admin.CreateProductAsync(retail: 10);
        var untouched = await admin.CreateProductAsync(retail: 10, openingQuantity: 3, openingCost: 4);
        var file = Workbook(["الكود", "اسم المنتج", "الصنف", "سعر الشراء", "سعر المفرق", "الكمية الفعلية"],
            [stocked.Sku, stocked.Name, stocked.CategoryName, "", "10", "7"],
            [neverStocked.Sku, neverStocked.Name, neverStocked.CategoryName, "6", "10", "5"],
            [untouched.Sku, untouched.Name, untouched.CategoryName, "", "10", ""]);

        var result = await (await admin.PostAsync("/api/products/import", file)).ReadAsync<ProductImportResult>();

        result.Errors.Should().BeEmpty();
        result.StockChanged.Should().Be(2);
        result.StockCountNumber.Should().NotBeNull();
        (await admin.GetProductAsync(stocked.Id)).Quantity.Should().Be(7);
        var opened = await admin.GetProductAsync(neverStocked.Id);
        opened.Quantity.Should().Be(5);
        opened.AverageCost.Should().Be(6, "a product never stocked opens at its purchase price, not as a count gain");
        (await admin.GetProductAsync(untouched.Id)).Quantity.Should().Be(3, "a blank cell leaves the stock alone");

        var decrease = await (await admin.GetAsync($"/api/inventory/movements?productId={stocked.Id}&type=CountDecrease")).ReadAsync<PagedResult<Application.Inventory.StockMovementDto>>();
        decrease.Items.Should().ContainSingle().Which.Should().Match<Application.Inventory.StockMovementDto>(m => m.Quantity == -3 && m.SourceNumber == result.StockCountNumber);
        var gains = await (await admin.GetAsync($"/api/inventory/movements?productId={neverStocked.Id}&group=Gains")).ReadAsync<PagedResult<Application.Inventory.StockMovementDto>>();
        gains.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Categories_and_units_added_on_the_lists_sheet_are_created_even_without_products()
    {
        var admin = await factory.LoginAsync();
        var category = TestData.Unique("صنف ورقة");
        var unit = "علبة " + Guid.NewGuid().ToString("N")[..5];

        // The template as downloaded, with one new category and one new unit typed on its second sheet and no product rows.
        var template = await admin.GetByteArrayAsync("/api/products/import-template");
        using var wb = new XLWorkbook(new MemoryStream(template));
        wb.Worksheet("المنتجات").Row(2).Clear();
        var lists = wb.Worksheet("الأصناف والوحدات");
        lists.Cell(lists.Column(1).LastCellUsed()!.Address.RowNumber + 1, 1).SetValue(category);
        lists.Cell(lists.Column(2).LastCellUsed()!.Address.RowNumber + 1, 2).SetValue(unit);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await (await admin.PostAsync("/api/products/import", new MultipartFormDataContent { { file, "file", "products.xlsx" } }))
            .ReadAsync<ProductImportResult>();

        result.Errors.Should().BeEmpty();
        result.Should().BeEquivalentTo(new { Created = 0, Updated = 0, CategoriesAdded = 1, UnitsAdded = 1 });
        var lookups = await admin.LookupsAsync();
        lookups.Categories.Should().Contain(c => c.Name == category);
        lookups.Units.Should().Contain(u => u.Name == unit);
    }

    [Fact]
    public async Task Exported_actual_quantity_starts_at_the_current_one_and_only_edited_rows_change_stock()
    {
        var admin = await factory.LoginAsync();
        var category = await admin.CreateCategoryAsync();
        var lookups = await admin.LookupsAsync();
        async Task<ProductDetailDto> Create(decimal qty) => await (await admin.PostAsJsonAsync("/api/products", new SaveProductRequest(TestData.Unique("عد"), null, null,
            category.Id, null, lookups.Units[0].Id, null, null, null, null, null, null, 4, 9, 9, 0, 2, null, qty, 4))).ReadAsync<ProductDetailDto>();
        var counted = await Create(10);
        var soldMeanwhile = await Create(5);

        var exported = await admin.GetByteArrayAsync($"/api/products/export?categoryId={category.Id}");
        using var wb = new XLWorkbook(new MemoryStream(exported));
        var sheet = wb.Worksheet("المنتجات");
        int Col(string header) => sheet.Row(1).CellsUsed().First(c => c.GetString() == header).Address.ColumnNumber;
        var current = Col("الكمية الحالية");
        var actual = Col("الكمية الفعلية");
        actual.Should().Be(current + 1, "«الكمية الفعلية» comes right after «الكمية الحالية»");
        var rows = sheet.RowsUsed().Skip(1).ToDictionary(r => r.Cell(Col("الكود")).GetString());
        rows[counted.Sku].Cell(current).GetDouble().Should().Be(10);
        rows[counted.Sku].Cell(actual).GetDouble().Should().Be(10, "it starts equal to the current quantity");

        // After the export two units of the other product leave the shelf; the file still says 5 for it.
        (await admin.AdjustAsync(soldMeanwhile.Id, Application.Inventory.AdjustmentKind.Damaged, 2)).EnsureSuccessStatusCode();
        rows[counted.Sku].Cell(actual).SetValue(8);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await (await admin.PostAsync("/api/products/import", new MultipartFormDataContent { { file, "file", "products.xlsx" } }))
            .ReadAsync<ProductImportResult>();

        result.Errors.Should().BeEmpty();
        result.StockChanged.Should().Be(1);
        (await admin.GetProductAsync(counted.Id)).Quantity.Should().Be(8);
        (await admin.GetProductAsync(soldMeanwhile.Id)).Quantity.Should().Be(3, "an untouched row does not bring back the old quantity");
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
