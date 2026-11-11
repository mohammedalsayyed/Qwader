using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Application.Settings;
using Qwader.Domain.Administration;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuditLogTests(ApiFactory factory)
{
    [Fact]
    public async Task An_entry_shows_the_product_code_and_name_instead_of_its_internal_number()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync();

        var log = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/audit-log?area=Product.Create&pageSize=50", TestJson.Options);

        log!.Items.Should().Contain(e => e.EntityId == product.Id.ToString() && e.EntityLabel == $"{product.Sku} · {product.DisplayName}");
    }

    [Fact]
    public async Task The_admin_deletes_a_period_with_their_password_and_the_deletion_is_logged()
    {
        var admin = await factory.LoginAsync();
        long oldId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = new AuditLog { Action = "Product.Update", EntityType = "Product", UserName = "قديم", OccurredAt = new DateTime(2001, 1, 15, 10, 0, 0, DateTimeKind.Utc) };
            db.AuditLogs.Add(old);
            await db.SaveChangesAsync();
            oldId = old.Id;
        }

        var wrong = await admin.PostAsJsonAsync("/api/settings/audit-log/delete", new DeleteAuditLogRequest(new(2001, 1, 1), new(2001, 1, 31), "wrong-password"));
        ((int)wrong.StatusCode).Should().BeOneOf(400, 422);

        var response = await admin.PostAsJsonAsync("/api/settings/audit-log/delete", new DeleteAuditLogRequest(new(2001, 1, 1), new(2001, 1, 31), ApiFactory.AdminPassword));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var check = factory.Services.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await after.AuditLogs.AnyAsync(a => a.Id == oldId)).Should().BeFalse();
        (await after.AuditLogs.AnyAsync(a => a.Action == "AuditLog.Delete" && a.NewValues!.Contains("2001-01-01"))).Should().BeTrue("the deletion itself stays in the log");
    }

    [Fact]
    public async Task The_product_list_shows_the_newest_first()
    {
        var admin = await factory.LoginAsync();
        var older = await admin.CreateProductAsync();
        var newer = await admin.CreateProductAsync();

        var list = await admin.GetFromJsonAsync<PagedResult<ProductListItemDto>>("/api/products?pageSize=5", TestJson.Options);

        list!.Items.Select(p => p.Id).Should().ContainInOrder(newer.Id, older.Id);
        list.Items[0].Id.Should().Be(newer.Id);
    }
}
