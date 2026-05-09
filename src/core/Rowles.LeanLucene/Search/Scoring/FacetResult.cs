namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>The facet result for one field: the field name and its value-count buckets.</summary>
public sealed class FacetResult
{
    /// <summary>Gets the name of the field these facet counts are for.</summary>
    public string FieldName { get; }

    /// <summary>Gets the value-count buckets, sorted by count descending.</summary>
    public IReadOnlyList<FacetBucket> Buckets { get; }

    /// <summary>Initialises a new <see cref="FacetResult"/> with the given field name and buckets.</summary>
    /// <param name="fieldName">The field that was faceted.</param>
    /// <param name="buckets">The accumulated value-count pairs, sorted by count descending.</param>
    public FacetResult(string fieldName, IReadOnlyList<FacetBucket> buckets)
    {
        FieldName = fieldName;
        Buckets = buckets;
    }
}
