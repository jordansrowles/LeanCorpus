namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// Why a flush or merge wrote a Float32 vector field without the vector graph that
/// <c>IndexWriterConfig.BuildHnswOnFlush</c> requested (ADR052). The field is searched exactly.
/// </summary>
public enum VectorGraphBuildSkipReason
{
    /// <summary>Level-zero adjacency would exceed the maximum array length.</summary>
    TooLarge = 0,

    /// <summary>Reachability repair could not reach every vector from the entry point.</summary>
    Unreachable = 1,
}
