namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>The facet result for one field: the field name and its value-count buckets.</summary>
public sealed class FacetResult
{
    /// <summary>Gets the name of the field these facet counts are for.</summary>
    public string FieldName { get; }

    /// <summary>Gets the value-count buckets, sorted by count descending.</summary>
    public IReadOnlyList<FacetBucket> Buckets { get; }

    /// <summary>Gets the number of buckets before offset and limit are applied.</summary>
    public int TotalBucketCount { get; }

    /// <summary>Gets the count of matching documents without a value, when requested.</summary>
    public int? MissingCount { get; }

    /// <summary>Initialises a new <see cref="FacetResult"/> with the given field name and buckets.</summary>
    /// <param name="fieldName">The field that was faceted.</param>
    /// <param name="buckets">The accumulated value-count pairs, sorted by count descending.</param>
    public FacetResult(string fieldName, IReadOnlyList<FacetBucket> buckets)
        : this(fieldName, buckets, buckets?.Count ?? 0)
    {
    }

    /// <summary>Initialises a new <see cref="FacetResult"/> with result metadata.</summary>
    /// <param name="fieldName">The field that was faceted.</param>
    /// <param name="buckets">The returned value-count pairs.</param>
    /// <param name="totalBucketCount">The number of buckets before paging.</param>
    /// <param name="missingCount">The count of documents without a value, when requested.</param>
    public FacetResult(string fieldName, IReadOnlyList<FacetBucket> buckets, int totalBucketCount, int? missingCount = null)
    {
        FieldName = fieldName;
        Buckets = buckets;
        TotalBucketCount = totalBucketCount;
        MissingCount = missingCount;
    }
}
