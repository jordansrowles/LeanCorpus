using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// Selects the runtime encoding used to map a CLR value into a <see cref="NumericField"/> double payload.
/// </summary>
/// <remarks>
/// Generated mappers require an explicit encoding for any type where the default
/// <see cref="double"/> projection would be lossy or surprising, such as <see cref="DateTimeOffset"/>,
/// <see cref="DateOnly"/>, <see cref="TimeOnly"/>, and <see cref="decimal"/>.
/// </remarks>
public enum LeanNumericEncoding
{
    /// <summary>No encoding. The value is projected directly to <see cref="double"/>.</summary>
    None,

    /// <summary>Encodes <see cref="DateTimeOffset"/> as Unix milliseconds since the epoch.</summary>
    UnixMilliseconds,

    /// <summary>Encodes <see cref="DateTimeOffset"/> as Unix seconds since the epoch.</summary>
    UnixSeconds,

    /// <summary>Encodes <see cref="DateTimeOffset"/> as UTC tick count.</summary>
    UtcTicks,

    /// <summary>Encodes <see cref="DateOnly"/> as <see cref="DateOnly.DayNumber"/>.</summary>
    DateOnlyDayNumber,

    /// <summary>Encodes <see cref="TimeOnly"/> as <see cref="TimeOnly.Ticks"/>.</summary>
    TimeOnlyTicks,

    /// <summary>
    /// Encodes <see cref="decimal"/> losslessly as an invariant-culture string in a
    /// stored-only field rather than a numeric field. Sorting and range queries are not supported.
    /// </summary>
    DecimalAsString,
}
