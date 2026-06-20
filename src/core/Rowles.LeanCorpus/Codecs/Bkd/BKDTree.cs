using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Bkd;

/// <summary>
/// Writes a 1-dimensional BKD tree for numeric point values, enabling O(log N + results) range lookups.
/// File format (.bkd): [fieldCount:int32] per field: [fieldName:string] [nodeCount:int32] [nodes...]
/// Each leaf stores sorted (value, docId) pairs; internal nodes store split values.
/// </summary>
internal static class BKDWriter
{
    /// <summary>Default max leaf size for BKD tree nodes.</summary>
    public const int DefaultMaxLeafSize = 512;

    internal static void Write(string filePath, Dictionary<string, List<(double Value, int DocId)>> fieldPoints, int maxLeafSize = DefaultMaxLeafSize)
    {
        if (maxLeafSize < 2)
            throw new ArgumentOutOfRangeException(nameof(maxLeafSize), maxLeafSize,
                "maxLeafSize must be at least 2. Values below 2 degenerate the tree into a single flat leaf node.");

        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        bodyBuf.WriteInt32(fieldPoints.Count);
        foreach (var (field, points) in fieldPoints)
        {
            bodyBuf.WriteString(field);
            points.Sort((a, b) => a.Value.CompareTo(b.Value));
            WriteNode(bodyBuf, points, 0, points.Count, maxLeafSize);
        }
        using var output = new IndexOutput(filePath);
        CodecFileHeader.Write(output, CodecFormats.Bkd, bodyBuf.WrittenSpan);
    }

    private static void WriteNode(IBufferWriter<byte> writer, List<(double Value, int DocId)> points, int start, int end, int maxLeafSize)
    {
        int count = end - start;
        if (count <= maxLeafSize)
        {
            // Leaf node
            writer.WriteByte(1); // leaf marker
            writer.WriteInt32(count);
            for (int i = start; i < end; i++)
            {
                writer.WriteInt64(BitConverter.DoubleToInt64Bits(points[i].Value));
                writer.WriteInt32(points[i].DocId);
            }
        }
        else
        {
            // Internal node — split at median
            int mid = start + count / 2;
            writer.WriteByte(0); // internal marker
            writer.WriteInt64(BitConverter.DoubleToInt64Bits(points[mid].Value)); // split value
            WriteNode(writer, points, start, mid, maxLeafSize);
            WriteNode(writer, points, mid, end, maxLeafSize);
        }
    }
}
