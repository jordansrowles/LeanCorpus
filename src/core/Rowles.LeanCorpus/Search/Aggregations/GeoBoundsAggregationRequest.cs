using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Requests aggregate Geo bounds.</summary>
public sealed class GeoBoundsAggregationRequest : ISearchAggregationRequest
{
    /// <summary>Creates a Geo bounds aggregation request.</summary>
    public GeoBoundsAggregationRequest(string name, string field, bool wrapLongitude = true)
    {
        Name = SpatialAggregationRequestValidation.ValidateName(name, nameof(name));
        Field = FieldNameValidator.Validate(field, nameof(field));
        WrapLongitude = wrapLongitude;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Field { get; }

    /// <summary>Gets whether the result should use the minimum circular longitude envelope.</summary>
    public bool WrapLongitude { get; }
}

