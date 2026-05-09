using Rowles.LeanLucene.Codecs.Vectors;
namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Reads a <see cref="HnswGraph"/> previously written by <see cref="HnswWriter"/>. The graph
/// is materialised into the frozen, read-only state ready for concurrent search.
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
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.ValidateHeader(reader, CodecConstants.HnswVersion, "HNSW");

        int dimension = reader.ReadInt32();
        bool normalised = reader.ReadByte() != 0;
        if (expectedNormalised is bool expected && expected != normalised)
            throw new InvalidDataException(
                $"HNSW file at '{filePath}' declares Normalised={normalised} but the segment field declares Normalised={expected}.");
        if (vectorSource.Dimension != dimension)
            throw new InvalidDataException(
                $"HNSW file dimension {dimension} does not match vector source dimension {vectorSource.Dimension}.");

        int m = reader.ReadInt32();
        int m0 = reader.ReadInt32();
        int efConstruction = reader.ReadInt32();
        long seed = reader.ReadInt64();
        int entryPoint = reader.ReadInt32();
        int maxLevel = reader.ReadInt32();
        int nodeCount = reader.ReadInt32();
        int levelCount = reader.ReadInt32();

        var levels = new List<Dictionary<int, int[]>>(levelCount);
        for (int i = 0; i < levelCount; i++)
            levels.Add(new Dictionary<int, int[]>());

        for (int level = levelCount - 1; level >= 0; level--)
        {
            int nodes = reader.ReadInt32();
            var dict = levels[level];
            for (int n = 0; n < nodes; n++)
            {
                int docId = reader.ReadInt32();
                int neighCount = reader.ReadInt32();
                var arr = new int[neighCount];
                for (int k = 0; k < neighCount; k++)
                    arr[k] = reader.ReadInt32();

                if (docIdRemap is null)
                {
                    dict[docId] = arr;
                }
                else if (docIdRemap.TryGetValue(docId, out int newDocId))
                {
                    var remapped = new List<int>(arr.Length);
                    foreach (int neigh in arr)
                    {
                        if (docIdRemap.TryGetValue(neigh, out int newNeigh))
                            remapped.Add(newNeigh);
                    }
                    dict[newDocId] = remapped.ToArray();
                }
            }
        }

        if (docIdRemap is not null)
        {
            entryPoint = docIdRemap.TryGetValue(entryPoint, out int newEntry) ? newEntry : -1;
            nodeCount = 0;
            foreach (var lvl in levels) nodeCount += lvl.Count;
            // Fix maxLevel if higher levels became empty after remap.
            while (maxLevel >= 0 && levels[maxLevel].Count == 0) maxLevel--;
            // If the original entry point was dropped, pick any surviving top-level node.
            if (entryPoint == -1 && maxLevel >= 0)
            {
                using var e = levels[maxLevel].Keys.GetEnumerator();
                if (e.MoveNext()) entryPoint = e.Current;
            }
        }

        var config = new HnswBuildConfig { M = m, M0 = m0, EfConstruction = efConstruction };
        return HnswGraph.FromFrozen(vectorSource, config, seed, levels, entryPoint, maxLevel, nodeCount);
    }
}
