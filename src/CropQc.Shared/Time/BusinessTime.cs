using System.Globalization;

namespace CropQc.Shared.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IBusinessTimeService
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset NowPacific { get; }
    DateTimeOffset ToPacific(DateTimeOffset value);
    DateTimeOffset PacificLocalToUtc(DateTime value);
    DateOnly PacificDate(DateTimeOffset value);
    UtcDayRange UtcRangeForPacificDate(DateOnly date);
    string FormatPacific(DateTimeOffset value, string format = "g", bool includeZone = true);
    string FormatPacific(DateTimeOffset? value, string format = "g", bool includeZone = true, string empty = "—");
    string FormatPacificInput(DateTimeOffset value);
    string TimeZoneAbbreviation(DateTimeOffset value);
    DateTimeOffset NextNightlyBackupUtc(DateTimeOffset? afterUtc = null);
    bool IsNightlyCandidate(DateTimeOffset value);
}

public sealed class PacificBusinessTimeService(IClock clock) : IBusinessTimeService
{
    public const string IanaTimeZoneId = "America/Los_Angeles";
    public const string WindowsTimeZoneId = "Pacific Standard Time";
    private static readonly TimeZoneInfo PacificTimeZone = ResolvePacificTimeZone();

    public DateTimeOffset UtcNow => clock.UtcNow.ToUniversalTime();
    public DateTimeOffset NowPacific => ToPacific(UtcNow);

    public DateTimeOffset ToPacific(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, PacificTimeZone);

    public DateTimeOffset PacificLocalToUtc(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (PacificTimeZone.IsInvalidTime(unspecified))
        {
            throw new ArgumentException("The selected Pacific time does not exist because of the daylight-saving transition.", nameof(value));
        }

        if (PacificTimeZone.IsAmbiguousTime(unspecified))
        {
            var offset = PacificTimeZone.GetAmbiguousTimeOffsets(unspecified).Max();
            return new DateTimeOffset(unspecified, offset).ToUniversalTime();
        }

        return new DateTimeOffset(unspecified, PacificTimeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    public DateOnly PacificDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(ToPacific(value).Date);

    public UtcDayRange UtcRangeForPacificDate(DateOnly date)
    {
        var start = PacificLocalToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
        var end = PacificLocalToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
        return new UtcDayRange(start, end);
    }

    public string FormatPacific(DateTimeOffset value, string format = "g", bool includeZone = true)
    {
        var pacific = ToPacific(value);
        var rendered = pacific.ToString(format, CultureInfo.CurrentCulture);
        return includeZone ? $"{rendered} {TimeZoneAbbreviation(value)}" : rendered;
    }

    public string FormatPacific(DateTimeOffset? value, string format = "g", bool includeZone = true, string empty = "—") =>
        value is null ? empty : FormatPacific(value.Value, format, includeZone);

    public string FormatPacificInput(DateTimeOffset value) =>
        ToPacific(value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    public string TimeZoneAbbreviation(DateTimeOffset value) =>
        PacificTimeZone.IsDaylightSavingTime(ToPacific(value).DateTime) ? "PDT" : "PST";

    public DateTimeOffset NextNightlyBackupUtc(DateTimeOffset? afterUtc = null)
    {
        var after = (afterUtc ?? UtcNow).ToUniversalTime();
        var local = ToPacific(after);
        var candidateDate = DateOnly.FromDateTime(local.Date);
        if (local.TimeOfDay >= TimeSpan.FromHours(1))
        {
            candidateDate = candidateDate.AddDays(1);
        }

        var candidate = candidateDate.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Unspecified);
        return PacificLocalToUtc(candidate);
    }

    public bool IsNightlyCandidate(DateTimeOffset value) =>
        ToPacific(value).Hour == 1;

    public static TimeZoneInfo ResolvePacificTimeZone()
    {
        foreach (var id in new[] { IanaTimeZoneId, WindowsTimeZoneId })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException("Pacific business timezone is unavailable on this host.");
    }
}
