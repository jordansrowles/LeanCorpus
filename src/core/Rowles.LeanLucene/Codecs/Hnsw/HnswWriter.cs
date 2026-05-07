namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Writes a frozen <see cref="HnswGraph"/> to disc.
/// File format (version 1):
/// <code>
/// [magic:int32][version:byte=1]
/// [dimension:int32][normalised:byte][M:int32][M0:int32][efConstruction:int32]
/// [seed:int64][entryPoint:int32][maxLevel:int32][nodeCount:int32]
/// [levelCount:int32]
/// for each level (descending from max to 0):
///   [nodeCount:int32]
///   for each node:
///     [docId:int32][neighbourCount:int32][neighbours:int32[]]
/// </code>
/// </summary>
internal static class HnswWriter
{
    public static void Write(string filePath, HnswGraph graph, int dimension, bool normalised)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.IsReadOnly)
            throw new InvalidOperationException("HnswGraph must be frozen before writing.");

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(writer, CodecConstants.HnswVersion);

        writer.Write(dimension);
        writer.Write((byte)(normalised ? 1 : 0));
        writer.Write(graph.M);
        writer.Write(graph.M0);
        writer.Write(graph.EfConstruction);
        writer.Write(graph.Seed);
        writer.Write(graph.EntryPoint);
        writer.Write(graph.MaxLevel);
        writer.Write(graph.NodeCount);

        int levelCount = graph.LevelCount;
        writer.Write(levelCount);

        for (int level = levelCount - 1; level >= 0; level--)
        {
            var nodes = graph.GetNodesAtLevel(level).ToArray();
            writer.Write(nodes.Length);
            foreach (var docId in nodes)
            {
                var neighbours = graph.GetNeighbours(docId, level);
                writer.Write(docId);
                writer.Write(neighbours.Count);
                foreach (var n in neighbours)
                    writer.Write(n);
            }
        }
    }
}
