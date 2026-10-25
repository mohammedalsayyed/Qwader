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
}
