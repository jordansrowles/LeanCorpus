namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Requests explicitly defined UTC Unix-millisecond date facet ranges.</summary>
public sealed class DateRangeFacetRequest : IFacetRequest
{
    private readonly IReadOnlyList<Int64Range> _encodedRanges;

    /// <summary>Initialises a date range facet request.</summary>
    /// <param name="field">The Int64 DocValues field containing UTC Unix milliseconds.</param>
    /// <param name="ranges">The non-empty set of uniquely labelled date ranges.</param>
    /// <param name="offset">The number of ordered buckets to skip.</param>
    /// <param name="limit">The maximum number of buckets to return.</param>
    /// <param name="order">The bucket ordering.</param>
    /// <param name="includeMissing">Whether documents without a date value are counted.</param>
    public DateRangeFacetRequest(
        string field,
        IReadOnlyList<DateRange> ranges,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.CountDescending,
        bool includeMissing = false)
    {
        RangeFacetValidation.ValidateRequest(field, offset, limit, order);
        Field = field;
        Ranges = Array.AsReadOnly(RangeFacetValidation.CopyRanges(ranges, nameof(ranges), range => range.Label));
        var encodedRanges = new Int64Range[Ranges.Count];
        for (int i = 0; i < Ranges.Count; i++)
            encodedRanges[i] = Ranges[i].EncodedRange;
        _encodedRanges = Array.AsReadOnly(encodedRanges);
        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the date ranges expressed as absolute instants.</summary>
    public IReadOnlyList<DateRange> Ranges { get; }

    /// <inheritdoc/>
    public int Offset { get; }

    /// <inheritdoc/>
    public int Limit { get; }

    /// <inheritdoc/>
    public FacetBucketOrder Order { get; }

    /// <inheritdoc/>
    public bool IncludeMissing { get; }

    internal IReadOnlyList<Int64Range> EncodedRanges => _encodedRanges;
}
