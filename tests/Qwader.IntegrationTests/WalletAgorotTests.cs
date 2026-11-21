using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Finance;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>
/// Production, 2026-11-21: a walk-in sale refused as «paid less than the total». The till added its lines up before rounding
/// (two lines of 0.505 → 1.01) while the server rounds each line (0.51 + 0.51 = 1.02): the till's total was an agora under the
/// server's. Those agorot are rounding: the sale goes through at the total the till showed and was paid.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WalletAgorotTests(ApiFactory factory)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_till_total_an_agora_under_the_servers_is_paid_in_full(bool byWallet)
    {
        var admin = await factory.LoginAsync();
        var settings = await (await admin.GetAsync("/api/settings")).ReadAsync<Application.Settings.SettingsDto>();
        (await admin.PutAsJsonAsync("/api/settings", settings with { SaleTransfersPending = false })).EnsureSuccessStatusCode();
        try
        {
            var account = byWallet
                ? (await (await admin.PostAsJsonAsync("/api/financial-accounts",
                    new SaveAccountRequest(TestData.Unique("محفظة"), FinancialAccountType.Wallet, null, null, null))).ReadAsync<AccountDto>()).Id
                : (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
            var a = await admin.CreateProductAsync(retail: 1.01m, openingQuantity: 10, openingCost: 0.5m);
            var b = await admin.CreateProductAsync(retail: 1.01m, openingQuantity: 10, openingCost: 0.5m);

            // Each line 0.5 × 1.01 = 0.505: the server makes 0.51 + 0.51 = 1.02; the older till showed (and was paid) 1.01.
            var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
                [new(a.Id, 0.5m, 1.01m), new(b.Id, 0.5m, 1.01m)], [new(account, 1.01m)], null))).ReadAsync<SaleDetailDto>();
            (sale.Total, sale.Remaining, sale.InvoiceDiscount).Should().Be((1.01m, 0m, 0.01m));

            // With the agorot kept in the discount: 1.02 − 0.02 = 1.00 on the server, the till paid 1 (it counted 1.01 − 0.01).
            var kept = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0.01m,
                [new(a.Id, 0.5m, 1.01m), new(b.Id, 0.5m, 1.01m)], [new(account, 1m)], null))).ReadAsync<SaleDetailDto>();
            (kept.Total, kept.Remaining).Should().Be((1m, 0m));
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
        }
    }
}
