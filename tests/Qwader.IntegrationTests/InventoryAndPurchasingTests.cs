using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Catalog;
using Qwader.Application.Inventory;
using Qwader.Application.Purchasing;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class InventoryAndPurchasingTests(ApiFactory factory)
{
    private async Task<AppDbContext> DbAsync()
    {
        await Task.CompletedTask;
        return factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
    }

    [Fact]
    public async Task Weighted_average_follows_purchases_shrinkage_and_purchase_returns()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync();

        await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 10, 10)]);
        (await admin.GetProductAsync(product.Id)).AverageCost.Should().Be(10);

        var second = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 10, 20)]);
        (await admin.GetProductAsync(product.Id)).AverageCost.Should().Be(15);

        (await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 5)).EnsureSuccessStatusCode();
        var afterDamage = await admin.GetProductAsync(product.Id);
        afterDamage.Quantity.Should().Be(15);
        afterDamage.AverageCost.Should().Be(15, "shrinkage leaves at the average and does not change it");

        // Returning 5 units bought at 20: (15×15 − 5×20) / 10 = 12.5
        await (await admin.PostAsJsonAsync($"/api/purchases/{second.Id}/returns", new PurchaseReturnRequest(Guid.NewGuid(), null,
            ReturnSettlement.ReduceBalance, null, "تالف من المورد", [new ReturnLineInput(second.Lines[0].Id, 5)]))).ReadAsync<PurchaseDetailDto>();

        var final = await admin.GetProductAsync(product.Id);
        final.Quantity.Should().Be(10);
        final.AverageCost.Should().Be(12.5m);
        final.StockValue.Should().Be(125);
    }

    [Fact]
    public async Task Posting_a_purchase_allocates_the_discount_and_updates_supplier_and_account()
    {
        var admin = await factory.LoginAsync();
        var a = await admin.CreateProductAsync();
        var b = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(x => x.Name == "الصندوق النقدي");
        await admin.FundAsync(cash.Id, 100);
        var db = await DbAsync();
        var cashBefore = await db.FinancialAccounts.Where(x => x.Id == cash.Id).Select(x => x.CurrentBalance).FirstAsync();

        // Lines 300 + 100 = 400, discount 40 → total 360; each line carries 10% of the discount.
        var purchase = await admin.PurchaseAsync(supplier.Id,
            [new PurchaseLineInput(a.Id, 10, 30), new PurchaseLineInput(b.Id, 5, 20)], discount: 40, paid: 100, accountId: cash.Id);

        purchase.Total.Should().Be(360);
        purchase.Remaining.Should().Be(260);
        purchase.Lines.Single(l => l.ProductId == a.Id).NetUnitCost.Should().Be(27);
        purchase.Lines.Single(l => l.ProductId == b.Id).NetUnitCost.Should().Be(18);
        (await admin.GetProductAsync(a.Id)).AverageCost.Should().Be(27);

        var supplierAfter = await admin.GetFromJsonAsync<SupplierDto>($"/api/suppliers/{supplier.Id}", TestJson.Options);
        supplierAfter!.Balance.Should().Be(260);
        supplierAfter.TotalPurchases.Should().Be(360);
        supplierAfter.TotalPayments.Should().Be(100);

        db = await DbAsync();
        (await db.FinancialAccounts.Where(x => x.Id == cash.Id).Select(x => x.CurrentBalance).FirstAsync()).Should().Be(cashBefore - 100);
        (await db.AccountTransactions.CountAsync(t => t.SourceType == "PurchaseInvoice" && t.SourceId == purchase.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Issuing_more_than_available_is_refused_without_side_effects()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(openingQuantity: 3, openingCost: 5);

        var response = await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 4);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadFromJsonAsync<ProblemBody>(TestJson.Options))!.Title.Should().Contain("الكمية المتوفرة");
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(3);
        var db = await DbAsync();
        (await db.StockMovements.CountAsync(m => m.ProductId == product.Id)).Should().Be(1, "only the opening balance exists");
    }

    [Fact]
    public async Task Concurrent_issues_of_the_last_units_never_go_negative()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(openingQuantity: 3, openingCost: 5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => admin.AdjustAsync(product.Id, AdjustmentKind.Lost, 3)));

        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        responses.Where(r => !r.IsSuccessStatusCode).Should().AllSatisfy(r => ((int)r.StatusCode).Should().Be(422));
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(0);
    }

    [Fact]
    public async Task Repeating_a_request_with_the_same_key_applies_it_once()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(openingQuantity: 10, openingCost: 5);
        var key = Guid.NewGuid();

        var first = await (await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 2, key)).ReadAsync<StockAdjustmentResult>();
        var second = await (await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 2, key)).ReadAsync<StockAdjustmentResult>();

        second.Number.Should().Be(first.Number);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(8);
    }

    [Fact]
    public async Task Voiding_a_purchase_reverses_stock_supplier_and_payment()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync();
        var account = (await admin.LookupsAsync()).Accounts.First(x => x.Name == "بنك فلسطين");
        await admin.FundAsync(account.Id, 40);
        var db = await DbAsync();
        var before = await db.FinancialAccounts.Where(x => x.Id == account.Id).Select(x => x.CurrentBalance).FirstAsync();

        var purchase = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 4, 25)], paid: 40, accountId: account.Id);
        var voided = await (await admin.PostAsJsonAsync($"/api/purchases/{purchase.Id}/void", new VoidRequest("فاتورة مكررة"))).ReadAsync<PurchaseDetailDto>();

        voided.Status.Should().Be(PurchaseStatus.Voided);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(0);
        (await admin.GetFromJsonAsync<SupplierDto>($"/api/suppliers/{supplier.Id}", TestJson.Options))!.Balance.Should().Be(0);
        db = await DbAsync();
        (await db.FinancialAccounts.Where(x => x.Id == account.Id).Select(x => x.CurrentBalance).FirstAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Purchase_cannot_be_voided_once_its_goods_are_gone()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync();
        var purchase = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 4, 25)]);
        (await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 2)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/purchases/{purchase.Id}/void", new VoidRequest("خطأ"));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Draft_purchase_has_no_effect_until_posted()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync();

        var draft = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 6, 10)], post: false);
        draft.Status.Should().Be(PurchaseStatus.Draft);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(0);

        var posted = await (await admin.PostAsJsonAsync($"/api/purchases/{draft.Id}/post", new PostPurchaseRequest(Guid.NewGuid()))).ReadAsync<PurchaseDetailDto>();
        posted.Status.Should().Be(PurchaseStatus.Posted);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(6);
    }

    [Fact]
    public async Task Returning_more_than_purchased_is_refused()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync();
        var purchase = await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 3, 10)]);

        var response = await admin.PostAsJsonAsync($"/api/purchases/{purchase.Id}/returns", new PurchaseReturnRequest(Guid.NewGuid(), null,
            ReturnSettlement.ReduceBalance, null, null, [new ReturnLineInput(purchase.Lines[0].Id, 4)]));

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Supplier_statement_runs_the_balance_line_by_line()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();
        var supplier = await admin.CreateSupplierAsync(openingBalance: 50);
        var cash = (await admin.LookupsAsync()).Accounts.First(x => x.Name == "الصندوق النقدي");
        await admin.FundAsync(cash.Id, 30);
        await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 10, 10)], paid: 30, accountId: cash.Id);

        var statement = await admin.GetFromJsonAsync<StatementDto>($"/api/suppliers/{supplier.Id}/statement", TestJson.Options);

        statement!.Lines.Select(l => l.Balance).Should().Equal(50, 150, 120);
        statement.ClosingBalance.Should().Be(120);
    }

    [Fact]
    public async Task Stock_count_posts_the_difference_as_adjustments()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(openingQuantity: 10, openingCost: 4);

        var count = await (await admin.PostAsJsonAsync("/api/inventory/counts", new CreateStockCountRequest(null, null, [product.Id], null))).ReadAsync<StockCountDto>();
        await (await admin.PutAsJsonAsync($"/api/inventory/counts/{count.Id}", new SaveCountsRequest([new CountEntry(count.Lines[0].Id, 7)]))).ReadAsync<StockCountDto>();
        var posted = await (await admin.PostAsync($"/api/inventory/counts/{count.Id}/post", null)).ReadAsync<StockCountDto>();

        posted.Status.Should().Be(StockCountStatus.Posted);
        posted.Lines[0].Difference.Should().Be(-3);
        (await admin.GetProductAsync(product.Id)).Quantity.Should().Be(7);
        var db = await DbAsync();
        (await db.StockMovements.AnyAsync(m => m.ProductId == product.Id && m.Type == StockMovementType.CountDecrease && m.Quantity == -3)).Should().BeTrue();
    }
}
