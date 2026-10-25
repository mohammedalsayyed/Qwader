using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Auth;
using Qwader.Application.Common;
using Qwader.Application.Settings;
using Qwader.Application.Users;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PermissionTests(ApiFactory factory)
{
    private async Task<HttpClient> CreateUserAndLoginAsync(string role)
    {
        var admin = await factory.LoginAsync();
        var userName = $"{role.ToLowerInvariant()}_{Guid.NewGuid():N}"[..20];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "مستخدم اختبار", null, "Temp12345", [role])))
            .EnsureSuccessStatusCode();
        var client = await factory.LoginAsync(userName, "Temp12345");
        (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Changed123")))
            .EnsureSuccessStatusCode();
        return await factory.LoginAsync(userName, "Changed123");
    }

    [Fact]
    public async Task Sales_role_cannot_manage_users_or_settings_but_can_read_store_settings()
    {
        var sales = await CreateUserAndLoginAsync(SystemRoles.Sales);

        (await sales.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.GetAsync("/api/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await (await sales.GetAsync("/api/settings")).Content.ReadFromJsonAsync<SettingsDto>(TestJson.Options);
        (await sales.PutAsJsonAsync("/api/settings", settings)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sales_role_token_does_not_carry_cost_or_void_permissions()
    {
        var sales = await CreateUserAndLoginAsync(SystemRoles.Sales);

        var me = await sales.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", TestJson.Options);

        me!.Permissions.Should().Contain(Permissions.SalesCreate)
            .And.NotContain([Permissions.ProductsViewCost, Permissions.SalesVoid, Permissions.TransfersReview, Permissions.ReportsProfit]);
    }

    [Fact]
    public async Task Administrator_role_permissions_cannot_be_reduced()
    {
        var admin = await factory.LoginAsync();
        var roles = await admin.GetFromJsonAsync<List<RoleDto>>("/api/roles", TestJson.Options);
        var adminRole = roles!.Single(r => r.Name == SystemRoles.Admin);

        var response = await admin.PutAsJsonAsync($"/api/roles/{adminRole.Id}",
            new SaveRoleRequest(adminRole.DisplayName, adminRole.Description, [Permissions.ProductsView]));

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Administrator_cannot_deactivate_own_account()
    {
        var admin = await factory.LoginAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", TestJson.Options);

        var response = await admin.PutAsJsonAsync($"/api/users/{me!.Id}",
            new UpdateUserRequest(me.FullName, null, IsActive: false, [SystemRoles.Admin]));

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Duplicate_username_is_reported_on_the_username_field()
    {
        var admin = await factory.LoginAsync();

        var response = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest(ApiFactory.AdminUser, "مكرر", null, "Temp12345", [SystemRoles.Viewer]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(TestJson.Options);
        problem!.Errors!["userName"].Should().Contain("اسم المستخدم مستخدم من قبل");
    }

    [Fact]
    public async Task Updating_settings_persists_and_writes_an_audit_entry()
    {
        var admin = await factory.LoginAsync();
        var current = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", TestJson.Options);

        var response = await admin.PutAsJsonAsync("/api/settings", current! with { StorePhone = "0599123456", DefaultMarginPercent = 30 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloaded = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", TestJson.Options);
        reloaded!.StorePhone.Should().Be("0599123456");
        reloaded.DefaultMarginPercent.Should().Be(30);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AnyAsync(a => a.Action == "Settings.Update" && a.NewValues!.Contains("0599123456"))).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_creates_the_initial_financial_accounts_and_default_warehouse()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.FinancialAccounts.Select(a => a.Name).ToListAsync()).Should()
            .Contain(["الصندوق النقدي", "جوال بي", "بنك فلسطين", "بنك القدس", "البنك الإسلامي", "بال بي"]);
        (await db.Warehouses.CountAsync(w => w.IsDefault)).Should().Be(1);
    }

    [Fact]
    public async Task Paged_user_list_clamps_page_size()
    {
        var admin = await factory.LoginAsync();
        var page = await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/users?pageSize=5000", TestJson.Options);
        page!.PageSize.Should().Be(100);
    }
}
