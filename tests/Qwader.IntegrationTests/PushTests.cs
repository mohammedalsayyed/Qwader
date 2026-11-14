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
    private static string Endpoint(string name) => $"https://fcm.googleapis.com/fcm/send/{name}-{Guid.NewGuid():N}";

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
        var wallet = (await admin.LookupsAsync()).Accounts.First(a => a.Type != FinancialAccountType.Cash);
        await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, TestData.Unique("زبون"), "0598887766", 0,
            [new(product.Id, 3, 100)], [new(wallet.Id, 300)], null))).ReadAsync<SaleDetailDto>();

        (await factory.Push.WaitForAsync(adminPhone, "بيع جديد")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminPhone, $"الدفع: {wallet.Name} 300 ₪ (بانتظار التحقق)")).Should().BeTrue("the sale says how it was paid");
        // The transfer says where the money went and who took it.
        (await factory.Push.WaitForAsync(adminPhone, $"حوالة 300 ₪ إلى {wallet.Name}")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminPhone, "سجّلها: ")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminPhone, "نفد من المخزون")).Should().BeTrue();
        (await factory.Push.WaitForAsync(adminLaptop, "بانتظار التحقق")).Should().BeTrue();
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

    [Fact]
    public async Task Each_user_keeps_their_own_notifications_list_even_without_phone_notifications()
    {
        var admin = await factory.LoginAsync();
        var seller = await CreateUserAsync(admin, SystemRoles.Sales);
        var product = await admin.CreateProductAsync(retail: 77, openingQuantity: 5, openingCost: 10);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash);
        var sale = await (await seller.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 77)], [new(cash.Id, 77)], null))).ReadAsync<SaleDetailDto>();

        FeedPageDto? feed = null;
        for (var i = 0; i < 60; i++) // written by the background sender
        {
            feed = await admin.GetFromJsonAsync<FeedPageDto>("/api/notification-feed?pageSize=50", TestJson.Options);
            if (feed!.Items.Any(n => n.Body.Contains(sale.Number))) break;
            await Task.Delay(50);
        }
        var item = feed!.Items.First(n => n.Body.Contains(sale.Number));
        item.Title.Should().Be("بيع جديد — 77 ₪");
        item.Url.Should().Be($"/sales/{sale.Id}");
        item.Read.Should().BeFalse();
        feed.UnreadCount.Should().BeGreaterThan(0);

        var sellerFeed = await seller.GetFromJsonAsync<FeedPageDto>("/api/notification-feed?pageSize=50", TestJson.Options);
        sellerFeed!.Items.Should().NotContain(n => n.Body.Contains(sale.Number), "sales staff do not see every invoice");

        (await admin.PostAsync($"/api/notification-feed/{item.Id}/read", null)).EnsureSuccessStatusCode();
        (await admin.GetFromJsonAsync<FeedPageDto>("/api/notification-feed?pageSize=50", TestJson.Options))!.Items.First(n => n.Id == item.Id).Read.Should().BeTrue();
        (await admin.PostAsync("/api/notification-feed/read-all", null)).EnsureSuccessStatusCode();
        (await admin.GetFromJsonAsync<FeedPageDto>("/api/notification-feed", TestJson.Options))!.UnreadCount.Should().Be(0);
    }

    private async Task<HttpClient> CreateUserAsync(HttpClient admin, string role)
    {
        var userName = $"push_{Guid.NewGuid():N}"[..20];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "مستخدم إشعارات", null, "Temp12345", [role]))).EnsureSuccessStatusCode();
        var client = await factory.LoginAsync(userName, "Temp12345");
        (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Changed123"))).EnsureSuccessStatusCode();
        return await factory.LoginAsync(userName, "Changed123");
    }

    [Fact]
    public async Task A_sale_sent_after_the_connection_returned_notifies_like_any_sale()
    {
        var admin = await factory.LoginAsync();
        var phone = Endpoint("offline-admin");
        await SubscribeAsync(admin, phone, new PushTopicsDto(true, true, true));
        var product = await admin.CreateProductAsync(retail: 40, openingQuantity: 1, openingCost: 10);
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash);

        // Sold 2 while the device was offline, but only 1 was in stock: the sale is kept and a manager is told.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 2, 40)], [new(cash.Id, 80)], null, DateTime.UtcNow.AddMinutes(-20)))).ReadAsync<SaleDetailDto>();

        (await factory.Push.WaitForAsync(phone, "بيع جديد — 80 ₪")).Should().BeTrue();
        (await factory.Push.WaitForAsync(phone, $"فاتورة {sale.Number}")).Should().BeTrue();
        (await factory.Push.WaitForAsync(phone, $"فاتورة تحتاج مراجعة — {sale.Number}")).Should().BeTrue();
        factory.Push.Received(phone, "بدون إنترنت").Should().BeFalse("the sale notification looks the same as any other");
    }
}
