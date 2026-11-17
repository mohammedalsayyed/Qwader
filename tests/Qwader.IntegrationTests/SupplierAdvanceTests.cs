using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Reports;
using Qwader.Domain.Finance;

namespace Qwader.IntegrationTests;

/// <summary>
/// Money paid to a supplier ahead of the goods, after the system started: it leaves the account (on its day) and stays «لنا عنده»
/// until the goods come; the balance sheet stays balanced.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupplierAdvanceTests(ApiFactory factory)
{
    [Fact]
    public async Task An_advance_leaves_the_account_and_stays_with_the_supplier_until_the_goods_come()
    {
        var admin = await factory.LoginAsync();
        var cash = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("صندوق مقدم"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        await admin.FundAsync(cash, 1000);
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync();
        async Task<decimal> Cash() => (await (await admin.GetAsync($"/api/financial-accounts/{cash}")).ReadAsync<AccountDto>()).CurrentBalance;
        async Task<decimal> Owed() => (await (await admin.GetAsync($"/api/suppliers/{supplier.Id}")).ReadAsync<SupplierDto>()).Balance;
        async Task<decimal> Difference() => (await (await admin.GetAsync("/api/reports/balance-sheet")).ReadAsync<BalanceSheetDto>()).Difference;
        var difference = await Difference();

        // Without «advance» nothing is owed, so a payment is refused.
        (await admin.PostAsJsonAsync("/api/payment-vouchers",
            new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null, 300, cash, null, null, null)))
            .StatusCode.Should().NotBe(HttpStatusCode.OK);

        // Paid yesterday, ahead of the goods.
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var voucher = await (await admin.PostAsJsonAsync("/api/payment-vouchers",
            new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null, 300, cash, null, null, null, Date: yesterday, Advance: true)))
            .ReadAsync<VoucherDto>();
        (await Cash()).Should().Be(700);
        (await Owed()).Should().Be(-300, "لنا عنده 300");
        (await Difference()).Should().Be(difference);

        // The goods come (100): 200 stays with them.
        await admin.PurchaseAsync(supplier.Id, [new(product.Id, 10, 10)]);
        (await Owed()).Should().Be(-200);
        (await Difference()).Should().Be(difference);

        // Voiding the advance gives the money back to the account; then the store owes the 100.
        (await admin.PostAsJsonAsync($"/api/payment-vouchers/{voucher.Id}/void", new { reason = "اختبار" })).EnsureSuccessStatusCode();
        (await Cash()).Should().Be(1000);
        (await Owed()).Should().Be(100);

        // «advance» is for a supplier payment only.
        (await admin.PostAsJsonAsync("/api/payment-vouchers",
            new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.Other, null, "أحد", 10, cash, null, null, null, Advance: true)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
