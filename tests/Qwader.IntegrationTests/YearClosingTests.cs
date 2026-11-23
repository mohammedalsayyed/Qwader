using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Qwader.Application.Finance;
using Qwader.Application.Settings;
using Qwader.Domain.Finance;

namespace Qwader.IntegrationTests;

/// <summary>«إقفال السنة»: off by default; once a year is closed nothing dated in it can be recorded, voided or re-dated until it is reopened.</summary>
[Collection(ApiCollection.Name)]
public sealed class YearClosingTests(ApiFactory factory)
{
    private static async Task SqlAsync(string sql)
    {
        await using var connection = new SqlConnection(ApiFactory.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_closed_year_is_locked_until_reopened()
    {
        var admin = await factory.LoginAsync();
        async Task<YearClosingStatusDto> Status() => await (await admin.GetAsync("/api/year-closing")).ReadAsync<YearClosingStatusDto>();

        var off = await Status();
        off.Enabled.Should().BeFalse("it is off until turned on in the settings");
        (await admin.PostAsJsonAsync("/api/year-closing/close", new CloseYearRequest(off.NextYear))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // The store «started» last year, so last year can be closed.
        var lastYear = DateTime.UtcNow.Year - 1;
        await SqlAsync($"UPDATE AspNetUsers SET CreatedAt = '{lastYear}-03-01' WHERE UserName = 'admin'");
        try
        {
            var cash = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
                new SaveAccountRequest(TestData.Unique("صندوق إقفال"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
            await admin.FundAsync(cash, 1000);
            Task<HttpResponseMessage> Withdraw(DateOnly? date) => admin.PostAsJsonAsync("/api/payment-vouchers",
                new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.OwnerWithdrawal, null, "صاحب المحل", 10, cash, null, null, null, ConfirmDuplicate: true, Date: date));
            var old = await (await Withdraw(new DateOnly(lastYear, 6, 1))).ReadAsync<VoucherDto>();   // before closing: fine

            var on = await (await admin.PutAsJsonAsync("/api/year-closing/enabled", new SetYearClosingRequest(true))).ReadAsync<YearClosingStatusDto>();
            (on.Enabled, on.NextYear, on.YearEnded, on.LockedThrough).Should().Be((true, lastYear, true, (int?)null));
            (await admin.PostAsJsonAsync("/api/year-closing/close", new CloseYearRequest(lastYear + 1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var closed = await (await admin.PostAsJsonAsync("/api/year-closing/close", new CloseYearRequest(lastYear))).ReadAsync<YearClosingStatusDto>();
            closed.LockedThrough.Should().Be(lastYear);
            closed.History.First().Should().Match<ClosedYearDto>(h => h.Year == lastYear && h.ReopenedAt == null && h.ClosedBy != null);

            // Nothing dated in it: not a new document, not voiding an old one. Today's work goes on.
            var refused = await Withdraw(new DateOnly(lastYear, 7, 1));
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("مقفلة");
            (await admin.PostAsJsonAsync($"/api/payment-vouchers/{old.Id}/void", new VoidDocumentRequest("خطأ"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Withdraw(null)).EnsureSuccessStatusCode();

            // Turned off, nothing is locked; on again, the closed year is locked again.
            (await admin.PutAsJsonAsync("/api/year-closing/enabled", new SetYearClosingRequest(false))).EnsureSuccessStatusCode();
            var allowed = await Withdraw(new DateOnly(lastYear, 7, 2));
            allowed.IsSuccessStatusCode.Should().BeTrue(await allowed.Content.ReadAsStringAsync());
            (await admin.PutAsJsonAsync("/api/year-closing/enabled", new SetYearClosingRequest(true))).EnsureSuccessStatusCode();
            (await Withdraw(new DateOnly(lastYear, 7, 3))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // Reopened (with a reason), it can be corrected again; the history keeps who reopened it and why.
            (await admin.PostAsJsonAsync($"/api/year-closing/{lastYear}/reopen", new ReopenYearRequest(""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var reopened = await (await admin.PostAsJsonAsync($"/api/year-closing/{lastYear}/reopen", new ReopenYearRequest("تصحيح سند"))).ReadAsync<YearClosingStatusDto>();
            reopened.LockedThrough.Should().BeNull();
            reopened.History.First().ReopenReason.Should().Be("تصحيح سند");
            (await admin.PostAsJsonAsync($"/api/payment-vouchers/{old.Id}/void", new VoidDocumentRequest("خطأ"))).EnsureSuccessStatusCode();
        }
        finally
        {
            var status = await Status();
            if (status.LockedThrough is { } y)
                await admin.PostAsJsonAsync($"/api/year-closing/{y}/reopen", new ReopenYearRequest("نهاية الاختبار"));
            await admin.PutAsJsonAsync("/api/year-closing/enabled", new SetYearClosingRequest(false));
            await SqlAsync("UPDATE AspNetUsers SET CreatedAt = SYSUTCDATETIME() WHERE UserName = 'admin'");
        }
    }
}
