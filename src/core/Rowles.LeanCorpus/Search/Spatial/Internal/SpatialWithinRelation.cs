namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>Describes whether a prepared query is a candidate within one indexed value.</summary>
internal enum SpatialWithinRelation : byte
{
    Disjoint,
    Candidate,
    NotWithin,
}
