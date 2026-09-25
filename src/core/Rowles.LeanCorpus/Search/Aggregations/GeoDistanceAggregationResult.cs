using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Result of a Geo distance aggregation.</summary>
public sealed class GeoDistanceAggregationResult : ISearchAggregationResult
{
    internal GeoDistanceAggregationResult(string name, string field, IReadOnlyList<GeoDistanceBucket> buckets)
    {
        Name = name;
        Field = field;
        Buckets = buckets;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the distance buckets in request order.</summary>
    public IReadOnlyList<GeoDistanceBucket> Buckets { get; }
}

