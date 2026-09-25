using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Result of a dimension-weighted Geo centroid aggregation.</summary>
public sealed class GeoCentroidAggregationResult : ISearchAggregationResult
{
    internal GeoCentroidAggregationResult(
        string name,
        string field,
        GeoPoint? centroid,
        SpatialDimension? dimension,
        long contributingDocumentCount)
    {
        Name = name;
        Field = field;
        Centroid = centroid;
        Dimension = dimension;
        ContributingDocumentCount = contributingDocumentCount;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets the resulting centroid, or <see langword="null"/> when no values contributed.</summary>
    public GeoPoint? Centroid { get; }

    /// <summary>Gets the highest contributing spatial dimension, or <see langword="null"/> when empty.</summary>
    public SpatialDimension? Dimension { get; }

    /// <summary>Gets the matched-document count contributing to the selected dimension.</summary>
    public long ContributingDocumentCount { get; }
}

