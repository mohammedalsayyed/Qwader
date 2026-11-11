using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Settings;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class IntegrityTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_balance_matches_its_movements_and_a_tampered_one_is_reported()
    {
        var admin = await factory.LoginAsync();
        var customer = await admin.PostAsJsonAsync("/api/customers", new { name = TestData.Unique("عميل فحص"), phone = "059" + Random.Shared.Next(1000000, 9999999), openingBalance = 40 });
        customer.EnsureSuccessStatusCode();

        // Whatever the other tests did (sales, purchases, returns, voids, counts…), the books must agree with themselves.
        var report = await admin.GetFromJsonAsync<IntegrityReport>("/api/settings/integrity", TestJson.Options);
        report!.Checks.Where(c => c.IssueCount > 0).Should().BeEmpty();
        report.Ok.Should().BeTrue();
        report.Checks.Should().Contain(c => c.Name == "ديون العملاء" && c.Checked > 0);

        // A balance changed behind the ledger's back is caught (then put back, so the shared database stays clean).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = await db.Customers.Where(c => !c.IsWalkIn && c.Balance == 40).OrderByDescending(c => c.Id).FirstAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE Customers SET Balance = Balance + 7 WHERE Id = {target.Id}");
        try
        {
            var tampered = await admin.GetFromJsonAsync<IntegrityReport>("/api/settings/integrity", TestJson.Options);
            tampered!.Ok.Should().BeFalse();
            var issue = tampered.Checks.Single(c => c.Name == "ديون العملاء").Issues.Should().ContainSingle().Subject;
            issue.Stored.Should().Be(47);
            issue.Computed.Should().Be(40);
            issue.Link.Should().Be($"/customers/{target.Id}");
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"UPDATE Customers SET Balance = Balance - 7 WHERE Id = {target.Id}");
        }
    }

    [Fact]
    public async Task Api_responses_are_compressed_but_sign_in_is_not()
    {
        var admin = await factory.LoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products?pageSize=50");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await admin.SendAsync(request);
        response.Content.Headers.ContentEncoding.Should().Contain("br");

        var anonymous = factory.CreateClient();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName = ApiFactory.AdminUser, password = ApiFactory.AdminPassword }),
        };
        login.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        (await anonymous.SendAsync(login)).Content.Headers.ContentEncoding.Should().BeEmpty();
    }
}
