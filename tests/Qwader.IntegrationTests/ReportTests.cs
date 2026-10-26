using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Finance;
using Qwader.Application.Inventory;
using Qwader.Application.Purchasing;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>
/// Tests in the shared collection run one after another, so the change in today's report across a known
/// set of operations is exactly the effect of those operations.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReportTests(ApiFactory factory)
{
    [Fact]
    public async Task Profit_and_loss_follows_the_accounting_rules()
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Name == "الصندوق النقدي").Id;
        var product = await admin.CreateProductAsync(retail: 20);
        var supplier = await admin.CreateSupplierAsync();
        await admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 10, 10)]);
        await admin.FundAsync(cash, 100);
        var before = await admin.GetFromJsonAsync<PeriodReportDto>("/api/reports/period", TestJson.Options);

        // Sale: 3 × 20 = 60, invoice discount 6 → 54 net; cost 3 × 10 = 30.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 6,
            [new(product.Id, 3, 20)], [new(cash, 54)], null))).ReadAsync<SaleDetailDto>();
        // Return 1 sellable unit: revenue back 18 (its share after the discount), cost back 10.
        await (await admin.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new SaleReturnRequest(Guid.NewGuid(), ReturnSettlement.Refund, cash, null,
            [new(sale.Lines[0].Id, 1)]))).ReadAsync<SaleDetailDto>();
        // One unit broken in the store: a 10 ₪ inventory loss.
        (await admin.AdjustAsync(product.Id, AdjustmentKind.Damaged, 1)).EnsureSuccessStatusCode();
        // An operating expense of 5.
        var category = (await admin.LookupsAsync()).ExpenseCategories.First().Id;
        await (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "اختبار تقرير", 5, cash, null, null))).ReadAsync<ExpenseDto>();
        // Paying a supplier and collecting a debt are NOT profit or loss.
        await (await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null, 20, cash, null, null, null)))
            .ReadAsync<VoucherDto>();

        var after = await admin.GetFromJsonAsync<PeriodReportDto>("/api/reports/period", TestJson.Options);

        (after!.InvoiceCount - before!.InvoiceCount).Should().Be(1);
        (after.GrossSales - before.GrossSales).Should().Be(60);
        (after.Discounts - before.Discounts).Should().Be(6);
        (after.Returns - before.Returns).Should().Be(18);
        (after.NetSales - before.NetSales).Should().Be(36);
        (after.CostOfSales - before.CostOfSales).Should().Be(20);
        (after.GrossProfit - before.GrossProfit).Should().Be(16);
        (after.InventoryLosses - before.InventoryLosses).Should().Be(10);
        (after.Expenses - before.Expenses).Should().Be(5);
        (after.NetOperatingProfit - before.NetOperatingProfit).Should().Be(1, "16 gross profit − 10 shrinkage − 5 expenses");
        // Cash: +54 sale, −18 refund, −5 expense, −20 supplier payment = +11 (opening funding excluded).
        (after.NetCashFlow - before.NetCashFlow).Should().Be(11);

        var products = await admin.GetFromJsonAsync<List<ProductReportRow>>("/api/reports/products?take=500", TestJson.Options);
        var row = products!.Single(p => p.ProductId == product.Id);
        row.Quantity.Should().Be(2);
        row.Revenue.Should().Be(36);
        row.Profit.Should().Be(16);
    }

    [Fact]
    public async Task Daily_series_covers_every_day_of_the_range()
    {
        var admin = await factory.LoginAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var days = await admin.GetFromJsonAsync<List<DailyRowDto>>($"/api/reports/daily?from={today.AddDays(-6):yyyy-MM-dd}&to={today:yyyy-MM-dd}", TestJson.Options);
        days!.Should().HaveCount(7);
    }

    [Fact]
    public async Task Report_figures_respect_report_permissions()
    {
        var admin = await factory.LoginAsync();
        async Task<HttpClient> UserWith(string role)
        {
            var userName = TestData.Unique("rep");
            (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "مستخدم تقارير", null, "Temp12345", [role]))).EnsureSuccessStatusCode();
            var temp = await factory.LoginAsync(userName, "Temp12345");
            (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Report12345"))).EnsureSuccessStatusCode();
            return await factory.LoginAsync(userName, "Report12345");
        }

        var seller = await UserWith(SystemRoles.Sales);
        (await seller.GetAsync("/api/reports/period")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var viewer = await UserWith(SystemRoles.Viewer);
        var seen = await viewer.GetFromJsonAsync<PeriodReportDto>("/api/reports/period", TestJson.Options);
        seen!.CostOfSales.Should().BeNull();
        seen.GrossProfit.Should().BeNull();
        seen.CashIn.Should().BeNull();
        (await viewer.GetAsync("/api/reports/receivables")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var export = await admin.GetAsync("/api/reports/summary/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Contain("spreadsheetml");
    }
}
