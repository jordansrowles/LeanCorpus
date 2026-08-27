namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Describes how facet buckets for one field should be returned.</summary>
public sealed class FacetRequest
{
    /// <summary>
    /// Initialises a new <see cref="FacetRequest"/>.
    /// </summary>
    /// <param name="field">The DocValues field to facet.</param>
    /// <param name="offset">The number of ordered buckets to skip.</param>
    /// <param name="limit">The maximum number of buckets to return. Zero returns no buckets.</param>
    /// <param name="order">The bucket ordering.</param>
    /// <param name="includeMissing">Whether matching documents without a value should be counted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="field"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when paging values or <paramref name="order"/> are invalid.</exception>
    public FacetRequest(
        string field,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.CountDescending,
        bool includeMissing = false)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (!Enum.IsDefined(order))
            throw new ArgumentOutOfRangeException(nameof(order));

        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    /// <summary>Gets the DocValues field to facet.</summary>
    public string Field { get; }

    /// <summary>Gets the number of ordered buckets to skip.</summary>
    public int Offset { get; }

    /// <summary>Gets the maximum number of buckets to return. Zero means no buckets.</summary>
    public int Limit { get; }

    /// <summary>Gets the ordering applied before paging.</summary>
    public FacetBucketOrder Order { get; }

    /// <summary>Gets whether documents without a value should be counted.</summary>
    public bool IncludeMissing { get; }
}
