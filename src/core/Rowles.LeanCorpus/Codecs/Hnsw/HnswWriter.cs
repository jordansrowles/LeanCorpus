using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Writes a frozen <see cref="HnswGraph"/> to disc.
/// </summary>
internal static class HnswWriter
{
    public static void Write(string filePath, HnswGraph graph, int dimension, bool normalised)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.IsReadOnly)
            throw new InvalidOperationException("HnswGraph must be frozen before writing.");

        CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Hnsw, durable: false, bodyOutput =>
        {
            bodyOutput.WriteInt32(dimension);
            bodyOutput.WriteByte((byte)(normalised ? 1 : 0));
            bodyOutput.WriteInt32(graph.M);
            bodyOutput.WriteInt32(graph.M0);
            bodyOutput.WriteInt32(graph.EfConstruction);
            bodyOutput.WriteInt64(graph.Seed);
            bodyOutput.WriteInt32(graph.EntryPoint);
            bodyOutput.WriteInt32(graph.MaxLevel);
            bodyOutput.WriteInt32(graph.NodeCount);

            int levelCount = graph.LevelCount;
            bodyOutput.WriteInt32(levelCount);

            for (int level = levelCount - 1; level >= 0; level--)
            {
                var nodes = graph.GetNodesAtLevel(level).ToArray();
                bodyOutput.WriteInt32(nodes.Length);
                foreach (var docId in nodes)
                {
                    var neighbours = graph.GetNeighbours(docId, level);
                    bodyOutput.WriteInt32(docId);
                    bodyOutput.WriteInt32(neighbours.Count);
                    foreach (var n in neighbours)
                        bodyOutput.WriteInt32(n);
                }
            }
        });
    }
}
