using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qwader.Application.Common;
using Qwader.Application.Notifications;

namespace Qwader.Api.Controllers;

/// <summary>Each user's own notifications (sales, transfers, stock, salary), as the phone received them.</summary>
[ApiController]
[Route("api/notification-feed")]
[Authorize]
public sealed class NotificationFeedController(INotificationFeedService feed) : ControllerBase
{
    [HttpGet]
    public Task<FeedPageDto> List([FromQuery] PagedRequest request, CancellationToken ct) => feed.ListAsync(request, ct);

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
    {
        await feed.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await feed.MarkReadAsync(null, ct);
        return NoContent();
    }
}
