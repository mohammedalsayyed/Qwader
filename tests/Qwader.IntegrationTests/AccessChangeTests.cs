using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Users;
using Qwader.Domain.Identity;

namespace Qwader.IntegrationTests;

/// <summary>
/// Permissions changed by an administrator apply while the employee's app is open: the old token is refused (401 with
/// X-Access-Changed), the refresh hands out the new permissions — no sign-out needed.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccessChangeTests(ApiFactory factory)
{
    [Fact]
    public async Task A_role_changed_by_the_administrator_reaches_the_employee_at_once()
    {
        var admin = await factory.LoginAsync();
        var role = await (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest(TestData.Unique("دور"), null, [Permissions.ProductsView])))
            .ReadAsync<RoleDto>();
        var userName = TestData.Unique("emp");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "موظف", null, "Temp12345", [role.Name]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Emp123456"))).EnsureSuccessStatusCode();
        var employee = await factory.LoginAsync(userName, "Emp123456");
        (await employee.GetAsync("/api/products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await employee.GetAsync("/api/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The administrator adds the customers permission to the role.
        (await admin.PutAsJsonAsync($"/api/roles/{role.Id}", new SaveRoleRequest(role.DisplayName, null, [Permissions.ProductsView, Permissions.CustomersManage])))
            .EnsureSuccessStatusCode();
        await Task.Delay(TimeSpan.FromSeconds(11)); // the fingerprint is re-read at most every 10 seconds

        // The old token is refused with the marker; the refresh (the app does it by itself) carries the new permission.
        var refused = await employee.GetAsync("/api/products");
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.Contains("X-Access-Changed").Should().BeTrue();
        var refreshed = await (await employee.PostAsync("/api/auth/refresh", null)).ReadAsync<AuthResponse>();
        refreshed.User.Permissions.Should().Contain(Permissions.CustomersManage);
        employee.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        (await employee.GetAsync("/api/customers")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
