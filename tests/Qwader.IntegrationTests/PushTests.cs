using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Notifications;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Application.Settings;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PushTests(ApiFactory factory)
{
    private static string Endpoint(string name) => $"https://push.example/{name}-{Guid.NewGuid():N}";

    private static async Task SubscribeAsync(HttpClient client, string endpoint, PushTopicsDto topics) =>
        (await client.PutAsJsonAsync("/api/push/subscription", new SavePushSubscriptionRequest(endpoint, "p256dh-key", "auth-key", topics, "اختبار")))
            .EnsureSuccessStatusCode();

    [Fact]
    public async Task A_sale_notifies_each_device_about_what_its_user_may_see_and_chose()
    {
        var admin = await factory.LoginAsync();
        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", TestJson.Options);
        (await admin.PutAsJsonAsync("/api/settings", settings! with { SaleTransfersPending = true })).EnsureSuccessStatusCode();

        var adminPhone = Endpoint("admin");
        var adminLaptop = Endpoint("laptop");
        await SubscribeAsync(admin, adminPhone, new PushTopicsDto(true, true, true));
        await SubscribeAsync(admin, adminLaptop, new PushTopicsDto(Transfers: true, Stock: false, Sales: false));
        var seller = await CreateUserAsync(admin, SystemRoles.Sales);
        var sellerPhone = Endpoint("seller");
        await SubscribeAsync(seller, sellerPhone, new PushTopicsDto(true, true, true));

        // 3 in stock, alert level 2: selling all 3 empties the shelf.
        var product = await admin.CreateProductAsync(retail: 100, reorderLevel: 2);
        await admin.PurchaseAsync((await admin.CreateSupplierAsync()).Id, [new PurchaseLineInput(product.Id, 3, 60)]);
        var wallet = (await admin.LookupsAsync()).Accounts.First(a => a.Type != FinancialAccountType.Cash).Id;
        await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, TestData.Unique("زبون"), "0598887766", 0,
            [new(product.Id, 3, 100)], [new(wallet, 300)], null))).ReadAsync<SaleDetailDto>();

        (await factory.Push.WaitForAsync(adminPhone, "بيع جديد")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminPhone, "حوالة بانتظار التحقق")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminPhone, "نفد من المخزون")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminLaptop, "حوالة بانتظار التحقق")).Should().BeTrue();
        (await factory.Push.WaitForAsync(sellerPhone, "نفد من المخزون")).Should().BeTrue("sales staff may view stock");

        factory.Push.Received(adminLaptop, "بيع جديد").Should().BeFalse("this device did not choose sales");
        factory.Push.Received(sellerPhone, "بيع جديد").Should().BeFalse("sales staff cannot see others' invoices");
        factory.Push.Received(sellerPhone, "حوالة").Should().BeFalse("sales staff cannot verify transfers");
    }

    [Fact]
    public async Task A_device_that_removed_the_app_is_forgotten_after_the_next_notification()
    {
        var admin = await factory.LoginAsync();
        var gone = Endpoint("gone");
        await SubscribeAsync(admin, gone, new PushTopicsDto(true, true, true));
        (await admin.GetFromJsonAsync<PushStatusDto>($"/api/push?endpoint={Uri.EscapeDataString(gone)}", TestJson.Options))!.Topics.Should().NotBeNull();

        (await admin.PostAsync("/api/push/test", null)).EnsureSuccessStatusCode();
        (await factory.Push.WaitForAsync(gone, "الإشعارات تعمل")).Should().BeTrue();

        PushStatusDto? status = null;
        for (var i = 0; i < 40; i++) // the removal is saved right after the send
        {
            status = await admin.GetFromJsonAsync<PushStatusDto>($"/api/push?endpoint={Uri.EscapeDataString(gone)}", TestJson.Options);
            if (status!.Topics is null) break;
            await Task.Delay(50);
        }
        status!.Topics.Should().BeNull();
        status.PublicKey.Should().NotBeNullOrEmpty();
    }

    private async Task<HttpClient> CreateUserAsync(HttpClient admin, string role)
    {
        var userName = $"push_{Guid.NewGuid():N}"[..20];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "مستخدم إشعارات", null, "Temp12345", [role]))).EnsureSuccessStatusCode();
        var client = await factory.LoginAsync(userName, "Temp12345");
        (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Changed123"))).EnsureSuccessStatusCode();
        return await factory.LoginAsync(userName, "Changed123");
    }
}
