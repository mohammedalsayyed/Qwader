using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Finance;
using Qwader.Application.Settings;
using Qwader.Domain.Finance;

namespace Qwader.IntegrationTests;

/// <summary>«مصاريف صاحب العمل» per owner: the owners come from the settings, each expense is one of theirs, totals per owner.</summary>
[Collection(ApiCollection.Name)]
public sealed class OwnerExpensesTests(ApiFactory factory)
{
    [Fact]
    public async Task Each_owner_has_their_expenses_and_totals()
    {
        var admin = await factory.LoginAsync();
        var cash = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("صندوق ملاك"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        await admin.FundAsync(cash, 1000);
        var a = TestData.Unique("معتصم");
        var b = TestData.Unique("هيثم");
        try
        {
            (await (await admin.PutAsJsonAsync("/api/settings/owners", new SetOwnersRequest([a, b, a, " "]))).ReadAsync<List<string>>())
                .Should().Equal(a, b);
            (await (await admin.GetAsync("/api/settings")).ReadAsync<SettingsDto>()).Owners.Should().Equal(a, b);

            Task<HttpResponseMessage> Pay(string name, decimal amount) => admin.PostAsJsonAsync("/api/payment-vouchers",
                new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.OwnerWithdrawal, null, name, amount, cash, null, "مصروف", null));
            (await Pay(a, 100)).EnsureSuccessStatusCode();
            (await Pay(a, 50)).EnsureSuccessStatusCode();
            (await Pay(b, 30)).EnsureSuccessStatusCode();
            // A name not in the list is refused.
            (await Pay("شخص آخر", 10)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var all = await (await admin.GetAsync("/api/payment-vouchers/owner-total")).ReadAsync<OwnerExpensesTotalDto>();
            all.ByOwner!.Take(2).Should().BeEquivalentTo([new OwnerTotalRow(a, 2, 150), new OwnerTotalRow(b, 1, 30)], o => o.WithStrictOrdering());
            var one = await (await admin.GetAsync($"/api/payment-vouchers/owner-total?owner={Uri.EscapeDataString(a)}")).ReadAsync<OwnerExpensesTotalDto>();
            (one.Count, one.Total).Should().Be((2, 150m));
            var list = await (await admin.GetAsync($"/api/payment-vouchers?purpose=OwnerWithdrawal&beneficiary={Uri.EscapeDataString(b)}"))
                .ReadAsync<Application.Common.PagedResult<VoucherDto>>();
            list.Items.Should().ContainSingle().Which.Amount.Should().Be(30);

            // Saving the other settings keeps the owners; the payment details printed on invoices are saved with them.
            var settings = await (await admin.GetAsync("/api/settings")).ReadAsync<SettingsDto>();
            (await admin.PutAsJsonAsync("/api/settings", settings with { Owners = null, PaymentHolder = " معتصم قويدر ", PaymentAccount = "123456", PaymentPhone = "0599000000" }))
                .EnsureSuccessStatusCode();
            var saved = await (await admin.GetAsync("/api/settings")).ReadAsync<SettingsDto>();
            saved.Owners.Should().Equal(a, b);
            (saved.PaymentHolder, saved.PaymentAccount, saved.PaymentPhone, saved.PaymentBank).Should().Be(("معتصم قويدر", "123456", "0599000000", (string?)null));
            saved.ProfitsLocked.Should().BeTrue("profits are hidden by default");
            (await admin.PutAsJsonAsync("/api/settings", saved with { ProfitsLocked = false })).EnsureSuccessStatusCode();
            (await (await admin.GetAsync("/api/settings")).ReadAsync<SettingsDto>()).ProfitsLocked.Should().BeFalse();
            (await admin.PutAsJsonAsync("/api/settings", saved with { PaymentHolder = null, PaymentAccount = null, PaymentPhone = null, ProfitsLocked = true }))
                .EnsureSuccessStatusCode();
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/settings/owners", new SetOwnersRequest([]))).EnsureSuccessStatusCode();
        }
    }
}
