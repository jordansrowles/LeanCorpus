namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Defines one double-precision numeric facet range.</summary>
public sealed class NumericRange
{
    /// <summary>Initialises a numeric range.</summary>
    public NumericRange(
        string label,
        double? lowerBound = null,
        double? upperBound = null,
        bool includeLower = true,
        bool includeUpper = false)
    {
        Label = RangeFacetValidation.ValidateLabel(label, nameof(label));
        ValidateBound(lowerBound, nameof(lowerBound));
        ValidateBound(upperBound, nameof(upperBound));
        RangeFacetValidation.ValidateBounds(lowerBound, upperBound, includeLower, includeUpper);

        LowerBound = lowerBound;
        UpperBound = upperBound;
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
    }

    /// <summary>Gets the bucket label.</summary>
    public string Label { get; }

    /// <summary>Gets the optional lower bound.</summary>
    public double? LowerBound { get; }

    /// <summary>Gets the optional upper bound.</summary>
    public double? UpperBound { get; }

    /// <summary>Gets whether the lower bound is included.</summary>
    public bool IncludeLower { get; }

    /// <summary>Gets whether the upper bound is included.</summary>
    public bool IncludeUpper { get; }

    internal bool Contains(double value)
    {
        if (double.IsNaN(value))
            return false;

        if (LowerBound is { } lower
            && (value < lower || (!IncludeLower && value == lower)))
            return false;

        if (UpperBound is { } upper
            && (value > upper || (!IncludeUpper && value == upper)))
            return false;

        return true;
    }

    internal bool Contains(long value)
    {
        if (LowerBound is { } lower
            && (Compare(value, lower) < 0 || (!IncludeLower && Compare(value, lower) == 0)))
            return false;

        if (UpperBound is { } upper
            && (Compare(value, upper) > 0 || (!IncludeUpper && Compare(value, upper) == 0)))
            return false;

        return true;
    }

    private static int Compare(long value, double boundary)
    {
        // Every finite double above 2^63 is greater than every Int64 value.
        if (boundary > 9_223_372_036_854_775_808d)
            return -1;
        // Every finite double below -2^63 is less than every Int64 value.
        if (boundary < -9_223_372_036_854_775_808d)
            return 1;

        // A decimal comparison keeps large Int64 values exact while retaining
        // the represented double boundary for the mixed-typed request.
        return ((decimal)value).CompareTo((decimal)boundary);
    }

    private static void ValidateBound(double? bound, string parameterName)
    {
        if (bound is { } value && (double.IsNaN(value) || double.IsInfinity(value)))
            throw new ArgumentOutOfRangeException(parameterName, "Range bounds must be finite when specified.");
    }
}

/// <summary>Defines one exact Int64 numeric facet range.</summary>
public sealed class Int64Range
{
    /// <summary>Initialises an Int64 range.</summary>
    public Int64Range(
        string label,
        long? lowerBound = null,
        long? upperBound = null,
        bool includeLower = true,
        bool includeUpper = false)
    {
        Label = RangeFacetValidation.ValidateLabel(label, nameof(label));
        RangeFacetValidation.ValidateBounds(lowerBound, upperBound, includeLower, includeUpper);

        LowerBound = lowerBound;
        UpperBound = upperBound;
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
    }

    /// <summary>Gets the bucket label.</summary>
    public string Label { get; }

    /// <summary>Gets the optional lower bound.</summary>
    public long? LowerBound { get; }

    /// <summary>Gets the optional upper bound.</summary>
    public long? UpperBound { get; }

    /// <summary>Gets whether the lower bound is included.</summary>
    public bool IncludeLower { get; }

    /// <summary>Gets whether the upper bound is included.</summary>
    public bool IncludeUpper { get; }

    internal bool Contains(long value)
    {
        if (LowerBound is { } lower
            && (value < lower || (!IncludeLower && value == lower)))
            return false;

        if (UpperBound is { } upper
            && (value > upper || (!IncludeUpper && value == upper)))
            return false;

        return true;
    }
}

internal static class RangeFacetValidation
{
    public static string ValidateLabel(string label, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(label, parameterName);
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Range labels must not be empty or whitespace.", parameterName);
        return label;
    }

    public static void ValidateBounds<T>(
        T? lowerBound,
        T? upperBound,
        bool includeLower,
        bool includeUpper)
        where T : struct, IComparable<T>
    {
        if (lowerBound is not { } lower || upperBound is not { } upper)
            return;

        int comparison = lower.CompareTo(upper);
        if (comparison > 0)
            throw new ArgumentException("A range lower bound must not be greater than its upper bound.");
        if (comparison == 0 && (!includeLower || !includeUpper))
            throw new ArgumentException("Equal range bounds must include both boundaries.");
    }

    public static T[] CopyRanges<T>(
        IReadOnlyList<T> ranges,
        string parameterName,
        Func<T, string> getLabel)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(ranges, parameterName);
        if (ranges.Count == 0)
            throw new ArgumentException("At least one range is required.", parameterName);

        var copy = new T[ranges.Count];
        var labels = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i] ?? throw new ArgumentException("Ranges must not contain null values.", parameterName);
            if (!labels.Add(getLabel(range)))
                throw new ArgumentException($"Range label '{getLabel(range)}' was supplied more than once.", parameterName);
            copy[i] = range;
        }

        return copy;
    }

    public static void ValidateRequest(
        string field,
        int offset,
        int limit,
        FacetBucketOrder order)
    {
        Document.Fields.FieldNameValidator.Validate(field, nameof(field));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (!Enum.IsDefined(order))
            throw new ArgumentOutOfRangeException(nameof(order));
    }
}
