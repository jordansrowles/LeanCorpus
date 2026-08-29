namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>A single facet bucket: a field value, or the opt-in missing value, and its document count.</summary>
/// <param name="Value">The field value represented by this bucket.</param>
/// <param name="Count">The number of matching documents that have this field value.</param>
public readonly record struct FacetBucket(string Value, int Count)
{
    /// <summary>Gets whether this bucket represents documents without a value.</summary>
    public bool IsMissing { get; }

    private FacetBucket(string value, int count, bool isMissing)
        : this(value, count)
    {
        IsMissing = isMissing;
    }

    /// <summary>Creates a bucket representing documents without a value.</summary>
    /// <param name="count">The number of matching documents without a value.</param>
    public static FacetBucket Missing(int count) => new(string.Empty, count, isMissing: true);
}
