using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Auth;
using Qwader.Application.Finance;
using Qwader.Application.Notifications;
using Qwader.Application.Payroll;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Payroll;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PayrollTests(ApiFactory factory)
{
    private async Task<(int UserId, HttpClient Client)> NewUserAsync(HttpClient admin)
    {
        var userName = $"emp_{Guid.NewGuid():N}"[..18];
        var user = await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "عامل " + userName[^4..], null, "Temp12345", [SystemRoles.Sales])))
            .ReadAsync<UserDto>();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Worker12345"))).EnsureSuccessStatusCode();
        return (user.Id, await factory.LoginAsync(userName, "Worker12345"));
    }

    private async Task<decimal> BalanceAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.CurrentBalance).FirstAsync();
    }

    [Fact]
    public async Task Advances_deductions_and_bonuses_settle_into_one_salary_paid_from_an_account()
    {
        var admin = await factory.LoginAsync();
        var (userId, worker) = await NewUserAsync(admin);
        var workerPhone = $"https://fcm.googleapis.com/fcm/send/worker-{Guid.NewGuid():N}";
        (await worker.PutAsJsonAsync("/api/push/subscription", new SavePushSubscriptionRequest(workerPhone, "p256dh-key", "auth-key", new PushTopicsDto(false, false, false), "اختبار")))
            .EnsureSuccessStatusCode();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        await admin.FundAsync(cash, 10_000);
        var start = await BalanceAsync(cash);
        var expensesAtStart = await ExpensesTodayAsync(admin);

        (await admin.PostAsJsonAsync("/api/payroll/employees", new SaveEmployeeRequest(userId, "abc12", PayPeriod.Monthly, 3000, null, null)))
            .StatusCode.Should().Be((HttpStatusCode)400, "a phone number is digits only");
        var employee = await (await admin.PostAsJsonAsync("/api/payroll/employees", new SaveEmployeeRequest(userId, "0591234567", PayPeriod.Monthly, 3000, null, null)))
            .ReadAsync<EmployeeDto>();
        employee.Phone.Should().Be("0591234567");

        async Task<EmployeeDto> Entry(PayrollEntryType type, decimal amount, int? account = null) =>
            await (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/entries",
                new CreatePayrollEntryRequest(Guid.NewGuid(), type, amount, null, account, null))).ReadAsync<EmployeeDto>();
        (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/entries",
            new CreatePayrollEntryRequest(Guid.NewGuid(), PayrollEntryType.Advance, 500, null, null, null))).StatusCode.Should().Be((HttpStatusCode)400, "an advance needs an account");
        await Entry(PayrollEntryType.Advance, 500, cash);
        await Entry(PayrollEntryType.Deduction, 100);
        var before = await Entry(PayrollEntryType.Bonus, 50);
        before.OpenAdvances.Should().Be(500);
        (await BalanceAsync(cash)).Should().Be(start - 500, "the advance is paid at once");

        // The worker hears about each, on the phone and in their notifications list — even with no topic chosen.
        (await factory.Push.WaitForAsync(workerPhone, "تم صرف سلفة لك — 500 ₪")).Should().BeTrue();
        (await factory.Push.WaitForAsync(workerPhone, "خصم من راتبك — 100 ₪")).Should().BeTrue();
        (await factory.Push.WaitForAsync(workerPhone, "مكافأة لك — 50 ₪")).Should().BeTrue();
        (await worker.GetFromJsonAsync<FeedPageDto>("/api/notification-feed", TestJson.Options))!.Items
            .Should().Contain(n => n.Title == "تم صرف سلفة لك — 500 ₪" && n.Url == "/account/payroll");

        // The advance is owed by the employee, not an expense yet, and is undone only from the payroll screen.
        (await ExpensesTodayAsync(admin)).Should().Be(expensesAtStart);
        var voucherId = await AdvanceVoucherAsync(before.Entries.First(e => e.Type == PayrollEntryType.Advance).Id);
        (await admin.PostAsJsonAsync($"/api/payment-vouchers/{voucherId}/void", new VoidDocumentRequest("خطأ"))).StatusCode.Should().Be((HttpStatusCode)422);

        // More days than October holds is refused.
        (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/settlements",
            new CreateSettlementRequest(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 3, cash, null))).StatusCode.Should().Be((HttpStatusCode)400);

        var paid = await (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/settlements",
            new CreateSettlementRequest(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1, cash, null))).ReadAsync<EmployeeDto>();
        var s = paid.Settlements.Single();
        (s.Gross, s.Bonuses, s.Deductions, s.Advances, s.Net).Should().Be((3000m, 50m, 100m, 500m, 2450m));
        paid.OpenAdvances.Should().Be(0);
        (await BalanceAsync(cash)).Should().Be(start - 500 - 2450);
        // Now the whole salary is an expense: 3000 + 50 − 100, of which 500 was paid earlier as the advance.
        (await ExpensesTodayAsync(admin)).Should().Be(expensesAtStart + 2950);

        // The same period cannot be paid twice.
        (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/settlements",
            new CreateSettlementRequest(Guid.NewGuid(), new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 14), 1, cash, null))).StatusCode.Should().Be((HttpStatusCode)422);

        // The worker's phone is told what was paid, without the store's account.
        (await factory.Push.WaitForAsync(workerPhone, "تم صرف راتبك — 2,450 ₪")).Should().BeTrue();
        (await factory.Push.WaitForAsync(workerPhone, "سلف: − 500 ₪")).Should().BeTrue();
        factory.Push.Received(workerPhone, "الصندوق").Should().BeFalse();

        // The worker sees their own record, without which account paid it; not the payroll screens.
        var mine = (await worker.GetFromJsonAsync<EmployeeDto>("/api/payroll/me", TestJson.Options))!;
        mine.Settlements.Should().ContainSingle(x => x.Net == 2450 && x.AccountName == null);
        mine.Entries.Should().OnlyContain(x => x.AccountName == null && x.ExpenseNumber == null);
        (await worker.GetAsync("/api/payroll/employees")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/payroll/me")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Voiding the settlement gives the money back and reopens what it took.
        var voided = await (await admin.PostAsJsonAsync($"/api/payroll/settlements/{s.Id}/void", new VoidDocumentRequest("خطأ في الأيام"))).ReadAsync<EmployeeDto>();
        voided.OpenAdvances.Should().Be(500);
        voided.OpenDeductions.Should().Be(100);
        (await BalanceAsync(cash)).Should().Be(start - 500);
        (await ExpensesTodayAsync(admin)).Should().Be(expensesAtStart, "the advance is owed again, not an expense");
    }

    [Fact]
    public async Task Advances_larger_than_the_salary_carry_to_the_next_settlement()
    {
        var admin = await factory.LoginAsync();
        var (userId, _) = await NewUserAsync(admin);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        await admin.FundAsync(cash, 1000);
        var employee = await (await admin.PostAsJsonAsync("/api/payroll/employees", new SaveEmployeeRequest(userId, null, PayPeriod.Daily, 100, null, null)))
            .ReadAsync<EmployeeDto>();
        await (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/entries",
            new CreatePayrollEntryRequest(Guid.NewGuid(), PayrollEntryType.Advance, 800, null, cash, null))).ReadAsync<EmployeeDto>();

        // 5 days = 500, advances 800: nothing to pay, 300 still owed by the worker.
        var after = await (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/settlements",
            new CreateSettlementRequest(Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), 5, null, null))).ReadAsync<EmployeeDto>();
        after.Settlements.Single().Net.Should().Be(0);
        after.Settlements.Single().CarriedAdvance.Should().Be(300);
        after.OpenAdvances.Should().Be(300);

        var next = await (await admin.PostAsJsonAsync($"/api/payroll/employees/{employee.Id}/settlements",
            new CreateSettlementRequest(Guid.NewGuid(), new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 10), 5, cash, null))).ReadAsync<EmployeeDto>();
        next.Settlements.First().Net.Should().Be(200);
        next.OpenAdvances.Should().Be(0);

        // The first cannot be undone while the later one holds its carried advance.
        (await admin.PostAsJsonAsync($"/api/payroll/settlements/{after.Settlements.Single().Id}/void", new VoidDocumentRequest("x")))
            .StatusCode.Should().Be((HttpStatusCode)422);
    }

    private async Task<int> AdvanceVoucherAsync(int entryId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PayrollEntries.Where(e => e.Id == entryId).Select(e => e.PaymentVoucherId!.Value).FirstAsync();
    }

    private static async Task<decimal> ExpensesTodayAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<Application.Reports.PeriodReportDto>("/api/reports/period", TestJson.Options))!.Expenses ?? 0;
}
