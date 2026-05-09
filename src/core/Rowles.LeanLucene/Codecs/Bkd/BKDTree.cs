namespace Rowles.LeanLucene.Codecs.Bkd;

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
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(writer, CodecConstants.BKDVersion);

        writer.Write(fieldPoints.Count);
        foreach (var (field, points) in fieldPoints)
        {
            writer.Write(field);
            points.Sort((a, b) => a.Value.CompareTo(b.Value));
            WriteNode(writer, points, 0, points.Count, maxLeafSize);
        }
    }

    private static void WriteNode(BinaryWriter writer, List<(double Value, int DocId)> points, int start, int end, int maxLeafSize)
    {
        int count = end - start;
        if (count <= maxLeafSize)
        {
            // Leaf node
            writer.Write((byte)1); // leaf marker
            writer.Write(count);
            for (int i = start; i < end; i++)
            {
                writer.Write(points[i].Value);
                writer.Write(points[i].DocId);
            }
        }
        else
        {
            // Internal node — split at median
            int mid = start + count / 2;
            writer.Write((byte)0); // internal marker
            writer.Write(points[mid].Value); // split value
            WriteNode(writer, points, start, mid, maxLeafSize);
            WriteNode(writer, points, mid, end, maxLeafSize);
        }
    }
}
