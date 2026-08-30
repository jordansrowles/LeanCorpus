namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Requests explicitly defined double-precision numeric facet ranges.</summary>
public sealed class NumericRangeFacetRequest : IFacetRequest
{
    /// <summary>Initialises a numeric range facet request.</summary>
    /// <param name="field">The numeric DocValues field to facet.</param>
    /// <param name="ranges">The non-empty set of uniquely labelled ranges.</param>
    /// <param name="offset">The number of ordered buckets to skip.</param>
    /// <param name="limit">The maximum number of buckets to return.</param>
    /// <param name="order">The bucket ordering.</param>
    /// <param name="includeMissing">Whether documents without a numeric value are counted.</param>
    public NumericRangeFacetRequest(
        string field,
        IReadOnlyList<NumericRange> ranges,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.CountDescending,
        bool includeMissing = false)
    {
        RangeFacetValidation.ValidateRequest(field, offset, limit, order);
        Field = field;
        Ranges = Array.AsReadOnly(RangeFacetValidation.CopyRanges(ranges, nameof(ranges), range => range.Label));
        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the explicitly defined numeric ranges.</summary>
    public IReadOnlyList<NumericRange> Ranges { get; }

    /// <inheritdoc/>
    public int Offset { get; }

    /// <inheritdoc/>
    public int Limit { get; }

    /// <inheritdoc/>
    public FacetBucketOrder Order { get; }

    /// <inheritdoc/>
    public bool IncludeMissing { get; }
}

/// <summary>Requests explicitly defined exact Int64 numeric facet ranges.</summary>
public sealed class Int64RangeFacetRequest : IFacetRequest
{
    /// <summary>Initialises an Int64 range facet request.</summary>
    /// <param name="field">The Int64 DocValues field to facet.</param>
    /// <param name="ranges">The non-empty set of uniquely labelled ranges.</param>
    /// <param name="offset">The number of ordered buckets to skip.</param>
    /// <param name="limit">The maximum number of buckets to return.</param>
    /// <param name="order">The bucket ordering.</param>
    /// <param name="includeMissing">Whether documents without an Int64 value are counted.</param>
    public Int64RangeFacetRequest(
        string field,
        IReadOnlyList<Int64Range> ranges,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.CountDescending,
        bool includeMissing = false)
    {
        RangeFacetValidation.ValidateRequest(field, offset, limit, order);
        Field = field;
        Ranges = Array.AsReadOnly(RangeFacetValidation.CopyRanges(ranges, nameof(ranges), range => range.Label));
        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the explicitly defined Int64 ranges.</summary>
    public IReadOnlyList<Int64Range> Ranges { get; }

    /// <inheritdoc/>
    public int Offset { get; }

    /// <inheritdoc/>
    public int Limit { get; }

    /// <inheritdoc/>
    public FacetBucketOrder Order { get; }

    /// <inheritdoc/>
    public bool IncludeMissing { get; }
}
