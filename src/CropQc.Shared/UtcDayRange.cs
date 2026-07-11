namespace CropQc.Shared;

public readonly record struct UtcDayRange(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset value) => value >= Start && value < End;

    public static UtcDayRange ForUtcDay(DateTimeOffset value)
    {
        var utcDate = value.UtcDateTime.Date;
        var start = new DateTimeOffset(utcDate, TimeSpan.Zero);
        return new UtcDayRange(start, start.AddDays(1));
    }
}
