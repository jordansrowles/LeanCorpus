namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Requests UTC Unix-millisecond date histogram buckets.</summary>
public sealed class DateHistogramFacetRequest : IFacetRequest
{
    /// <summary>Initialises a date histogram request.</summary>
    public DateHistogramFacetRequest(
        string field,
        DateHistogramInterval interval,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.ValueAscending,
        bool includeMissing = false)
    {
        RangeFacetValidation.ValidateRequest(field, offset, limit, order);
        Field = field;
        Interval = interval ?? throw new ArgumentNullException(nameof(interval));
        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    /// <inheritdoc/>
    public string Field { get; }

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
