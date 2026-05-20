using System.Globalization;

namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// AOT-safe encoding and decoding helpers used by generated document mappers
/// when projecting CLR temporal and decimal values to numeric or stored fields.
/// </summary>
public static class LeanNumericEncoders
{
    /// <summary>Encodes a <see cref="DateTimeOffset"/> as Unix milliseconds since the epoch.</summary>
    /// <param name="value">The temporal value to encode.</param>
    /// <returns>The Unix millisecond value as a <see cref="double"/>.</returns>
    public static double ToUnixMilliseconds(DateTimeOffset value)
        => value.ToUnixTimeMilliseconds();

    /// <summary>Decodes a <see cref="DateTimeOffset"/> from Unix milliseconds.</summary>
    /// <param name="value">The encoded Unix millisecond value.</param>
    /// <returns>The decoded <see cref="DateTimeOffset"/> in UTC.</returns>
    public static DateTimeOffset FromUnixMilliseconds(double value)
        => DateTimeOffset.FromUnixTimeMilliseconds(checked((long)value));

    /// <summary>Encodes a <see cref="DateTimeOffset"/> as Unix seconds since the epoch.</summary>
    /// <param name="value">The temporal value to encode.</param>
    /// <returns>The Unix second value as a <see cref="double"/>.</returns>
    public static double ToUnixSeconds(DateTimeOffset value)
        => value.ToUnixTimeSeconds();

    /// <summary>Decodes a <see cref="DateTimeOffset"/> from Unix seconds.</summary>
    /// <param name="value">The encoded Unix second value.</param>
    /// <returns>The decoded <see cref="DateTimeOffset"/> in UTC.</returns>
    public static DateTimeOffset FromUnixSeconds(double value)
        => DateTimeOffset.FromUnixTimeSeconds(checked((long)value));

    /// <summary>Encodes a <see cref="DateTimeOffset"/> as UTC ticks.</summary>
    /// <param name="value">The temporal value to encode.</param>
    /// <returns>The UTC tick count as a <see cref="double"/>.</returns>
    public static double ToUtcTicks(DateTimeOffset value)
        => value.UtcTicks;

    /// <summary>Decodes a <see cref="DateTimeOffset"/> from UTC ticks.</summary>
    /// <param name="value">The encoded UTC tick value.</param>
    /// <returns>The decoded <see cref="DateTimeOffset"/> in UTC.</returns>
    public static DateTimeOffset FromUtcTicks(double value)
        => new(checked((long)value), TimeSpan.Zero);

    /// <summary>Encodes a <see cref="DateOnly"/> as <see cref="DateOnly.DayNumber"/>.</summary>
    /// <param name="value">The date value to encode.</param>
    /// <returns>The day number as a <see cref="double"/>.</returns>
    public static double ToDayNumber(DateOnly value)
        => value.DayNumber;

    /// <summary>Decodes a <see cref="DateOnly"/> from its day number.</summary>
    /// <param name="value">The encoded day number.</param>
    /// <returns>The decoded <see cref="DateOnly"/>.</returns>
    public static DateOnly FromDayNumber(double value)
        => DateOnly.FromDayNumber(checked((int)value));

    /// <summary>Encodes a <see cref="TimeOnly"/> as <see cref="TimeOnly.Ticks"/>.</summary>
    /// <param name="value">The time-of-day value to encode.</param>
    /// <returns>The tick count as a <see cref="double"/>.</returns>
    public static double ToTimeOnlyTicks(TimeOnly value)
        => value.Ticks;

    /// <summary>Decodes a <see cref="TimeOnly"/> from its tick count.</summary>
    /// <param name="value">The encoded tick count.</param>
    /// <returns>The decoded <see cref="TimeOnly"/>.</returns>
    public static TimeOnly FromTimeOnlyTicks(double value)
        => new(checked((long)value));

    /// <summary>Formats a <see cref="decimal"/> losslessly using the invariant culture.</summary>
    /// <param name="value">The decimal value to format.</param>
    /// <returns>The round-trip string representation.</returns>
    public static string ToDecimalString(decimal value)
        => value.ToString("G", CultureInfo.InvariantCulture);

    /// <summary>Parses a <see cref="decimal"/> from an invariant-culture string.</summary>
    /// <param name="value">The string representation produced by <see cref="ToDecimalString"/>.</param>
    /// <returns>The decoded <see cref="decimal"/>.</returns>
    public static decimal FromDecimalString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
