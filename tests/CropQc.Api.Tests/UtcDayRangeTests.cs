using CropQc.Shared;

namespace CropQc.Api.Tests;

public sealed class UtcDayRangeTests
{
    [Fact]
    public void UtcDayRange_IncludesStartOfDay()
    {
        var range = UtcDayRange.ForUtcDay(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.True(range.Contains(new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void UtcDayRange_IncludesMiddleOfDay()
    {
        var range = UtcDayRange.ForUtcDay(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.True(range.Contains(new DateTimeOffset(2026, 7, 11, 12, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void UtcDayRange_IncludesFinalInstantBeforeNextDay()
    {
        var range = UtcDayRange.ForUtcDay(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.True(range.Contains(new DateTimeOffset(2026, 7, 11, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999)));
    }

    [Fact]
    public void UtcDayRange_ExcludesExactlyAtNextDayBoundary()
    {
        var range = UtcDayRange.ForUtcDay(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.False(range.Contains(new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void UtcDayRange_ExcludesRecordsOutsideSelectedDate()
    {
        var range = UtcDayRange.ForUtcDay(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.False(range.Contains(new DateTimeOffset(2026, 7, 10, 23, 59, 59, TimeSpan.Zero)));
        Assert.False(range.Contains(new DateTimeOffset(2026, 7, 12, 0, 0, 1, TimeSpan.Zero)));
    }

    [Fact]
    public void UtcDayRange_PreservesExistingUtcDaySemanticsAcrossDstTransition()
    {
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var localDuringSpringForward = new DateTime(2026, 3, 8, 3, 30, 0, DateTimeKind.Unspecified);
        var instant = TimeZoneInfo.ConvertTimeToUtc(localDuringSpringForward, losAngeles);

        var range = UtcDayRange.ForUtcDay(new DateTimeOffset(instant, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromDays(1), range.End - range.Start);
        Assert.True(range.Contains(new DateTimeOffset(instant, TimeSpan.Zero)));
    }
}
