using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Notifications;
using Qwader.Domain.Administration;
using Qwader.Domain.Notifications;
using Qwader.Infrastructure.Persistence;
using WebPush;
using PushSubscription = Qwader.Domain.Notifications.PushSubscription;

namespace Qwader.Infrastructure.Services;

public sealed record PushJob(PushMessage Message, PushTopics Topic = PushTopics.None, string[]? Permissions = null, int? UserId = null);

/// <summary>In-memory hand-off from requests to the background sender. Bounded so a flood can never exhaust memory.</summary>
public sealed class PushQueue
{
    private readonly Channel<PushJob> channel = Channel.CreateBounded<PushJob>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
    public void Enqueue(PushJob job) => channel.Writer.TryWrite(job);
    public IAsyncEnumerable<PushJob> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}

public sealed class PushNotifier(PushQueue queue) : IPushNotifier
{
    public void Notify(PushTopics topic, PushMessage message, params string[] anyOfPermissions) =>
        queue.Enqueue(new PushJob(message, topic, anyOfPermissions));

    public void NotifyUser(int userId, PushMessage message) => queue.Enqueue(new PushJob(message, UserId: userId));
}

public enum PushResult { Sent, Gone, Failed }

public interface IPushSender
{
    Task<PushResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct);
}

/// <summary>Sends through the browser's push service (Google, Apple, Mozilla) signed with this server's VAPID keys.</summary>
public sealed class WebPushSender(VapidKeys keys, IConfiguration config, ILogger<WebPushSender> logger) : IPushSender, IDisposable
{
    private readonly WebPushClient client = new();

    public async Task<PushResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct)
    {
        var (publicKey, privateKey) = await keys.GetAsync(ct);
        var vapid = new VapidDetails(config["Push:Subject"] ?? "https://qwader.ma8228354.workers.dev", publicKey, privateKey);
        try
        {
            // A day is enough: an alert about a transfer or stock is stale after that.
            await client.SendNotificationAsync(new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                payload, new Dictionary<string, object> { ["vapidDetails"] = vapid, ["TTL"] = 86400 }, ct);
            return PushResult.Sent;
        }
        catch (WebPushException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden)
        {
            // Turned off, app removed, or made with another server key (403): forget it; the device can turn notifications on again.
            return PushResult.Gone;
        }
        catch (Exception e) when (e is WebPushException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(e, "Push to subscription {Id} failed", subscription.Id);
            return PushResult.Failed;
        }
    }

    public void Dispose() => client.Dispose();
}

/// <summary>The VAPID key pair, created once and kept in the settings table (the private key never leaves the server).</summary>
public sealed class VapidKeys(IServiceScopeFactory scopes)
{
    private const string PublicKeyName = "Push.VapidPublicKey";
    private const string PrivateKeyName = "Push.VapidPrivateKey";
    private readonly SemaphoreSlim gate = new(1, 1);
    private (string Public, string Private)? cached;

    public async Task<(string Public, string Private)> GetAsync(CancellationToken ct)
    {
        if (cached is { } hit) return hit;
        await gate.WaitAsync(ct);
        try
        {
            if (cached is { } again) return again;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Settings.Where(s => s.Key == PublicKeyName || s.Key == PrivateKeyName).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
            if (stored.TryGetValue(PublicKeyName, out var pub) && stored.TryGetValue(PrivateKeyName, out var priv))
                return (cached = (pub, priv)).Value;

            var generated = VapidHelper.GenerateVapidKeys();
            db.Settings.AddRange(new Setting { Key = PublicKeyName, Value = generated.PublicKey }, new Setting { Key = PrivateKeyName, Value = generated.PrivateKey });
            await db.SaveChangesAsync(ct);
            return (cached = (generated.PublicKey, generated.PrivateKey)).Value;
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>Delivers queued notifications one by one, outside any request, and forgets devices that unsubscribed.</summary>
public sealed class PushDispatcher(PushQueue queue, IServiceScopeFactory scopes, ILogger<PushDispatcher> logger) : BackgroundService
{
    // Arabic kept as UTF-8 rather than \uXXXX escapes: push payloads are limited to about 4 KB.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DeliverAsync(job, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Push delivery failed for «{Title}»", job.Message.Title);
            }
        }
    }

    private async Task DeliverAsync(PushJob job, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var query = db.PushSubscriptions.Where(s => s.User.IsActive);
        if (job.UserId is { } userId)
        {
            query = query.Where(s => s.UserId == userId);
        }
        else
        {
            var permissions = job.Permissions ?? [];
            query = query.Where(s => (s.Topics & job.Topic) != 0
                && s.User.UserRoles.Any(ur => ur.Role.Permissions.Any(p => permissions.Contains(p.PermissionCode))));
        }
        var targets = await query.ToListAsync(ct);
        if (targets.Count == 0) return;

        var payload = JsonSerializer.Serialize(job.Message, Json);
        foreach (var subscription in targets)
        {
            switch (await sender.SendAsync(subscription, payload, ct))
            {
                case PushResult.Sent: subscription.LastSentAt = clock.UtcNow; break;
                case PushResult.Gone: db.PushSubscriptions.Remove(subscription); break;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class PushSubscriptionService(AppDbContext db, VapidKeys keys, ICurrentUser currentUser, IClock clock, IPushNotifier notifier)
    : IPushSubscriptionService
{
    public async Task<PushStatusDto> GetStatusAsync(string? endpoint, CancellationToken ct = default)
    {
        var (publicKey, _) = await keys.GetAsync(ct);
        var userId = currentUser.RequiredUserId;
        var topics = string.IsNullOrEmpty(endpoint)
            ? null
            : await db.PushSubscriptions.AsNoTracking().Where(s => s.Endpoint == endpoint && s.UserId == userId)
                .Select(s => (PushTopics?)s.Topics).FirstOrDefaultAsync(ct);
        return new PushStatusDto(publicKey, topics is { } t ? PushTopicsDto.From(t) : null);
    }

    public async Task SaveAsync(SavePushSubscriptionRequest request, CancellationToken ct = default)
    {
        // A shared device that changes hands moves to whoever subscribed last.
        var subscription = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (subscription is null)
        {
            subscription = new PushSubscription { Endpoint = request.Endpoint, CreatedAt = clock.UtcNow };
            db.PushSubscriptions.Add(subscription);
        }
        subscription.UserId = currentUser.RequiredUserId;
        subscription.P256dh = request.P256dh;
        subscription.Auth = request.Auth;
        subscription.Topics = request.Topics.ToFlags();
        subscription.Device = string.IsNullOrWhiteSpace(request.Device) ? null : request.Device.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string endpoint, CancellationToken ct = default) =>
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint && s.UserId == currentUser.RequiredUserId).ExecuteDeleteAsync(ct);

    public async Task SendTestAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequiredUserId;
        if (!await db.PushSubscriptions.AnyAsync(s => s.UserId == userId, ct))
            throw new BusinessRuleException("فعّل الإشعارات على هذا الجهاز أولًا");
        notifier.NotifyUser(userId, new PushMessage("معرض قويدر", "الإشعارات تعمل على هذا الجهاز ✓", "/", "test"));
    }
}
