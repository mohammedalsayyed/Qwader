using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Users;
using Qwader.Domain.Identity;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_token_and_all_admin_permissions()
    {
        var client = factory.CreateCookieClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUser, ApiFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.User.Permissions.Should().BeEquivalentTo(Permissions.All.Select(p => p.Code));
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("qwader_rt=") && c.Contains("httponly"));
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected_with_arabic_message()
    {
        var client = factory.CreateCookieClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUser, "not-the-password1"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(TestJson.Options);
        problem!.Title.Should().Be("اسم المستخدم أو كلمة المرور غير صحيحة");
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_issues_a_working_token()
    {
        var client = await factory.LoginAsync();

        var refresh = await client.PostAsync("/api/auth/refresh", null);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await refresh.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        var fresh = factory.CreateClient();
        fresh.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        (await fresh.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = await factory.LoginAsync();

        (await client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task New_user_must_change_password_before_using_the_api()
    {
        var admin = await factory.LoginAsync();
        var userName = "cashier_" + Guid.NewGuid().ToString("N")[..6];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "موظف مبيعات", null, "Temp12345", [SystemRoles.Sales])))
            .EnsureSuccessStatusCode();

        var user = await factory.LoginAsync(userName, "Temp12345");
        var blocked = await user.GetAsync("/api/lookups");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadFromJsonAsync<ProblemBody>(TestJson.Options))!.Title.Should().Contain("تغيير كلمة المرور");

        (await user.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "MyOwnPass99")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var relogged = await factory.LoginAsync(userName, "MyOwnPass99");
        (await relogged.GetAsync("/api/lookups")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Change_password_validation_errors_are_keyed_by_camel_case_field()
    {
        var client = await factory.LoginAsync();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(ApiFactory.AdminPassword, "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(TestJson.Options);
        problem!.Errors.Should().ContainKey("newPassword");
    }
}
