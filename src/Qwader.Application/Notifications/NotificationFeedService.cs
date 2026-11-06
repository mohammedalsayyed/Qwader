using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Notifications;

namespace Qwader.Application.Notifications;

public sealed record FeedItemDto(long Id, PushTopics Topic, string Title, string Body, string Url, DateTime CreatedAt, bool Read);

public sealed record FeedPageDto(IReadOnlyList<FeedItemDto> Items, int Page, int PageSize, int TotalCount, int TotalPages, int UnreadCount);

/// <summary>The signed-in user's own notifications, newest first.</summary>
public interface INotificationFeedService
{
    Task<FeedPageDto> ListAsync(PagedRequest request, CancellationToken ct = default);
    Task MarkReadAsync(long? id, CancellationToken ct = default);
}

public sealed class NotificationFeedService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : INotificationFeedService
{
    public async Task<FeedPageDto> ListAsync(PagedRequest request, CancellationToken ct = default)
    {
        var me = currentUser.RequiredUserId;
        var mine = db.UserNotifications.AsNoTracking().Where(n => n.UserId == me);
        var page = await mine.OrderByDescending(n => n.Id)
            .Select(n => new FeedItemDto(n.Id, n.Topic, n.Title, n.Body, n.Url, n.CreatedAt, n.ReadAt != null))
            .ToPagedAsync(request, ct);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        return new FeedPageDto(page.Items, page.Page, page.PageSize, page.TotalCount, page.TotalPages, unread);
    }

    /// <summary>One notification (opened), or all of them when <paramref name="id"/> is null.</summary>
    public async Task MarkReadAsync(long? id, CancellationToken ct = default)
    {
        var me = currentUser.RequiredUserId;
        var now = clock.UtcNow;
        await db.UserNotifications.Where(n => n.UserId == me && n.ReadAt == null && (id == null || n.Id == id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
