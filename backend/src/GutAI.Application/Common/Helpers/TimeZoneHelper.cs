using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Helpers;

public static class TimeZoneHelper
{
    public static (DateTime UtcStart, DateTime UtcEnd) GetUserTodayUtcRange(User? user)
    {
        TimeZoneInfo tz;
        try
        {
            tz = !string.IsNullOrEmpty(user?.TimezoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(user.TimezoneId)
                : TimeZoneInfo.Utc;
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.Utc;
        }

        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var localToday = nowInUserTz.Date;
        var localTomorrow = localToday.AddDays(1);

        var utcStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), tz);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTomorrow, DateTimeKind.Unspecified), tz)
            .AddTicks(-1);

        return (utcStart, utcEnd);
    }
}
