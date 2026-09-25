using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Requests a dimension-weighted Geo centroid.</summary>
public sealed class GeoCentroidAggregationRequest : ISearchAggregationRequest
{
    /// <summary>Creates a Geo centroid aggregation request.</summary>
    public GeoCentroidAggregationRequest(string name, string field)
    {
        Name = SpatialAggregationRequestValidation.ValidateName(name, nameof(name));
        Field = FieldNameValidator.Validate(field, nameof(field));
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Field { get; }
}

