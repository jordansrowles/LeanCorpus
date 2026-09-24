namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Identifies the coordinate family and spatial encoding stored by a segment field.</summary>
public enum SpatialFieldKind : byte
{
    /// <summary>Geographic point coordinates.</summary>
    GeoPoint = 1,

    /// <summary>Cartesian point coordinates.</summary>
    XYPoint = 2,

    /// <summary>Geographic shape primitives.</summary>
    GeoShape = 3,

    /// <summary>Cartesian shape primitives.</summary>
    XYShape = 4,
}
