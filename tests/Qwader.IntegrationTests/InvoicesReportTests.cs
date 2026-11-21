using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>«تقرير فواتير المبيعات»: each invoice — paid, where to, still owed, and its profit only with «عرض الأرباح».</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoicesReportTests(ApiFactory factory)
{
    [Fact]
    public async Task Each_invoice_shows_where_its_money_went_and_what_is_owed()
    {
        var admin = await factory.LoginAsync();
        var settings = await (await admin.GetAsync("/api/settings")).ReadAsync<Application.Settings.SettingsDto>();
        (await admin.PutAsJsonAsync("/api/settings", settings with { SaleTransfersPending = false })).EnsureSuccessStatusCode();
        try
        {
            var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash);
            var walletName = TestData.Unique("محفظة تقرير");
            var wallet = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
                new SaveAccountRequest(walletName, FinancialAccountType.Wallet, null, null, null))).ReadAsync<AccountDto>()).Id;
            var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("عميل تقرير"), null, null, null))).ReadAsync<CustomerDto>();
            var product = await admin.CreateProductAsync(retail: 50, openingQuantity: 10, openingCost: 30);

            Task<SaleDetailDto> Sell(int quantity, int? customerId, SalePaymentInput[] payments) =>
                admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customerId, null, null, 0,
                    [new(product.Id, quantity, 50)], payments, null)).ContinueWith(t => t.Result.ReadAsync<SaleDetailDto>()).Unwrap();

            var split = await Sell(1, null, [new(cash.Id, 30), new(wallet, 20)]);     // 50: cash + wallet, paid
            var partly = await Sell(2, customer.Id, [new(cash.Id, 40)]);             // 100: 40 now, 30 collected later → 30 owed
            var credit = await Sell(1, customer.Id, []);                               // 50: nothing paid
            (await admin.PostAsJsonAsync("/api/receipt-vouchers",
                new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id, null, null, 30, wallet, null, null, null))).EnsureSuccessStatusCode();

            var report = await (await admin.GetAsync("/api/reports/invoices")).ReadAsync<InvoiceReportDto>();
            InvoiceReportRow Row(int id) => report.Rows.Single(r => r.Id == id);

            var a = Row(split.Id);
            (a.Total, a.Paid, a.Remaining, a.Status, a.Profit).Should().Be((50m, 50m, 0m, "مدفوعة", (decimal?)20m));
            a.Payments.Should().BeEquivalentTo([new InvoicePaymentPart(cash.Name, 30), new InvoicePaymentPart(walletName, 20)]);

            var b = Row(partly.Id);
            (b.Total, b.Paid, b.Remaining, b.Status, b.Profit).Should().Be((100m, 70m, 30m, "جزئي", (decimal?)40m));
            b.Payments.Should().BeEquivalentTo([new InvoicePaymentPart(cash.Name, 40), new InvoicePaymentPart($"{walletName} (تحصيل لاحق)", 30)]);

            var c = Row(credit.Id);
            (c.Paid, c.Remaining, c.Status).Should().Be((0m, 50m, "آجل"));
            c.Payments.Should().BeEmpty();

            report.Count.Should().Be(report.Rows.Count);
            report.Remaining.Should().Be(report.Rows.Sum(r => r.Remaining));
            report.ByAccount.Single(p => p.Account == walletName).Amount.Should().Be(20);

            // Without «عرض الأرباح»: the same invoices, no profit.
            var role = await (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest(TestData.Unique("دور"), null, [Permissions.ReportsSales]))).ReadAsync<RoleDto>();
            var userName = TestData.Unique("u");
            (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "موظف", null, "Temp12345", [role.Name]))).EnsureSuccessStatusCode();
            var temp = await factory.LoginAsync(userName, "Temp12345");
            (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "User123456"))).EnsureSuccessStatusCode();
            var employee = await factory.LoginAsync(userName, "User123456");
            var plain = await (await employee.GetAsync("/api/reports/invoices")).ReadAsync<InvoiceReportDto>();
            plain.Profit.Should().BeNull();
            plain.Rows.Should().OnlyContain(r => r.Profit == null);
            plain.Rows.Single(r => r.Id == partly.Id).Remaining.Should().Be(30);

            (await admin.GetAsync("/api/reports/invoices/export")).EnsureSuccessStatusCode();
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
        }
    }
}
