using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Result of a Geo bounds aggregation.</summary>
public sealed class GeoBoundsAggregationResult : ISearchAggregationResult
{
    internal GeoBoundsAggregationResult(string name, string field, GeoRectangle? bounds, long contributingDocumentCount)
    {
        Name = name;
        Field = field;
        Bounds = bounds;
        ContributingDocumentCount = contributingDocumentCount;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the aggregate bounds, or <see langword="null"/> when no values contributed.</summary>
    public GeoRectangle? Bounds { get; }

    /// <summary>Gets the number of matched documents that contributed point or shape values.</summary>
    public long ContributingDocumentCount { get; }
}
