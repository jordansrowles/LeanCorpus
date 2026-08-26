using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>Represents one typed sort value at a search-after boundary.</summary>
public readonly record struct SearchAfterValue(
    SortFieldType Type,
    double NumericValue,
    long Int64Value,
    string? StringValue)
{
    /// <summary>Creates a score or numeric boundary value.</summary>
    public static SearchAfterValue FromNumeric(SortFieldType type, double value) => new(type, value, 0, null);

    /// <summary>Creates an integer or document-ID boundary value.</summary>
    public static SearchAfterValue FromInt64(SortFieldType type, long value) => new(type, 0, value, null);

    /// <summary>Creates a string boundary value.</summary>
    public static SearchAfterValue FromString(string value) => new(SortFieldType.String, 0, 0, value);
}
