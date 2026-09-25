using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Requests distance buckets over a geographic point field.</summary>
public sealed class GeoDistanceAggregationRequest : ISearchAggregationRequest
{
    /// <summary>Creates a Geo distance aggregation request.</summary>
    public GeoDistanceAggregationRequest(
        string name,
        string field,
        GeoPoint origin,
        IEnumerable<GeoDistanceRange> ranges)
    {
        Name = SpatialAggregationRequestValidation.ValidateName(name, nameof(name));
        Field = FieldNameValidator.Validate(field, nameof(field));
        Origin = origin;
        ArgumentNullException.ThrowIfNull(ranges);
        GeoDistanceRange[] copiedRanges = ranges.ToArray();
        foreach (GeoDistanceRange range in copiedRanges)
            range.Validate();
        Ranges = Array.AsReadOnly(copiedRanges);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the origin used to calculate each point distance.</summary>
    public GeoPoint Origin { get; }

    /// <summary>Gets the requested ranges in result order.</summary>
    public IReadOnlyList<GeoDistanceRange> Ranges { get; }
}

