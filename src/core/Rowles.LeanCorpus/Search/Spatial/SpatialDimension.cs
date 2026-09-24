namespace Rowles.LeanCorpus.Search.Spatial;

/// <summary>Identifies the highest spatial dimension represented by an aggregation result.</summary>
public enum SpatialDimension : byte
{
    /// <summary>Zero-dimensional points.</summary>
    Point = 0,

    /// <summary>One-dimensional line segments.</summary>
    Line = 1,

    /// <summary>Two-dimensional areas.</summary>
    Area = 2,
}
