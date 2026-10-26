using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Users;
using Qwader.Domain.Identity;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BackupTests(ApiFactory factory)
{
    [Fact]
    public async Task Backup_holds_every_table_and_the_main_lists_without_passwords()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 33, openingQuantity: 2, openingCost: 20);

        var response = await admin.GetAsync("/api/settings/backup");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().StartWith("qwader-backup-");
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain(["data/Products.json", "data/SaleInvoices.json", "data/Customers.json", "data/Settings.json", "data/AspNetUsers.json",
            "excel/المنتجات.xlsx", "excel/العملاء.xlsx", "اقرأني.txt"]);
        names.Should().NotContain("data/RefreshTokens.json");

        string Read(string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();
        Read("data/Products.json").Should().Contain(product.Sku);
        var users = Read("data/AspNetUsers.json");
        users.Should().Contain("\"UserName\":\"admin\"");
        users.Should().NotContain("PasswordHash").And.NotContain("SecurityStamp");
        Read("data/Settings.json").Should().NotContain("Push.Vapid");
    }

    [Fact]
    public async Task Only_the_system_administrator_can_download_a_backup()
    {
        var admin = await factory.LoginAsync();
        var userName = $"bk_{Guid.NewGuid():N}"[..18];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "مدير معرض", null, "Temp12345", [SystemRoles.Manager]))).EnsureSuccessStatusCode();
        var manager = await factory.LoginAsync(userName, "Temp12345");
        (await manager.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Changed123"))).EnsureSuccessStatusCode();
        manager = await factory.LoginAsync(userName, "Changed123");

        (await manager.GetAsync("/api/settings/backup")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
