using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Qwader.Api.Infrastructure;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProxyGateTests(ApiFactory factory)
{
    private const string Secret = "test-proxy-secret-123";

    private HttpClient GatedClient() =>
        factory.WithWebHostBuilder(b => b
                .UseSetting("Proxy:SharedSecret", Secret)
                .UseSetting("Proxy:PublicUrl", "https://qwader.example.dev"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Api_calls_without_the_proxy_secret_are_refused()
    {
        var response = await GatedClient().PostAsync("/api/auth/login", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Api_calls_with_the_proxy_secret_pass_through()
    {
        var client = GatedClient();
        client.DefaultRequestHeaders.Add(ProxyGate.HeaderName, Secret);

        var response = await client.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the gate lets it through to normal authentication");
    }

    [Fact]
    public async Task Wrong_secret_is_refused()
    {
        var client = GatedClient();
        client.DefaultRequestHeaders.Add(ProxyGate.HeaderName, "guess");

        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Direct_page_visits_redirect_to_the_public_https_address()
    {
        var response = await GatedClient().GetAsync("/products?stock=Low");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("https://qwader.example.dev/products?stock=Low");
    }

    [Fact]
    public async Task Health_check_stays_open() =>
        (await GatedClient().GetAsync("/api/health")).StatusCode.Should().Be(HttpStatusCode.OK);
}
