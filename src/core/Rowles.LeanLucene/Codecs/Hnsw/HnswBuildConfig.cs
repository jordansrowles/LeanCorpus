namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Build-time configuration for HNSW vector graphs.
/// </summary>
public sealed class HnswBuildConfig
{
    /// <summary>Maximum neighbours per node on layers above zero. Default 16.</summary>
    public int M { get; init; } = 16;

    /// <summary>Candidate set size during graph construction. Default 100.</summary>
    public int EfConstruction { get; init; } = 100;

    /// <summary>
    /// Maximum neighbours on layer zero. Defaults to <c>2 * M</c> when zero.
    /// </summary>
    public int M0 { get; init; }

    /// <summary>Effective layer-zero degree.</summary>
    internal int EffectiveM0 => M0 > 0 ? M0 : 2 * M;
}
