using System.Net.Http.Json;
using Qwader.Application.Catalog;
using Qwader.Application.Inventory;
using Qwader.Application.Lookups;
using Qwader.Application.Purchasing;

namespace Qwader.IntegrationTests;

/// <summary>Creates real records through the API so every test exercises the same paths as the UI.</summary>
public static class TestData
{
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;
    }

    public static async Task<LookupsDto> LookupsAsync(this HttpClient client) =>
        await (await client.GetAsync("/api/lookups")).ReadAsync<LookupsDto>();

    public static async Task<CategoryDto> CreateCategoryAsync(this HttpClient client, string? name = null) =>
        await (await client.PostAsJsonAsync("/api/categories", new SaveCategoryRequest(null, name ?? Unique("صنف"), null))).ReadAsync<CategoryDto>();

    public static async Task<ProductDetailDto> CreateProductAsync(this HttpClient client, string? name = null, decimal retail = 20,
        decimal openingQuantity = 0, decimal openingCost = 0, string? sku = null, decimal reorderLevel = 2)
    {
        var lookups = await client.LookupsAsync();
        var category = await client.CreateCategoryAsync();
        var request = new SaveProductRequest(name ?? Unique("منتج"), sku, null, category.Id, null, lookups.Units[0].Id, null, null,
            null, null, null, null, openingCost, retail, retail, 0, reorderLevel, null, openingQuantity, openingCost);
        return await (await client.PostAsJsonAsync("/api/products", request)).ReadAsync<ProductDetailDto>();
    }

    public static async Task<ProductDetailDto> GetProductAsync(this HttpClient client, int id) =>
        await (await client.GetAsync($"/api/products/{id}")).ReadAsync<ProductDetailDto>();

    public static async Task<SupplierDto> CreateSupplierAsync(this HttpClient client, decimal openingBalance = 0) =>
        await (await client.PostAsJsonAsync("/api/suppliers", new SaveSupplierRequest(Unique("مورد"), "0599000000", null, null, true, openingBalance)))
            .ReadAsync<SupplierDto>();

    public static async Task<PurchaseDetailDto> PurchaseAsync(this HttpClient client, int supplierId, IReadOnlyList<PurchaseLineInput> lines,
        decimal discount = 0, decimal paid = 0, int? accountId = null, bool post = true) =>
        await (await client.PostAsJsonAsync("/api/purchases", new SavePurchaseRequest(Guid.NewGuid(), supplierId, null, null, null,
            discount, paid, accountId, null, lines, post))).ReadAsync<PurchaseDetailDto>();

    public static Task<HttpResponseMessage> AdjustAsync(this HttpClient client, int productId, AdjustmentKind kind, decimal quantity, Guid? key = null) =>
        client.PostAsJsonAsync("/api/inventory/adjustments",
            new StockAdjustmentRequest(key ?? Guid.NewGuid(), productId, null, kind, quantity, "اختبار"));
}
