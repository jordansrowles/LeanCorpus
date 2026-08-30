namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Identifies a UTC calendar interval for a date histogram.</summary>
public enum DateHistogramCalendarInterval
{
    /// <summary>Calendar day beginning at midnight UTC.</summary>
    Day,
    /// <summary>ISO-style week beginning on Monday at midnight UTC.</summary>
    Week,
    /// <summary>Calendar month beginning at midnight UTC on its first day.</summary>
    Month,
    /// <summary>Calendar quarter beginning at midnight UTC on its first day.</summary>
    Quarter,
    /// <summary>Calendar year beginning at midnight UTC on 1 January.</summary>
    Year
}

/// <summary>Defines either a fixed elapsed duration or a UTC calendar interval for date histograms.</summary>
public sealed class DateHistogramInterval
{
    private DateHistogramInterval(TimeSpan? fixedInterval, DateHistogramCalendarInterval? calendarInterval)
    {
        FixedInterval = fixedInterval;
        CalendarInterval = calendarInterval;
    }

    /// <summary>Creates a fixed elapsed interval with millisecond precision.</summary>
    public static DateHistogramInterval Fixed(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "A fixed date histogram interval must be positive.");
        if (interval.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentException("A fixed date histogram interval must be an exact number of milliseconds.", nameof(interval));
        return new DateHistogramInterval(interval, null);
    }

    /// <summary>Creates a UTC calendar interval.</summary>
    public static DateHistogramInterval Calendar(DateHistogramCalendarInterval interval)
    {
        if (!Enum.IsDefined(interval))
            throw new ArgumentOutOfRangeException(nameof(interval));
        return new DateHistogramInterval(null, interval);
    }

    /// <summary>Gets a fixed one-minute interval.</summary>
    public static DateHistogramInterval Minute { get; } = Fixed(TimeSpan.FromMinutes(1));

    /// <summary>Gets a fixed one-hour interval.</summary>
    public static DateHistogramInterval Hour { get; } = Fixed(TimeSpan.FromHours(1));

    /// <summary>Gets the fixed elapsed interval, when this is a fixed interval.</summary>
    public TimeSpan? FixedInterval { get; }

    /// <summary>Gets the UTC calendar interval, when this is a calendar interval.</summary>
    public DateHistogramCalendarInterval? CalendarInterval { get; }

    /// <summary>Gets whether this interval uses calendar arithmetic.</summary>
    public bool IsCalendar => CalendarInterval is not null;

    internal (long StartUnixMilliseconds, long EndUnixMilliseconds) GetBucket(long unixMilliseconds)
    {
        if (FixedInterval is { } fixedInterval)
        {
            long width = checked(fixedInterval.Ticks / TimeSpan.TicksPerMillisecond);
            long quotient = unixMilliseconds / width;
            long remainder = unixMilliseconds % width;
            if (remainder < 0)
                quotient--;
            long fixedStart = checked(quotient * width);
            return (fixedStart, checked(fixedStart + width));
        }

        var instant = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        DateTimeOffset start = CalendarInterval switch
        {
            DateHistogramCalendarInterval.Day => new DateTimeOffset(instant.Year, instant.Month, instant.Day, 0, 0, 0, TimeSpan.Zero),
            DateHistogramCalendarInterval.Week => StartOfWeek(instant),
            DateHistogramCalendarInterval.Month => new DateTimeOffset(instant.Year, instant.Month, 1, 0, 0, 0, TimeSpan.Zero),
            DateHistogramCalendarInterval.Quarter => new DateTimeOffset(instant.Year, ((instant.Month - 1) / 3 * 3) + 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateHistogramCalendarInterval.Year => new DateTimeOffset(instant.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ => throw new InvalidOperationException("A date histogram interval must be fixed or calendar-based.")
        };
        DateTimeOffset end = CalendarInterval switch
        {
            DateHistogramCalendarInterval.Day => start.AddDays(1),
            DateHistogramCalendarInterval.Week => start.AddDays(7),
            DateHistogramCalendarInterval.Month => start.AddMonths(1),
            DateHistogramCalendarInterval.Quarter => start.AddMonths(3),
            DateHistogramCalendarInterval.Year => start.AddYears(1),
            _ => throw new InvalidOperationException("A date histogram interval must be fixed or calendar-based.")
        };
        return (start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds());
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset instant)
    {
        int daysSinceMonday = ((int)instant.DayOfWeek + 6) % 7;
        return new DateTimeOffset(instant.Year, instant.Month, instant.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-daysSinceMonday);
    }
}
