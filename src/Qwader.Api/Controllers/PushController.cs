using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qwader.Application.Notifications;

namespace Qwader.Api.Controllers;

/// <summary>Phone/browser notifications. Any signed-in user may subscribe their own device; what they receive still follows their permissions.</summary>
[ApiController]
[Route("api/push")]
[Authorize]
public sealed class PushController(IPushSubscriptionService push) : ControllerBase
{
    /// <summary>The server's public key, and this device's topics when it is subscribed.</summary>
    [HttpGet]
    public Task<PushStatusDto> Status([FromQuery] string? endpoint, CancellationToken ct) => push.GetStatusAsync(endpoint, ct);

    [HttpPut("subscription")]
    public async Task<IActionResult> Save(SavePushSubscriptionRequest request, CancellationToken ct)
    {
        await push.SaveAsync(request, ct);
        return NoContent();
    }

    [HttpDelete("subscription")]
    public async Task<IActionResult> Delete([FromQuery] string endpoint, CancellationToken ct)
    {
        await push.DeleteAsync(endpoint, ct);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        await push.SendTestAsync(ct);
        return NoContent();
    }
}
