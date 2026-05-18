namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Writes stored field data (.fdt) with configurable compression
/// and a parallel offset index (.fdx). Documents are grouped into blocks of 16.
/// Each field supports multiple values.
/// </summary>
internal static class StoredFieldsWriter
{
    private const int DefaultBlockSize = 16;

    /// <summary>
    /// Write stored fields from a flat struct-of-arrays buffer (used by IndexWriter flush path).
    /// </summary>
    internal static void Write(string fdtPath, string fdxPath,
        List<int> docStarts, List<int> fieldIds, List<StoredFieldValue> values, List<string> fieldNames,
        int blockSize = DefaultBlockSize, FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate)
    {
        int docCount = docStarts.Count;

        using var fdtStream = new FileStream(fdtPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdtWriter = new BinaryWriter(fdtStream, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(fdtWriter, CodecConstants.StoredFieldsVersion);
        fdtWriter.Write(blockSize);
        fdtWriter.Write((byte)compression);

        var blockOffsets = new List<long>();

        var rawStream = new MemoryStream(4096);
        var rawWriter = new BinaryWriter(rawStream, System.Text.Encoding.UTF8, leaveOpen: true);
        Span<byte> encodeBuf = stackalloc byte[512];

        var distinctFieldIds = new List<int>(16);
        Span<int> intraOffsetsStack = stackalloc int[64];
        bool[] seenFieldId = System.Buffers.ArrayPool<bool>.Shared.Rent(Math.Max(16, fieldNames.Count));
        try
        {

        for (int blockStart = 0; blockStart < docCount; blockStart += blockSize)
        {
            int blockEnd = Math.Min(blockStart + blockSize, docCount);
            int blockDocCount = blockEnd - blockStart;

            rawStream.SetLength(0);
            rawStream.Position = 0;

            Span<int> intraOffsets = blockDocCount <= intraOffsetsStack.Length
                ? intraOffsetsStack[..blockDocCount]
                : new int[blockDocCount];
            for (int d = 0; d < blockDocCount; d++)
            {
                intraOffsets[d] = (int)rawStream.Position;
                int docIdx = blockStart + d;
                int entryStart = docStarts[docIdx];
                int entryEnd = docIdx + 1 < docCount ? docStarts[docIdx + 1] : fieldIds.Count;

                distinctFieldIds.Clear();
                for (int e = entryStart; e < entryEnd; e++)
                {
                    int fid = fieldIds[e];
                    if (fid >= seenFieldId.Length)
                    {
                        var grown = System.Buffers.ArrayPool<bool>.Shared.Rent(fid + 1);
                        Array.Clear(grown);
                        foreach (int existing in distinctFieldIds) grown[existing] = true;
                        System.Buffers.ArrayPool<bool>.Shared.Return(seenFieldId);
                        seenFieldId = grown;
                    }
                    if (!seenFieldId[fid])
                    {
                        seenFieldId[fid] = true;
                        distinctFieldIds.Add(fid);
                    }
                }
                foreach (int fid in distinctFieldIds) seenFieldId[fid] = false;

                rawWriter.Write(distinctFieldIds.Count);
                foreach (int fid in distinctFieldIds)
                {
                    string name = fieldNames[fid];
                    int nameByteCount = System.Text.Encoding.UTF8.GetByteCount(name);
                    Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
                    System.Text.Encoding.UTF8.GetBytes(name, nameBuf);
                    rawWriter.Write(nameByteCount);
                    rawWriter.Write(nameBuf[..nameByteCount]);

                    int valueCount = 0;
                    for (int e = entryStart; e < entryEnd; e++)
                        if (fieldIds[e] == fid) valueCount++;
                    rawWriter.Write(valueCount);

                    for (int e = entryStart; e < entryEnd; e++)
                    {
                        if (fieldIds[e] != fid) continue;
                        WriteStoredValue(rawWriter, values[e], encodeBuf);
                    }
                }
            }
            rawWriter.Flush();

            int rawLength = (int)rawStream.Length;
            var rawData = rawStream.GetBuffer().AsSpan(0, rawLength);

            var (compData, compLength) = StoredFieldCompression.Compress(rawData, compression);

            blockOffsets.Add(fdtStream.Position);
            fdtWriter.Write(blockDocCount);
            fdtWriter.Write(rawLength);
            fdtWriter.Write(compLength);
            for (int i = 0; i < blockDocCount; i++)
                fdtWriter.Write(intraOffsets[i]);
            fdtWriter.Write(compData.AsSpan(0, compLength));
        }
        }
        finally
        {
            System.Buffers.ArrayPool<bool>.Shared.Return(seenFieldId);
        }

        rawWriter.Dispose();
        rawStream.Dispose();

        fdtWriter.Flush();

        using var fdxStream = new FileStream(fdxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdxWriter = new BinaryWriter(fdxStream, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(fdxWriter, CodecConstants.StoredFieldsVersion);
        fdxWriter.Write(blockSize);
        fdxWriter.Write(docCount);
        fdxWriter.Write(blockOffsets.Count);
        foreach (var offset in blockOffsets)
            fdxWriter.Write(offset);
    }

    internal static void Write(string fdtPath, string fdxPath, IReadOnlyList<Dictionary<string, List<string>>> docs,
        int blockSize = DefaultBlockSize, FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate)
        => Write(
            fdtPath,
            fdxPath,
            docs.Count,
            docId => docs[docId].ToDictionary(
                static kvp => kvp.Key,
                static kvp => kvp.Value.Select(StoredFieldValue.FromString).ToList()),
            blockSize,
            compression);

    internal static void Write(
        string fdtPath,
        string fdxPath,
        int docCount,
        Func<int, Dictionary<string, List<StoredFieldValue>>> readDocument,
        int blockSize = DefaultBlockSize,
        FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate)
    {
        using var fdtStream = new FileStream(fdtPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdtWriter = new BinaryWriter(fdtStream, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(fdtWriter, CodecConstants.StoredFieldsVersion);
        fdtWriter.Write(blockSize);
        fdtWriter.Write((byte)compression);

        var blockOffsets = new List<long>();

        var rawStream = new MemoryStream(4096);
        var rawWriter = new BinaryWriter(rawStream, System.Text.Encoding.UTF8, leaveOpen: true);
        Span<byte> encodeBuf = stackalloc byte[512];

        for (int blockStart = 0; blockStart < docCount; blockStart += blockSize)
        {
            int blockEnd = Math.Min(blockStart + blockSize, docCount);
            int blockCount = blockEnd - blockStart;

            rawStream.SetLength(0);
            rawStream.Position = 0;

            var intraOffsets = new int[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                intraOffsets[i] = (int)rawStream.Position;
                var fields = readDocument(blockStart + i);
                rawWriter.Write(fields.Count);
                foreach (var (name, values) in fields)
                {
                    int nameByteCount = System.Text.Encoding.UTF8.GetByteCount(name);
                    Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
                    System.Text.Encoding.UTF8.GetBytes(name, nameBuf);
                    rawWriter.Write(nameByteCount);
                    rawWriter.Write(nameBuf[..nameByteCount]);

                    rawWriter.Write(values.Count);
                    foreach (var value in values)
                        WriteStoredValue(rawWriter, value, encodeBuf);
                }
            }
            rawWriter.Flush();

            int rawLength = (int)rawStream.Length;
            var rawData = rawStream.GetBuffer().AsSpan(0, rawLength);

            var (compData, compLength) = StoredFieldCompression.Compress(rawData, compression);

            blockOffsets.Add(fdtStream.Position);
            fdtWriter.Write(blockCount);
            fdtWriter.Write(rawLength);
            fdtWriter.Write(compLength);
            for (int i = 0; i < blockCount; i++)
                fdtWriter.Write(intraOffsets[i]);
            fdtWriter.Write(compData.AsSpan(0, compLength));
        }

        rawWriter.Dispose();
        rawStream.Dispose();

        fdtWriter.Flush();

        using var fdxStream = new FileStream(fdxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdxWriter = new BinaryWriter(fdxStream, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(fdxWriter, CodecConstants.StoredFieldsVersion);
        fdxWriter.Write(blockSize);
        fdxWriter.Write(docCount);
        fdxWriter.Write(blockOffsets.Count);
        foreach (var offset in blockOffsets)
            fdxWriter.Write(offset);
    }

    private static void WriteStoredValue(BinaryWriter writer, StoredFieldValue value, Span<byte> encodeBuf)
    {
        writer.Write((byte)value.Kind);

        if (value.IsBinary)
        {
            var bytes = value.BinaryValue ?? [];
            writer.Write(bytes.Length);
            writer.Write(bytes);
            return;
        }

        var text = value.StringValue ?? string.Empty;
        int valueByteCount = System.Text.Encoding.UTF8.GetByteCount(text);
        Span<byte> valueBuf = valueByteCount <= encodeBuf.Length ? encodeBuf : new byte[valueByteCount];
        System.Text.Encoding.UTF8.GetBytes(text, valueBuf);
        writer.Write(valueByteCount);
        writer.Write(valueBuf[..valueByteCount]);
    }
}
