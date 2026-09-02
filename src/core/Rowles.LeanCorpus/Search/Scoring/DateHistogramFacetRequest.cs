namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Requests UTC Unix-millisecond date histogram buckets.</summary>
public sealed class DateHistogramFacetRequest : IFacetRequest
{
    /// <summary>Initialises a date histogram request.</summary>
    /// <param name="field">The Int64 DocValues field containing UTC Unix milliseconds.</param>
    /// <param name="interval">The fixed elapsed or UTC calendar interval.</param>
    /// <param name="offset">The number of ordered buckets to skip.</param>
    /// <param name="limit">The maximum number of buckets to return.</param>
    /// <param name="order">The bucket ordering.</param>
    /// <param name="includeMissing">Whether matching documents without a date value are counted.</param>
    /// <param name="name">Optional logical result name. Defaults to <paramref name="field"/>.</param>
    public DateHistogramFacetRequest(
        string field,
        DateHistogramInterval interval,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.ValueAscending,
        bool includeMissing = false,
        string? name = null)
    {
        Field = RangeFacetValidation.ValidateRequest(field, offset, limit, order);
        Name = RangeFacetValidation.ValidateName(name ?? Field);
        Interval = interval ?? throw new ArgumentNullException(nameof(interval));
        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    /// <inheritdoc/>
    public string Field { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the fixed elapsed or UTC calendar interval.</summary>
    public DateHistogramInterval Interval { get; }

    /// <inheritdoc/>
    public int Offset { get; }

    /// <inheritdoc/>
    public int Limit { get; }

    /// <inheritdoc/>
    public FacetBucketOrder Order { get; }

    /// <inheritdoc/>
    public bool IncludeMissing { get; }
}
