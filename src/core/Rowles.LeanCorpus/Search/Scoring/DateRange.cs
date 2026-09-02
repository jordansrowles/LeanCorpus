namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Defines one UTC Unix-millisecond date facet range.</summary>
public sealed class DateRange
{
    /// <summary>Initialises a date range from absolute instants.</summary>
    public DateRange(
        string label,
        DateTimeOffset? lowerBound = null,
        DateTimeOffset? upperBound = null,
        bool includeLower = true,
        bool includeUpper = false)
    {
        Label = RangeFacetValidation.ValidateLabel(label, nameof(label));
        RangeFacetValidation.ValidateBounds(lowerBound, upperBound, includeLower, includeUpper);

        LowerBound = lowerBound;
        UpperBound = upperBound;
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
        EncodedRange = new Int64Range(
            Label,
            lowerBound?.ToUnixTimeMilliseconds(),
            upperBound?.ToUnixTimeMilliseconds(),
            includeLower,
            includeUpper);
    }

    /// <summary>Gets the bucket label.</summary>
    public string Label { get; }

    /// <summary>Gets the optional lower instant.</summary>
    public DateTimeOffset? LowerBound { get; }

    /// <summary>Gets the optional upper instant.</summary>
    public DateTimeOffset? UpperBound { get; }

    /// <summary>Gets whether the lower instant is included.</summary>
    public bool IncludeLower { get; }

    /// <summary>Gets whether the upper instant is included.</summary>
    public bool IncludeUpper { get; }

    internal Int64Range EncodedRange { get; }
}
