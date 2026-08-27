using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Helpers;

public static class TimeZoneHelper
{
    public static (DateTime UtcStart, DateTime UtcEnd) GetUserTodayUtcRange(
        User? user, string? requestedTimezoneId = null)
    {
        var tz = ResolveTimeZone(user, requestedTimezoneId);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        return GetUtcRangeForLocalDate(localToday, tz);
    }

    public static (DateTime UtcStart, DateTime UtcEnd) GetUtcRangeForLocalDate(
        User? user, DateOnly localDate, string? requestedTimezoneId = null)
        => GetUtcRangeForLocalDate(localDate, ResolveTimeZone(user, requestedTimezoneId));

    public static (DateTime UtcStart, DateTime UtcEnd) GetUtcRangeForLocalDateRange(
        User? user, DateOnly from, DateOnly to, string? requestedTimezoneId = null)
    {
        var tz = ResolveTimeZone(user, requestedTimezoneId);
        var localStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, tz),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, tz).AddTicks(-1));
    }

    public static (DateTime UtcStart, DateTime UtcEnd) GetUtcRangeForFixedOffset(
        DateOnly localDate, int tzOffsetMinutes)
    {
        var offset = TimeSpan.FromMinutes(-tzOffsetMinutes);
        var localStart = localDate.ToDateTime(TimeOnly.MinValue);
        var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (localStart - offset, localEnd - offset - TimeSpan.FromTicks(1));
    }

    public static (DateTime UtcStart, DateTime UtcEnd) GetUtcRangeForFixedOffset(
        DateOnly from, DateOnly to, int tzOffsetMinutes)
    {
        var offset = TimeSpan.FromMinutes(-tzOffsetMinutes);
        var localStart = from.ToDateTime(TimeOnly.MinValue);
        var localEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (localStart - offset, localEnd - offset - TimeSpan.FromTicks(1));
    }

    public static (DateTime UtcStart, DateTime UtcEnd) GetUtcRangeForLocalDate(
        DateOnly localDate, TimeZoneInfo timeZone)
    {
        var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone).AddTicks(-1);
        return (utcStart, utcEnd);
    }

    /// <summary>Normalizes a request timestamp to a UTC instant. Unspecified values are treated as UTC.</summary>
    public static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static TimeZoneInfo ResolveTimeZone(User? user, string? requestedTimezoneId)
    {
        foreach (var id in new[] { requestedTimezoneId, user?.TimezoneId })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }

}
