using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Catalog;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>«عرض الأرباح» shows every profit; «عرض التكلفة» every cost — each on its own.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProfitCostPermissionTests(ApiFactory factory)
{
    private async Task<HttpClient> UserWith(HttpClient admin, params string[] permissions)
    {
        var role = await (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest(TestData.Unique("دور"), null, permissions))).ReadAsync<RoleDto>();
        var userName = TestData.Unique("u");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "موظف", null, "Temp12345", [role.Name]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "User123456"))).EnsureSuccessStatusCode();
        return await factory.LoginAsync(userName, "User123456");
    }

    [Fact]
    public async Task Cost_and_profit_each_need_their_own_permission()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 30, openingQuantity: 5, openingCost: 10);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 30)], [new(cash, 30)], null))).ReadAsync<SaleDetailDto>();
        string[] basics = [Permissions.ProductsView, Permissions.SalesViewAll, Permissions.SalesCreate, Permissions.ReportsSales, Permissions.ReportsFinancial];

        // Cost, no profit: costs show, profits do not; no balance sheet.
        var costOnly = await UserWith(admin, [.. basics, Permissions.ProductsViewCost]);
        var s1 = await (await costOnly.GetAsync($"/api/sales/{sale.Id}")).ReadAsync<SaleDetailDto>();
        (s1.TotalCost, s1.Profit).Should().Be((10m, (decimal?)null));
        var p1 = await (await costOnly.GetAsync($"/api/products/{product.Id}")).ReadAsync<ProductDetailDto>();
        (p1.AverageCost, p1.ExpectedProfit).Should().Be((10m, (decimal?)null));
        var r1 = await (await costOnly.GetAsync("/api/reports/period")).ReadAsync<PeriodReportDto>();
        r1.CostOfSales.Should().NotBeNull();
        (r1.GrossProfit, r1.NetOperatingProfit).Should().Be(((decimal?)null, (decimal?)null));
        (await costOnly.GetAsync("/api/reports/balance-sheet")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Profit, no cost: profits show, costs do not.
        var profitOnly = await UserWith(admin, [.. basics, Permissions.ProfitView]);
        var s2 = await (await profitOnly.GetAsync($"/api/sales/{sale.Id}")).ReadAsync<SaleDetailDto>();
        (s2.TotalCost, s2.Profit).Should().Be(((decimal?)null, 20m));
        s2.Lines.Should().OnlyContain(l => l.UnitCost == null);
        var r2 = await (await profitOnly.GetAsync("/api/reports/period")).ReadAsync<PeriodReportDto>();
        (r2.CostOfSales, r2.InventoryValue).Should().Be(((decimal?)null, (decimal?)null));
        r2.GrossProfit.Should().NotBeNull();

        // The administrator has both, and the store manager role does not get «عرض الأرباح» by itself.
        var roles = await (await admin.GetAsync("/api/roles")).ReadAsync<List<RoleDto>>();
        roles.Single(r => r.Name == SystemRoles.Admin).Permissions.Should().Contain(Permissions.ProfitView);
        roles.Single(r => r.Name == SystemRoles.Manager).Permissions.Should().NotContain(Permissions.ProfitView);
    }
}
