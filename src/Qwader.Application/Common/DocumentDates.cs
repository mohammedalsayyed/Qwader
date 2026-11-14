using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;

namespace Qwader.Application.Common;

public static class DocumentDates
{
    /// <summary>
    /// Converts a document's local date to the stored UTC timestamp: "now" for today, local noon for a past day
    /// (safely inside that day for reports). Future dates are refused.
    /// </summary>
    public static DateTime ToUtc(DateOnly? localDate, IClock clock, string field = "date")
    {
        var today = clock.LocalToday;
        var date = localDate ?? today;
        if (date > today) throw new ValidationFailedException(field, "لا يمكن أن يكون التاريخ في المستقبل");
        if (date == today) return clock.UtcNow;
        return TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(12, 0)), clock.LocalZone);
    }

    /// <summary>
    /// As <see cref="ToUtc"/>, and never before the first day the store worked on the system (the day its first user —
    /// the system admin — was created): an expense dated 1990 would change balances of years the system never saw.
    /// </summary>
    public static async Task<DateTime> ToUtcAsync(DateOnly? localDate, IClock clock, IAppDbContext db, CancellationToken ct, string field = "date")
    {
        if (localDate is { } date && date < clock.LocalToday)
        {
            var first = await db.Users.MinAsync(u => (DateTime?)u.CreatedAt, ct);
            if (first is { } created && date < DateOnly.FromDateTime(clock.ToLocal(created)))
                throw new ValidationFailedException(field,
                    $"لا يمكن أن يكون التاريخ قبل بداية العمل على النظام ({DateOnly.FromDateTime(clock.ToLocal(created)):yyyy-MM-dd})");
        }
        return ToUtc(localDate, clock, field);
    }
}
