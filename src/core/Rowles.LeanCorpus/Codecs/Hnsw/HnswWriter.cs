using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
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

        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        bodyBuf.WriteInt32(dimension);
        bodyBuf.WriteByte((byte)(normalised ? 1 : 0));
        bodyBuf.WriteInt32(graph.M);
        bodyBuf.WriteInt32(graph.M0);
        bodyBuf.WriteInt32(graph.EfConstruction);
        bodyBuf.WriteInt64(graph.Seed);
        bodyBuf.WriteInt32(graph.EntryPoint);
        bodyBuf.WriteInt32(graph.MaxLevel);
        bodyBuf.WriteInt32(graph.NodeCount);

        int levelCount = graph.LevelCount;
        bodyBuf.WriteInt32(levelCount);

        for (int level = levelCount - 1; level >= 0; level--)
        {
            var nodes = graph.GetNodesAtLevel(level).ToArray();
            bodyBuf.WriteInt32(nodes.Length);
            foreach (var docId in nodes)
            {
                var neighbours = graph.GetNeighbours(docId, level);
                bodyBuf.WriteInt32(docId);
                bodyBuf.WriteInt32(neighbours.Count);
                foreach (var n in neighbours)
                    bodyBuf.WriteInt32(n);
            }
        }

        using var output = new IndexOutput(filePath, durable: true);
        CodecFileHeader.Write(output, CodecFormats.Hnsw, bodyBuf.WrittenSpan);
    }
}
