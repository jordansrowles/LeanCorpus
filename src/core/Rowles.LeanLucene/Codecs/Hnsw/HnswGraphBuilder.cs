using Rowles.LeanLucene.Codecs.Vectors;
namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Bulk constructor for <see cref="HnswGraph"/>. Performs a deterministic shuffle
/// of insertion order when a seed is supplied so that builds are reproducible.
/// </summary>
internal static class HnswGraphBuilder
{
    /// <summary>
    /// Builds and freezes an HNSW graph over the supplied vectors. Vectors are inserted in
    /// shuffled order to avoid bias from the natural insertion sequence.
    /// </summary>
    /// <param name="vectorSource">Random-access source of pre-normalised vectors.</param>
    /// <param name="docIds">Document identifiers to insert. Each must resolve through <paramref name="vectorSource"/>.</param>
    /// <param name="config">Build parameters.</param>
    /// <param name="seed">RNG seed; when null, a random seed is generated and persisted onto the graph.</param>
    public static HnswGraph Build(
        IVectorSource vectorSource,
        IReadOnlyList<int> docIds,
        HnswBuildConfig config,
        long? seed = null)
    {
        ArgumentNullException.ThrowIfNull(vectorSource);
        ArgumentNullException.ThrowIfNull(docIds);
        ArgumentNullException.ThrowIfNull(config);

        long effectiveSeed = seed ?? GenerateSeed();
        var graph = new HnswGraph(vectorSource, config, effectiveSeed);

        // Deterministic Fisher-Yates shuffle keyed on the seed.
        var order = docIds.ToArray();
        var rng = new Random(unchecked((int)(effectiveSeed ^ 0x5A5A5A5AL)));
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        foreach (var docId in order)
            graph.Insert(docId);

        graph.Freeze();
        return graph;
    }

    private static long GenerateSeed()
    {
        Span<byte> buf = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return BitConverter.ToInt64(buf);
    }
}
