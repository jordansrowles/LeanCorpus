using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>A half-open Geo distance range, measured in metres.</summary>
public readonly record struct GeoDistanceRange
{
    /// <summary>Creates a distance range with an inclusive lower and exclusive upper bound.</summary>
    /// <param name="fromMetres">Inclusive lower bound, or <see langword="null"/> for zero.</param>
    /// <param name="toMetres">Exclusive upper bound, or <see langword="null"/> for infinity.</param>
    public GeoDistanceRange(double? fromMetres, double? toMetres)
    {
        ValidateBound(fromMetres, nameof(fromMetres));
        ValidateBound(toMetres, nameof(toMetres));
        if (fromMetres.HasValue && toMetres.HasValue && fromMetres.Value >= toMetres.Value)
            throw new ArgumentException("A Geo distance range lower bound must be less than its upper bound.");

        FromMetres = fromMetres;
        ToMetres = toMetres;
    }

    /// <summary>Gets the inclusive lower bound in metres, or <see langword="null"/> for zero.</summary>
    public double? FromMetres { get; }

    /// <summary>Gets the exclusive upper bound in metres, or <see langword="null"/> for infinity.</summary>
    public double? ToMetres { get; }

    internal void Validate()
    {
        ValidateBound(FromMetres, nameof(FromMetres));
        ValidateBound(ToMetres, nameof(ToMetres));
        if (FromMetres.HasValue && ToMetres.HasValue && FromMetres.Value >= ToMetres.Value)
            throw new ArgumentException("A Geo distance range lower bound must be less than its upper bound.");
    }

    private static void ValidateBound(double? value, string parameterName)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value < 0))
            throw new ArgumentOutOfRangeException(parameterName, "Distance bounds must be finite and non-negative.");
    }
}

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

/// <summary>One Geo distance bucket.</summary>
public readonly record struct GeoDistanceBucket(double? FromMetres, double? ToMetres, long DocumentCount);

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

internal static class SpatialAggregationRequestValidation
{
    internal static string ValidateName(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Aggregation names must not be empty or whitespace.", parameterName);
        return name;
    }
}
