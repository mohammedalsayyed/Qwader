using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Settings;
using Qwader.Application.Users;
using Qwader.Domain.Identity;

namespace Qwader.IntegrationTests;

/// <summary>Users from creation to sign-out: passwords, deactivation, lockout and role changes.</summary>
[Collection(ApiCollection.Name)]
public sealed class AccountLifecycleTests(ApiFactory factory)
{
    private static string NewUserName() => $"u_{Guid.NewGuid():N}"[..18];

    private async Task<(HttpClient Admin, UserDto User)> CreateUserAsync(params string[] roles)
    {
        var admin = await factory.LoginAsync();
        var user = await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(NewUserName(), "مستخدم دورة حياة", null, "Temp12345", roles)))
            .ReadAsync<UserDto>();
        return (admin, user);
    }

    private Task<HttpResponseMessage> TryLoginAsync(string userName, string password) =>
        factory.CreateCookieClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));

    [Fact]
    public async Task After_changing_the_password_only_the_new_one_works()
    {
        var (_, user) = await CreateUserAsync(SystemRoles.Sales);
        var client = await factory.LoginAsync(user.UserName, "Temp12345");
        (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "NewPass123"))).EnsureSuccessStatusCode();

        (await TryLoginAsync(user.UserName, "Temp12345")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TryLoginAsync(user.UserName, "NewPass123")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_sign_in()
    {
        var (admin, user) = await CreateUserAsync(SystemRoles.Sales);
        (await admin.PutAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest(user.FullName, null, false, [SystemRoles.Sales])))
            .EnsureSuccessStatusCode();

        var response = await TryLoginAsync(user.UserName, "Temp12345");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("معطّل");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var (_, user) = await CreateUserAsync(SystemRoles.Viewer);
        for (var i = 0; i < 5; i++) await TryLoginAsync(user.UserName, "wrong-password");

        var response = await TryLoginAsync(user.UserName, "Temp12345");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("تم إيقاف الدخول مؤقتًا");
    }

    [Fact]
    public async Task A_permission_added_to_a_role_applies_from_the_next_sign_in()
    {
        var admin = await factory.LoginAsync();
        var role = await (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest(TestData.Unique("دور"), null, [Permissions.SalesCreate])))
            .ReadAsync<RoleDto>();
        var user = await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(NewUserName(), "موظف دور مخصص", null, "Temp12345", [role.Name])))
            .ReadAsync<UserDto>();

        async Task<IReadOnlyList<string>> PermissionsAtLoginAsync() =>
            (await (await TryLoginAsync(user.UserName, "Temp12345")).ReadAsync<AuthResponse>()).User.Permissions;

        (await PermissionsAtLoginAsync()).Should().NotContain(Permissions.SalesViewAll);
        (await admin.PutAsJsonAsync($"/api/roles/{role.Id}", new SaveRoleRequest(role.DisplayName, null, [Permissions.SalesCreate, Permissions.SalesViewAll])))
            .EnsureSuccessStatusCode();
        (await PermissionsAtLoginAsync()).Should().Contain(Permissions.SalesViewAll);
    }

    [Fact]
    public async Task Only_the_system_administrator_can_reset_the_system()
    {
        var (_, user) = await CreateUserAsync(SystemRoles.Manager);
        var manager = await factory.LoginAsync(user.UserName, "Temp12345");
        (await manager.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Changed123"))).EnsureSuccessStatusCode();
        manager = await factory.LoginAsync(user.UserName, "Changed123");

        var response = await manager.PostAsJsonAsync("/api/settings/reset", new SystemResetRequest(SystemResetMode.TransactionsOnly, "Changed123"), TestJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
