using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Reports;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;
using Xunit.Abstractions;

namespace Qwader.IntegrationTests;

/// <summary>
/// «الميزانية العمومية»: every operation moves the right lines, and assets − liabilities − equity does not move
/// (the sheet stays balanced; nothing is forced to balance).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BalanceSheetTests(ApiFactory factory, ITestOutputHelper output)
{
    private static decimal Line(BalanceSheetDto b, string name) =>
        b.Assets.Concat(b.Liabilities).Concat(b.Equity).SelectMany(s => s.Lines).Where(l => l.Name.StartsWith(name)).Sum(l => l.Amount);

    private static decimal Account(BalanceSheetDto b, string name) =>
        b.Assets.SelectMany(s => s.Lines).Single(l => l.Name == name).Amount;

    [Fact]
    public async Task Each_operation_moves_its_lines_and_the_sheet_stays_balanced()
    {
        var admin = await factory.LoginAsync();
        async Task<BalanceSheetDto> Sheet() => await (await admin.GetAsync("/api/reports/balance-sheet")).ReadAsync<BalanceSheetDto>();

        var cashName = TestData.Unique("صندوق ميزانية");
        var cash = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(cashName, FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        var supplier = await admin.CreateSupplierAsync();
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(TestData.Unique("عميل ميزانية"), null, null, null))).ReadAsync<CustomerDto>();
        var product = await admin.CreateProductAsync(retail: 25);

        var before = await Sheet();
        output.WriteLine($"difference before: {before.Difference}");
        var difference = before.Difference;
        async Task<BalanceSheetDto> Step(string what, Func<Task> act)
        {
            var was = await Sheet();
            await act();
            var now = await Sheet();
            now.Difference.Should().Be(difference, $"«{what}» keeps the sheet balanced");
            (now.TotalAssets - now.TotalLiabilities).Should().Be(now.TotalEquity + now.Difference);
            output.WriteLine($"{what}: assets {was.TotalAssets} → {now.TotalAssets}, equity {was.TotalEquity} → {now.TotalEquity}");
            return was;
        }

        // Capital added: cash +1000, capital +1000.
        var was = await Step("إضافة رأس مال", async () => (await admin.PostAsJsonAsync("/api/receipt-vouchers",
            new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.CapitalContribution, null, "صاحب المال", null, 1000, cash, null, null, null))).EnsureSuccessStatusCode());
        var now = await Sheet();
        (Account(now, cashName), Line(now, "رأس مال مضاف") - Line(was, "رأس مال مضاف")).Should().Be((1000m, 1000m));

        // Bought for cash: 5 × 10 — stock +50, cash −50, equity unchanged.
        was = await Step("شراء نقدي", () => admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 5, 10)], paid: 50, accountId: cash));
        now = await Sheet();
        (Line(now, "المخزون") - Line(was, "المخزون"), Account(now, cashName), now.TotalEquity - was.TotalEquity).Should().Be((50m, 950m, 0m));

        // Bought on credit: 3 × 10 — stock +30, owed to suppliers +30.
        was = await Step("شراء آجل", () => admin.PurchaseAsync(supplier.Id, [new PurchaseLineInput(product.Id, 3, 10)]));
        now = await Sheet();
        (Line(now, "المخزون") - Line(was, "المخزون"), Line(now, "حسابات الموردين") - Line(was, "حسابات الموردين")).Should().Be((30m, 30m));

        // Sold for cash: 2 × 25 — cash +50, stock −20, profit +30.
        was = await Step("بيع نقدي", async () => (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 2, 25)], [new(cash, 50)], null))).EnsureSuccessStatusCode());
        now = await Sheet();
        (Account(now, cashName), Line(now, "المخزون") - Line(was, "المخزون"), Line(now, "صافي ربح") - Line(was, "صافي ربح")).Should().Be((1000m, -20m, 30m));

        // Sold on credit: 1 × 25 — owed by customers +25, stock −10, profit +15.
        was = await Step("بيع آجل", async () => (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, customer.Id, null, null, 0,
            [new(product.Id, 1, 25)], [], null))).EnsureSuccessStatusCode());
        now = await Sheet();
        (Line(now, "حسابات العملاء") - Line(was, "حسابات العملاء"), Line(now, "صافي ربح") - Line(was, "صافي ربح")).Should().Be((25m, 15m));

        // Collected from the customer: cash +25, owed by customers −25.
        was = await Step("تحصيل دين", async () => (await admin.PostAsJsonAsync("/api/receipt-vouchers",
            new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, customer.Id, null, null, 25, cash, null, null, null))).EnsureSuccessStatusCode());
        now = await Sheet();
        (Account(now, cashName), Line(now, "حسابات العملاء") - Line(was, "حسابات العملاء")).Should().Be((1025m, -25m));

        // Paid the supplier: cash −30, owed to suppliers −30.
        was = await Step("تسديد مورد", async () => (await admin.PostAsJsonAsync("/api/payment-vouchers",
            new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null, 30, cash, null, null, null))).EnsureSuccessStatusCode());
        now = await Sheet();
        (Account(now, cashName), Line(now, "حسابات الموردين") - Line(was, "حسابات الموردين")).Should().Be((995m, -30m));

        // An expense: cash −15, profit −15.
        var category = (await admin.LookupsAsync()).ExpenseCategories.First(c => c.Name != "الرواتب").Id;
        was = await Step("مصروف", async () => (await admin.PostAsJsonAsync("/api/expenses",
            new CreateExpenseRequest(Guid.NewGuid(), category, "اختبار ميزانية", 15, cash, null, null))).EnsureSuccessStatusCode());
        now = await Sheet();
        (Account(now, cashName), Line(now, "صافي ربح") - Line(was, "صافي ربح")).Should().Be((980m, -15m));

        // The owner takes 100: cash −100, withdrawals −100 (not an expense: profit unchanged).
        was = await Step("مسحوبات شخصية", async () => (await admin.PostAsJsonAsync("/api/payment-vouchers",
            new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.OwnerWithdrawal, null, "صاحب المال", 100, cash, null, null, null))).EnsureSuccessStatusCode());
        now = await Sheet();
        (Account(now, cashName), Line(now, "المسحوبات الشخصية") - Line(was, "المسحوبات الشخصية"), Line(now, "صافي ربح") - Line(was, "صافي ربح"))
            .Should().Be((880m, -100m, 0m));

        // «مصاريف صاحب العمل» lists it (and only the owner's), with the period's total.
        var owner = await (await admin.GetAsync("/api/payment-vouchers?purpose=OwnerWithdrawal&pageSize=100")).ReadAsync<Application.Common.PagedResult<VoucherDto>>();
        owner.Items.Should().Contain(v => v.Amount == 100 && v.FinancialAccountId == cash).And.OnlyContain(v => v.Purpose == "OwnerWithdrawal");
        (await (await admin.GetAsync("/api/payment-vouchers/owner-total")).ReadAsync<OwnerExpensesTotalDto>()).Total.Should().BeGreaterThanOrEqualTo(100);

        (await admin.GetAsync($"/api/reports/balance-sheet?date={DateTime.UtcNow.AddDays(3):yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
