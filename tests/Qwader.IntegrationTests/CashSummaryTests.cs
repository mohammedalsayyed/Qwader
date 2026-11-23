using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Application.Settings;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>«حركة الفلوس» on the home screen: received − suppliers − store expenses = store net; − owners' withdrawals = what is left.</summary>
[Collection(ApiCollection.Name)]
public sealed class CashSummaryTests(ApiFactory factory)
{
    [Fact]
    public async Task Money_in_and_out_with_the_owners_withdrawals_apart()
    {
        var admin = await factory.LoginAsync();
        var settings = await (await admin.GetAsync("/api/settings")).ReadAsync<SettingsDto>();
        (await admin.PutAsJsonAsync("/api/settings", settings with { SaleTransfersPending = false })).EnsureSuccessStatusCode();
        try
        {
            async Task<CashSummaryDto> Get() => await (await admin.GetAsync("/api/reports/cash-summary")).ReadAsync<CashSummaryDto>();
            var before = await Get();

            var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
            await admin.FundAsync(cash, 1000);   // an opening balance: not money in
            var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("عميل فلوس"), null, null, null))).ReadAsync<CustomerDto>();
            var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 10, openingCost: 30);
            Task<HttpResponseMessage> Sell(int? customerId, SalePaymentInput[] payments) => admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(),
                PriceTier.Retail, customerId, null, null, 0, [new(product.Id, 1, 50)], payments, null));
            (await Sell(null, [new(cash, 50)])).EnsureSuccessStatusCode();         // 50 in
            (await Sell(customer.Id, [])).EnsureSuccessStatusCode();               // credit: nothing in yet
            (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id,
                null, null, 20, cash, null, null, null))).EnsureSuccessStatusCode();   // 20 collected
            var voided = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection,
                customer.Id, null, null, 10, cash, null, null, null))).ReadAsync<VoucherDto>();
            (await admin.PostAsJsonAsync($"/api/receipt-vouchers/{voided.Id}/void", new VoidDocumentRequest("خطأ"))).EnsureSuccessStatusCode();

            var supplier = await admin.CreateSupplierAsync(openingBalance: 100);
            Task<HttpResponseMessage> Pay(PaymentPurpose purpose, decimal amount, int? supplierId = null, string? name = null) => admin.PostAsJsonAsync("/api/payment-vouchers",
                new CreatePaymentRequest(Guid.NewGuid(), purpose, supplierId, name, amount, cash, null, null, null));
            (await Pay(PaymentPurpose.SupplierPayment, 25, supplier.Id)).EnsureSuccessStatusCode();
            (await Pay(PaymentPurpose.Other, 5, name: "صيانة")).EnsureSuccessStatusCode();
            (await Pay(PaymentPurpose.OwnerWithdrawal, 15, name: "صاحب المحل")).EnsureSuccessStatusCode();

            var after = await Get();
            (after.Received - before.Received).Should().Be(70);
            (after.SupplierPayments - before.SupplierPayments).Should().Be(25);
            (after.OtherPayments - before.OtherPayments).Should().Be(5);
            (after.StoreNet - before.StoreNet).Should().Be(40);
            (after.OwnerWithdrawals - before.OwnerWithdrawals).Should().Be(15);
            (after.Remaining - before.Remaining).Should().Be(25);
            after.Remaining.Should().Be(after.StoreNet - after.OwnerWithdrawals);
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Only_the_administrator_has_it_unless_ticked()
    {
        var admin = await factory.LoginAsync();
        var roles = await (await admin.GetAsync("/api/roles")).ReadAsync<List<RoleDto>>();
        roles.Single(r => r.Name == SystemRoles.Admin).Permissions.Should().Contain(Permissions.DashboardCashFlow);
        roles.Single(r => r.Name == SystemRoles.Manager).Permissions.Should().NotContain(Permissions.DashboardCashFlow);

        async Task<HttpClient> UserWith(params string[] permissions)
        {
            var role = await (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest(TestData.Unique("دور"), null, permissions))).ReadAsync<RoleDto>();
            var userName = TestData.Unique("u");
            (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "موظف", null, "Temp12345", [role.Name]))).EnsureSuccessStatusCode();
            var temp = await factory.LoginAsync(userName, "Temp12345");
            (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "User123456"))).EnsureSuccessStatusCode();
            return await factory.LoginAsync(userName, "User123456");
        }
        var reports = await UserWith(Permissions.ReportsFinancial, Permissions.ReportsProfit, Permissions.ProfitView);
        (await reports.GetAsync("/api/reports/cash-summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ticked = await UserWith(Permissions.DashboardCashFlow);
        (await ticked.GetAsync("/api/reports/cash-summary")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
