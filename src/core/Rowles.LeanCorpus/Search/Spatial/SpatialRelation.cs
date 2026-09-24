namespace Rowles.LeanCorpus.Search.Spatial;

/// <summary>The document-level relationship required between an indexed shape and a query shape.</summary>
public enum SpatialRelation : byte
{
    /// <summary>At least one indexed value intersects the query shape.</summary>
    Intersects = 0,

    /// <summary>Every indexed primitive is within the query shape.</summary>
    Within = 1,

    /// <summary>One complete indexed field value contains the query shape.</summary>
    Contains = 2,

    /// <summary>Every indexed primitive is disjoint from the query shape.</summary>
    Disjoint = 3,
}
