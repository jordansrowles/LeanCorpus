using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>Codec-owned options for traversing an HNSW graph.</summary>
internal sealed class HnswTraversalOptions
{
    internal int Ef { get; init; } = 10;

    internal IBitSet? AllowList { get; init; }

    internal IBitSet? PostFilterMask { get; init; }

    internal int TopK { get; init; }

    internal int MaxPostFilterRetries { get; init; } = 3;
}
