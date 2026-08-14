using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Reads a <see cref="HnswGraph"/> previously written by <see cref="HnswWriter"/>.
/// Normal search opens retain a bounded body input and materialise only node identifiers
/// and neighbour offsets. Merge remapping materialises adjacency because it rewrites it.
/// </summary>
internal static class HnswReader
{
    public static HnswGraph Read(string filePath, IVectorSource vectorSource)
        => Read(filePath, vectorSource, expectedNormalised: null, docIdRemap: null);

    public static HnswGraph Read(string filePath, IVectorSource vectorSource, bool? expectedNormalised)
        => Read(filePath, vectorSource, expectedNormalised, docIdRemap: null);

    /// <summary>
    /// Reads a graph and optionally remaps every doc-id (entry point, node keys, neighbour ids)
    /// through <paramref name="docIdRemap"/>. Used by incremental merge to translate from a
    /// source segment's local ids into the merged segment's id space. Any node whose id is
    /// missing from the map is dropped; back-edges to dropped nodes are removed.
    /// </summary>
    public static HnswGraph Read(
        string filePath,
        IVectorSource vectorSource,
        bool? expectedNormalised,
        IReadOnlyDictionary<int, int>? docIdRemap)
        => Read(new IndexInput(filePath), vectorSource, expectedNormalised, docIdRemap);

