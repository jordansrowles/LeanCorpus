namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Describes one facet bucket collection request.</summary>
public interface IFacetRequest
{
    /// <summary>Gets the logical result name. Defaults to the source field.</summary>
    string Name => Field;

    /// <summary>Gets the field or dimension to facet.</summary>
    string Field { get; }

    /// <summary>Gets the number of ordered buckets to skip.</summary>
    int Offset { get; }

    /// <summary>Gets the maximum number of buckets to return.</summary>
    int Limit { get; }

    /// <summary>Gets the ordering applied before paging.</summary>
    FacetBucketOrder Order { get; }

    /// <summary>Gets whether matching documents without a value are counted.</summary>
    bool IncludeMissing { get; }
}
