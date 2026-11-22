using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Writes a <see cref="VectorGraph"/> as a <c>.vgr</c> body, version 1 (ADR052). All integers
/// are little-endian; neighbours and the entry point are ordinals.
/// </summary>
/// <remarks>
/// <code>
/// int32 dimension | byte scorer (1 = Float32 cosine) | byte flags (bit0 normalised, bit1 inverse norms)
/// int32 M | int32 M0 | int32 efConstruction | int64 seed
/// int32 nodeCount | int32 maxLevel (-1 when empty) | int32 entryOrdinal (-1 when empty)
/// int32[nodeCount] docIds, strictly ascending
/// level 0: nodeCount records of (1 + M0) int32: count, then M0 slots (unused = -1)
/// per level 1..maxLevel: int32 levelNodeCount, int32[levelNodeCount] ascending ordinals,
///                        levelNodeCount records of (1 + M) int32
/// float32[nodeCount] inverse norms, only when flags bit1 is set
/// </code>
/// </remarks>
internal static class VectorGraphWriter
{
    internal static void Write(string filePath, VectorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Graph, durable: false, body => WriteBody(body, graph));
    }

    internal static void WriteBody(CodecBodyOutput body, VectorGraph graph)
    {
        body.WriteInt32(graph.Dimension);
        body.WriteByte(VectorGraph.ScorerFloat32Cosine);
        byte flags = graph.Normalised ? VectorGraph.FlagNormalised : VectorGraph.FlagInverseNorms;
        body.WriteByte(flags);
        body.WriteInt32(graph.M);
        body.WriteInt32(graph.M0);
        body.WriteInt32(graph.EfConstruction);
        body.WriteInt64(graph.Seed);
        body.WriteInt32(graph.NodeCount);
        body.WriteInt32(graph.MaxLevel);
        body.WriteInt32(graph.EntryOrdinal);
        WriteInt32s(body, graph.DocIds);
        // Stream level zero without cloning it: a built graph owns one array, a mapped one is
        // written record by record.
        if (graph.NodeCount > 0 && !graph.OwnedLevel0.IsEmpty)
            WriteInt32s(body, graph.OwnedLevel0);
        else
            for (int ordinal = 0; ordinal < graph.NodeCount; ordinal++)
                WriteInt32s(body, graph.Level0RecordAt(ordinal));
        for (int level = 1; level <= graph.MaxLevel; level++)
        {
            ReadOnlySpan<int> ordinals = graph.LevelOrdinals(level);
            body.WriteInt32(ordinals.Length);
            WriteInt32s(body, ordinals);
            WriteInt32s(body, graph.UpperAdjacency(level));
        }

        if (!graph.Normalised)
        {
            foreach (float inverseNorm in graph.InverseNorms)
                body.WriteSingle(inverseNorm);
        }
    }

    private static void WriteInt32s(CodecBodyOutput body, ReadOnlySpan<int> values)
    {
        if (BitConverter.IsLittleEndian)
        {
            body.WriteBytes(MemoryMarshal.AsBytes(values));
            return;
        }

        foreach (int value in values)
            body.WriteInt32(value);
    }
}
