using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;

namespace Qwader.IntegrationTests;

/// <summary>The signed-in user's own password, asked before the profits show on screen.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConfirmPasswordTests(ApiFactory factory)
{
    [Fact]
    public async Task The_right_password_is_accepted_and_a_wrong_one_refused()
    {
        var admin = await factory.LoginAsync();
        (await admin.PostAsJsonAsync("/api/auth/confirm-password", new ConfirmPasswordRequest(ApiFactory.AdminPassword))).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync("/api/auth/confirm-password", new ConfirmPasswordRequest("wrong-password"))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/auth/confirm-password", new ConfirmPasswordRequest(ApiFactory.AdminPassword))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