    /// <summary>
    /// Takes ownership of <paramref name="input"/>. A separately owned bounded body slice is
    /// retained by the returned graph only when no remapping is requested.
    /// </summary>
    internal static HnswGraph Read(
        IndexInput input,
        IVectorSource vectorSource,
        bool? expectedNormalised,
        IReadOnlyDictionary<int, int>? docIdRemap)
    {
        ArgumentNullException.ThrowIfNull(input);
        IndexInput? body = null;
        try
        {
            using (input)
            using (var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Hnsw))
            {
                body = frame.OpenBodyInput();
            }

            string filePath = input.FilePath ?? "compound member";
            long position = 0;
            int dimension = body.ReadInt32(ref position);
            bool normalised = body.ReadByte(ref position) != 0;
            if (expectedNormalised is bool expected && expected != normalised)
                throw new InvalidDataException(
                    $"HNSW file at '{filePath}' declares Normalised={normalised} but the segment field declares Normalised={expected}.");
            if (vectorSource.Dimension != dimension)
                throw new InvalidDataException(
                    $"HNSW file dimension {dimension} does not match vector source dimension {vectorSource.Dimension}.");

            int m = body.ReadInt32(ref position);
            int m0 = body.ReadInt32(ref position);
            int efConstruction = body.ReadInt32(ref position);
            long seed = body.ReadInt64(ref position);
            int entryPoint = body.ReadInt32(ref position);
            int maxLevel = body.ReadInt32(ref position);
            int nodeCount = body.ReadInt32(ref position);
            int levelCount = body.ReadInt32(ref position);

            if (maxLevel < 0)
                throw new InvalidDataException($"HNSW file at '{filePath}' has negative maxLevel ({maxLevel}).");
            if (nodeCount < 0)
                throw new InvalidDataException($"HNSW file at '{filePath}' has negative nodeCount ({nodeCount}).");
            if (levelCount < 0 || levelCount > maxLevel + 1)
                throw new InvalidDataException(
                    $"HNSW file at '{filePath}' has levelCount {levelCount} but maxLevel is {maxLevel} (valid range 0..{maxLevel + 1}).");

            var config = new HnswBuildConfig { M = m, M0 = m0, EfConstruction = efConstruction };
            if (docIdRemap is null)
            {
                var levels = CreateMappedLevels(body, filePath, nodeCount, levelCount, ref position);
                EnsureFullyConsumed(body, filePath, position);
                var graph = HnswGraph.FromMapped(
                    vectorSource, config, seed, levels, entryPoint, maxLevel, nodeCount, body);
                body = null;
                return graph;
            }

            var remappedLevels = ReadRemappedLevels(
                body, filePath, nodeCount, levelCount, docIdRemap, ref position);
            EnsureFullyConsumed(body, filePath, position);
            body.Dispose();
            body = null;

            entryPoint = docIdRemap.TryGetValue(entryPoint, out int newEntry) ? newEntry : -1;
            while (maxLevel >= 0 && remappedLevels[maxLevel].Count == 0)
                maxLevel--;
            if (remappedLevels.Count > maxLevel + 1)
                remappedLevels.RemoveRange(maxLevel + 1, remappedLevels.Count - maxLevel - 1);
            nodeCount = remappedLevels.Count > 0 ? remappedLevels[0].Count : 0;
            if (entryPoint == -1 && maxLevel >= 0)
            {
                var topIds = remappedLevels[maxLevel].NodeIds;
                if (topIds.Length > 0)
                    entryPoint = topIds[0];
            }

            return HnswGraph.FromFrozen(
                vectorSource, config, seed, remappedLevels, entryPoint, maxLevel, nodeCount);
        }
        catch
        {
            body?.Dispose();
            input.Dispose();
            throw;
        }
    }

    private static List<HnswGraph.ReadOnlyLevel> CreateMappedLevels(
        IndexInput body,
        string filePath,
        int nodeCount,
        int levelCount,
        ref long position)
    {
        var levels = new List<HnswGraph.ReadOnlyLevel>(levelCount);
        for (int i = 0; i < levelCount; i++)
            levels.Add(null!);

        for (int level = levelCount - 1; level >= 0; level--)
        {
            int nodes = ReadNodeCount(body, filePath, nodeCount, level, ref position);
            var locations = new NodeLocation[nodes];
            for (int node = 0; node < nodes; node++)
            {
                int docId = body.ReadInt32(ref position);
                int neighbourCount = body.ReadInt32(ref position);
                ValidateNeighbourCount(filePath, nodeCount, level, docId, neighbourCount);
                long neighbourOffset = position;
                position = checked(position + checked((long)neighbourCount * sizeof(int)));
                if (position > body.Length)
                    throw new EndOfStreamException($"HNSW file at '{filePath}' is truncated in level {level} adjacency.");
                locations[node] = new NodeLocation(docId, neighbourOffset, neighbourCount);
            }

            Array.Sort(locations, static (left, right) => left.DocId.CompareTo(right.DocId));
            var docIds = new int[nodes];
            var offsets = new long[nodes];
            var counts = new int[nodes];
            for (int node = 0; node < nodes; node++)
            {
                if (node > 0 && locations[node - 1].DocId == locations[node].DocId)
                    throw new InvalidDataException(
                        $"HNSW file at '{filePath}' level {level} contains duplicate node {locations[node].DocId}.");
                docIds[node] = locations[node].DocId;
                offsets[node] = locations[node].NeighbourOffset;
                counts[node] = locations[node].NeighbourCount;
            }
            levels[level] = new HnswGraph.MappedLevel(body, docIds, offsets, counts);
        }

        return levels;
    }

    private static List<HnswGraph.FrozenLevel> ReadRemappedLevels(
        IndexInput body,
        string filePath,
        int nodeCount,
        int levelCount,
        IReadOnlyDictionary<int, int> docIdRemap,
        ref long position)
    {
        var levels = new List<HnswGraph.FrozenLevel>(levelCount);
        for (int i = 0; i < levelCount; i++)
            levels.Add(null!);

        for (int level = levelCount - 1; level >= 0; level--)
        {
            int nodes = ReadNodeCount(body, filePath, nodeCount, level, ref position);
            var docIds = new List<int>(nodes);
            var neighbourLists = new List<int[]>(nodes);
            for (int node = 0; node < nodes; node++)
            {
                int docId = body.ReadInt32(ref position);
                int neighbourCount = body.ReadInt32(ref position);
                ValidateNeighbourCount(filePath, nodeCount, level, docId, neighbourCount);
                var neighbours = new int[neighbourCount];
                body.ReadInt32Array(neighbours, neighbourCount, ref position);

                if (!docIdRemap.TryGetValue(docId, out int newDocId))
                    continue;

                var remapped = new List<int>(neighbours.Length);
                foreach (int neighbour in neighbours)
                {
                    if (docIdRemap.TryGetValue(neighbour, out int newNeighbour))
                        remapped.Add(newNeighbour);
                }
                docIds.Add(newDocId);
                neighbourLists.Add(remapped.ToArray());
            }

            var sortedIds = docIds.ToArray();
            var sortedNeighbours = neighbourLists.ToArray();
            Array.Sort(sortedIds, sortedNeighbours);
            levels[level] = new HnswGraph.FrozenLevel(sortedIds, sortedNeighbours);
        }

        return levels;
    }

    private static int ReadNodeCount(
        IndexInput body,
        string filePath,
        int nodeCount,
        int level,
        ref long position)
    {
        int nodes = body.ReadInt32(ref position);
        if (nodes < 0 || nodes > nodeCount)
            throw new InvalidDataException(
                $"HNSW file at '{filePath}' level {level} declares {nodes} nodes (valid range 0..{nodeCount}).");
        return nodes;
    }

    private static void ValidateNeighbourCount(
        string filePath,
        int nodeCount,
        int level,
        int docId,
        int neighbourCount)
    {
        if (neighbourCount < 0 || neighbourCount > nodeCount)
            throw new InvalidDataException(
                $"HNSW file at '{filePath}' node {docId} at level {level} has neighCount {neighbourCount} (valid range 0..{nodeCount}).");
    }

    private static void EnsureFullyConsumed(IndexInput body, string filePath, long position)
    {
        if (position != body.Length)
            throw new InvalidDataException(
                $"HNSW file at '{filePath}' contains {body.Length - position} trailing body bytes.");
    }

    private readonly record struct NodeLocation(int DocId, long NeighbourOffset, int NeighbourCount);
}
