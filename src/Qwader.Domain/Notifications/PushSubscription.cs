using Qwader.Domain.Identity;

namespace Qwader.Domain.Notifications;

/// <summary>What a device wants to hear about. A user only receives a topic their permissions allow.</summary>
[Flags]
public enum PushTopics
{
    None = 0,
    /// <summary>A transfer is waiting for verification (needs Transfers.Review).</summary>
    Transfers = 1,
    /// <summary>A product ran low or out after a sale (needs Inventory.View).</summary>
    Stock = 2,
    /// <summary>Every new or voided invoice (needs Sales.ViewAll).</summary>
    Sales = 4,
}

/// <summary>A browser/phone that accepted notifications (Web Push). One row per device; the endpoint identifies it.</summary>
public class PushSubscription
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public PushTopics Topics { get; set; }
    public string? Device { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSentAt { get; set; }

    public AppUser User { get; set; } = null!;
}
