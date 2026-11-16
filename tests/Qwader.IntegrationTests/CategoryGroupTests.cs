using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Application.Inventory;

namespace Qwader.IntegrationTests;

/// <summary>«الصنف العام»: a general category above the categories, for filters and stock totals.</summary>
[Collection(ApiCollection.Name)]
public sealed class CategoryGroupTests(ApiFactory factory)
{
    [Fact]
    public async Task A_general_category_gathers_its_categories_products_in_filters_and_stock_totals()
    {
        var admin = await factory.LoginAsync();
        var electric = await (await admin.PostAsJsonAsync("/api/category-groups", new SaveCategoryGroupRequest(TestData.Unique("أجهزة كهربائية")))).ReadAsync<CategoryGroupDto>();
        (await admin.PostAsJsonAsync("/api/category-groups", new SaveCategoryGroupRequest(electric.Name))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "names are unique");

        // Two products (5 × cost 10 and 2 × cost 30) in two categories, both put under «أجهزة كهربائية».
        var first = await admin.CreateProductAsync(retail: 20, openingQuantity: 5, openingCost: 10);
        var second = await admin.CreateProductAsync(retail: 50, openingQuantity: 2, openingCost: 30);
        foreach (var p in new[] { first, second })
        {
            var category = await (await admin.PutAsJsonAsync($"/api/categories/{p.CategoryId}",
                new SaveCategoryRequest(null, p.CategoryName, null, true, electric.Id))).ReadAsync<CategoryDto>();
            (category.GroupId, category.GroupName).Should().Be((electric.Id, electric.Name));
        }
        (await admin.PutAsJsonAsync($"/api/categories/{first.CategoryId}", new SaveCategoryRequest(null, first.CategoryName, null, true, 999_999)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unknown general category is refused");

        var listed = await (await admin.GetAsync($"/api/products?groupId={electric.Id}&pageSize=100")).ReadAsync<PagedResult<ProductListItemDto>>();
        listed.Items.Select(p => p.Id).Should().BeEquivalentTo([first.Id, second.Id]);

        var summary = await (await admin.GetAsync("/api/inventory/summary")).ReadAsync<InventorySummaryDto>();
        var row = summary.Groups!.Single(g => g.GroupId == electric.Id);
        (row.ProductCount, row.Quantity, row.Value).Should().Be((2, 7m, 110m));
        summary.Groups!.Should().Contain(g => g.GroupId == null, "the products of categories without one are shown apart");
        (await admin.LookupsAsync()).CategoryGroups!.Should().Contain(g => g.Id == electric.Id);

        // Removing it keeps its categories, without a general category.
        (await admin.DeleteAsync($"/api/category-groups/{electric.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await admin.GetAsync($"/api/categories/{first.CategoryId}")).ReadAsync<CategoryDto>()).GroupId.Should().BeNull();
    }
}
