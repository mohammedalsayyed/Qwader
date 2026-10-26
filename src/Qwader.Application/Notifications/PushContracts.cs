using System.Globalization;
using FluentValidation;
using Qwader.Domain.Notifications;

namespace Qwader.Application.Notifications;

/// <summary>What the phone shows. <paramref name="Url"/> opens when the notification is tapped; the same <paramref name="Tag"/> replaces an older one.</summary>
public sealed record PushMessage(string Title, string Body, string Url, string? Tag = null)
{
    public static string Money(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture) + " ₪";
}

public interface IPushNotifier
{
    /// <summary>
    /// Queues a notification for every device subscribed to <paramref name="topic"/> whose user holds any of the permissions.
    /// Call it after the transaction commits; sending happens in the background and never fails the request.
    /// </summary>
    void Notify(PushTopics topic, PushMessage message, params string[] anyOfPermissions);

    /// <summary>Queues a notification for all devices of one user (used by «send a test»).</summary>
    void NotifyUser(int userId, PushMessage message);
}

public sealed record PushTopicsDto(bool Transfers, bool Stock, bool Sales)
{
    public PushTopics ToFlags() =>
        (Transfers ? PushTopics.Transfers : 0) | (Stock ? PushTopics.Stock : 0) | (Sales ? PushTopics.Sales : 0);

    public static PushTopicsDto From(PushTopics t) =>
        new(t.HasFlag(PushTopics.Transfers), t.HasFlag(PushTopics.Stock), t.HasFlag(PushTopics.Sales));
}

/// <summary><see cref="Topics"/> is null when this device is not subscribed (or belongs to someone else).</summary>
public sealed record PushStatusDto(string PublicKey, PushTopicsDto? Topics);

public sealed record SavePushSubscriptionRequest(string Endpoint, string P256dh, string Auth, PushTopicsDto Topics, string? Device);

public interface IPushSubscriptionService
{
    Task<PushStatusDto> GetStatusAsync(string? endpoint, CancellationToken ct = default);
    Task SaveAsync(SavePushSubscriptionRequest request, CancellationToken ct = default);
    Task DeleteAsync(string endpoint, CancellationToken ct = default);
    Task SendTestAsync(CancellationToken ct = default);
}

public sealed class SavePushSubscriptionRequestValidator : AbstractValidator<SavePushSubscriptionRequest>
{
    public SavePushSubscriptionRequestValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(800)
            .Must(e => Uri.TryCreate(e, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps).WithMessage("عنوان الاشتراك غير صالح");
        RuleFor(x => x.P256dh).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Auth).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Topics).NotNull();
        RuleFor(x => x.Device).MaximumLength(200);
    }
}
