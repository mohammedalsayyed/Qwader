using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Domain.Finance;

namespace Qwader.IntegrationTests;

/// <summary>A purchase paid from more than one account (part cash, part bank): each account pays its part, and a void or an edit gives each part back.</summary>
[Collection(ApiCollection.Name)]
public sealed class PurchaseSplitPaymentTests(ApiFactory factory)
{
    [Fact]
    public async Task Each_account_pays_its_part_and_gets_it_back_on_void_and_edit()
    {
        var admin = await factory.LoginAsync();
        async Task<int> NewAccount(string name, FinancialAccountType type) =>
            (await (await admin.PostAsJsonAsync("/api/financial-accounts",
                new SaveAccountRequest(TestData.Unique(name), type, null, null, null))).ReadAsync<AccountDto>()).Id;
        async Task<decimal> Balance(int id) => (await (await admin.GetAsync($"/api/financial-accounts/{id}")).ReadAsync<AccountDto>()).CurrentBalance;
        async Task<decimal> Owed(int id) => (await (await admin.GetAsync($"/api/suppliers/{id}")).ReadAsync<SupplierDto>()).Balance;

        var cash = await NewAccount("صندوق شراء", FinancialAccountType.Cash);
        var bank = await NewAccount("بنك شراء", FinancialAccountType.Bank);
        await admin.FundAsync(cash, 500);
        await admin.FundAsync(bank, 500);
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync();
        PurchaseLineInput[] lines = [new(product.Id, 10, 30)]; // 300

        SavePurchaseRequest Request(params PurchasePaymentInput[] payments) =>
            new(Guid.NewGuid(), supplier.Id, null, null, null, 0, 0, null, null, lines, true, payments);

        // 100 cash + 150 bank of 300: 50 left owed.
        var invoice = await (await admin.PostAsJsonAsync("/api/purchases", Request(new(cash, 100), new(bank, 150)))).ReadAsync<PurchaseDetailDto>();
        invoice.PaidAmount.Should().Be(250);
        invoice.Remaining.Should().Be(50);
        invoice.Payments.Should().BeEquivalentTo([new { AccountId = cash, Amount = 100m }, new { AccountId = bank, Amount = 150m }], o => o.ExcludingMissingMembers());
        (await Balance(cash)).Should().Be(400);
        (await Balance(bank)).Should().Be(350);
        (await Owed(supplier.Id)).Should().Be(50);

        // Edit: now 200 cash + 100 bank (paid in full) — the old parts come back, the new ones go out.
        var edited = await (await admin.PostAsJsonAsync($"/api/purchases/{invoice.Id}/replace", Request(new(cash, 200), new(bank, 100)))).ReadAsync<PurchaseDetailDto>();
        edited.Remaining.Should().Be(0);
        (await Balance(cash)).Should().Be(300);
        (await Balance(bank)).Should().Be(400);
        (await Owed(supplier.Id)).Should().Be(0);

        // Void: every part back to its account.
        (await admin.PostAsJsonAsync($"/api/purchases/{edited.Id}/void", new VoidRequest("اختبار"))).EnsureSuccessStatusCode();
        (await Balance(cash)).Should().Be(500);
        (await Balance(bank)).Should().Be(500);
        (await Owed(supplier.Id)).Should().Be(0);

        // More than the invoice, or more than an account holds, is refused and moves nothing.
        (await admin.PostAsJsonAsync("/api/purchases", Request(new(cash, 200), new(bank, 200)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/purchases", Request(new(cash, 50), new(bank, 250)) with { Lines = [new(product.Id, 100, 30)] }))
            .IsSuccessStatusCode.Should().BeTrue("3000 owed, 300 paid within the funds");
        (await admin.PostAsJsonAsync("/api/purchases", Request(new PurchasePaymentInput(cash, 1000)) with { Lines = [new(product.Id, 100, 30)] }))
            .IsSuccessStatusCode.Should().BeFalse("the cash box holds 450");
        (await Balance(cash)).Should().Be(450);
        (await Balance(bank)).Should().Be(250);
    }

    [Fact]
    public async Task The_old_single_account_request_still_works()
    {
        var admin = await factory.LoginAsync();
        var cash = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("صندوق قديم"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        await admin.FundAsync(cash, 100);
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync();

        var invoice = await admin.PurchaseAsync(supplier.Id, [new(product.Id, 2, 30)], paid: 60, accountId: cash);
        invoice.PaymentAccountId.Should().Be(cash);
        invoice.Payments.Should().ContainSingle().Which.Amount.Should().Be(60);
        (await (await admin.PostAsJsonAsync($"/api/purchases/{invoice.Id}/void", new VoidRequest("اختبار"))).ReadAsync<PurchaseDetailDto>()).Status
            .Should().Be(Domain.Purchasing.PurchaseStatus.Voided);
        (await (await admin.GetAsync($"/api/financial-accounts/{cash}")).ReadAsync<AccountDto>()).CurrentBalance.Should().Be(100);
    }
}
