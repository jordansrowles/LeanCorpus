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

