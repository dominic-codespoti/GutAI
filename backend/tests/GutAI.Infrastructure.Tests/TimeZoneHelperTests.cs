using GutAI.Application.Common.Helpers;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class TimeZoneHelperTests
{
    [Fact]
    public void GetUtcRangeForLocalDate_UsesDaylightSavingOffset()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var (start, end) = TimeZoneHelper.GetUtcRangeForLocalDate(
            new DateOnly(2026, 8, 26),
            timezone);

        Assert.Equal(new DateTime(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(
            new DateTime(2026, 8, 27, 4, 0, 0, DateTimeKind.Utc).AddTicks(-1),
            end);
    }
}
