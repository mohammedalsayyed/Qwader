using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qwader.Application.Auth;
using Qwader.Infrastructure.Services;

namespace Qwader.IntegrationTests;

/// <summary>
/// Runs the real API against a dedicated SQL Server database that is recreated once per test run.
/// Override the server with QWADER_TEST_SQL (defaults to the local default instance).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Admin12345";

    private static readonly string SqlServer = Environment.GetEnvironmentVariable("QWADER_TEST_SQL") ?? ".";
    public static readonly string ConnectionString =
        $"Server={SqlServer};Database=Qwader_IntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

    public ApiFactory()
    {
        // Environment variables are visible to Program.cs before the host is built.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-tests-signing-key-0123456789-abcdefghij");
        Environment.SetEnvironmentVariable("Seed__AdminPassword", AdminPassword);
        Environment.SetEnvironmentVariable("Seed__SkipPasswordChange", "true");
        Environment.SetEnvironmentVariable("RateLimiting__LoginPerMinute", "10000");
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(
            new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF DB_ID('Qwader_IntegrationTests') IS NOT NULL
            BEGIN
                ALTER DATABASE Qwader_IntegrationTests SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE Qwader_IntegrationTests;
            END
            """;
        await cmd.ExecuteNonQueryAsync();

        _ = Services; // building the host migrates and seeds the fresh database
    }

    /// <summary>Notifications the API would have sent to phones.</summary>
    public FakePushSender Push { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
        });

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public HttpClient CreateCookieClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    public async Task<HttpClient> LoginAsync(string userName = AdminUser, string password = AdminPassword)
    {
        var client = CreateCookieClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public static class TestJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}

public sealed record ProblemBody(string Title, int Status, Dictionary<string, string[]>? Errors);

public sealed class FakePushSender : IPushSender
{
    private readonly List<(string Endpoint, string Payload)> sent = [];

    public Task<PushResult> SendAsync(Qwader.Domain.Notifications.PushSubscription subscription, string payload, CancellationToken ct)
    {
        lock (sent) sent.Add((subscription.Endpoint, payload));
        return Task.FromResult(subscription.Endpoint.Contains("gone") ? PushResult.Gone : PushResult.Sent);
    }

    /// <summary>Waits briefly for the background sender to deliver to <paramref name="endpoint"/> a message containing <paramref name="text"/>.</summary>
    public async Task<bool> WaitForAsync(string endpoint, string text, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 50)
        {
            lock (sent) if (sent.Any(s => s.Endpoint == endpoint && s.Payload.Contains(text))) return true;
            await Task.Delay(50);
        }
        return false;
    }

    public bool Received(string endpoint, string text)
    {
        lock (sent) return sent.Any(s => s.Endpoint == endpoint && s.Payload.Contains(text));
    }
}
