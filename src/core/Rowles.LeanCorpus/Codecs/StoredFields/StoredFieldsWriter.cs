using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

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

        using var fdtOutput = new IndexOutput(fdtPath, durable: true);
        using var fdtScope = CodecFileHeader.BeginStreamingWrite(fdtOutput, CodecConstants.StoredFieldsVersion);
        fdtScope.Output.WriteInt32(blockSize);
        fdtScope.Output.WriteByte((byte)compression);

        var blockOffsets = new List<long>();
        var rawBuf = new ArrayBufferWriter<byte>(4096);
        Span<byte> encodeBuf = stackalloc byte[512];

        var distinctFieldIds = new List<int>(16);
        Span<int> intraOffsetsStack = stackalloc int[64];
        bool[] seenFieldId = ArrayPool<bool>.Shared.Rent(Math.Max(16, fieldNames.Count));
        try
        {
            for (int blockStart = 0; blockStart < docCount; blockStart += blockSize)
            {
                int blockEnd = Math.Min(blockStart + blockSize, docCount);
                int blockDocCount = blockEnd - blockStart;

                rawBuf.Clear();

                Span<int> intraOffsets = blockDocCount <= intraOffsetsStack.Length
                    ? intraOffsetsStack[..blockDocCount]
                    : new int[blockDocCount];

                for (int d = 0; d < blockDocCount; d++)
                {
                    intraOffsets[d] = (int)rawBuf.WrittenCount;
                    int docIdx = blockStart + d;
                    int entryStart = docStarts[docIdx];
                    int entryEnd = docIdx + 1 < docCount ? docStarts[docIdx + 1] : fieldIds.Count;

                    distinctFieldIds.Clear();
                    for (int e = entryStart; e < entryEnd; e++)
                    {
                        int fid = fieldIds[e];
                        if (fid >= seenFieldId.Length)
                        {
                            var grown = ArrayPool<bool>.Shared.Rent(fid + 1);
                            Array.Clear(grown);
                            foreach (int existing in distinctFieldIds) grown[existing] = true;
                            ArrayPool<bool>.Shared.Return(seenFieldId);
                            seenFieldId = grown;
                        }
                        if (!seenFieldId[fid])
                        {
                            seenFieldId[fid] = true;
                            distinctFieldIds.Add(fid);
                        }
                    }
                    foreach (int fid in distinctFieldIds) seenFieldId[fid] = false;

                    rawBuf.WriteInt32(distinctFieldIds.Count);
                    foreach (int fid in distinctFieldIds)
                    {
                        string name = fieldNames[fid];
                        int nameByteCount = Encoding.UTF8.GetByteCount(name);
                        Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
                        Encoding.UTF8.GetBytes(name, nameBuf);
                        rawBuf.WriteInt32(nameByteCount);
                        rawBuf.WriteBytes(nameBuf[..nameByteCount]);

                        int valueCount = 0;
                        for (int e = entryStart; e < entryEnd; e++)
                            if (fieldIds[e] == fid) valueCount++;
                        rawBuf.WriteInt32(valueCount);

                        for (int e = entryStart; e < entryEnd; e++)
                        {
                            if (fieldIds[e] != fid) continue;
                            WriteStoredValue(rawBuf, values[e], encodeBuf);
                        }
                    }
                }

                int rawLength = (int)rawBuf.WrittenCount;
                var rawData = rawBuf.WrittenSpan;

                var (compData, compLength) = StoredFieldCompression.Compress(rawData, compression);

                blockOffsets.Add(fdtOutput.Position);
                fdtOutput.WriteInt32(blockDocCount);
                fdtOutput.WriteInt32(rawLength);
                fdtOutput.WriteInt32(compLength);
                for (int i = 0; i < blockDocCount; i++)
                    fdtOutput.WriteInt32(intraOffsets[i]);
                fdtOutput.WriteBytes(compData.AsSpan(0, compLength));
            }
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(seenFieldId);
        }

        WriteFdx(fdxPath, blockSize, docCount, blockOffsets);
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
        using var fdtOutput = new IndexOutput(fdtPath, durable: true);
        using var fdtScope = CodecFileHeader.BeginStreamingWrite(fdtOutput, CodecConstants.StoredFieldsVersion);
        fdtScope.Output.WriteInt32(blockSize);
        fdtScope.Output.WriteByte((byte)compression);

        var blockOffsets = new List<long>();
        var rawBuf = new ArrayBufferWriter<byte>(4096);
        Span<byte> encodeBuf = stackalloc byte[512];

        for (int blockStart = 0; blockStart < docCount; blockStart += blockSize)
        {
            int blockEnd = Math.Min(blockStart + blockSize, docCount);
            int blockCount = blockEnd - blockStart;

            rawBuf.Clear();

            var intraOffsets = new int[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                intraOffsets[i] = (int)rawBuf.WrittenCount;
                var fields = readDocument(blockStart + i);
                rawBuf.WriteInt32(fields.Count);
                foreach (var (name, values) in fields)
                {
                    int nameByteCount = Encoding.UTF8.GetByteCount(name);
                    Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
                    Encoding.UTF8.GetBytes(name, nameBuf);
                    rawBuf.WriteInt32(nameByteCount);
                    rawBuf.WriteBytes(nameBuf[..nameByteCount]);

                    rawBuf.WriteInt32(values.Count);
                    foreach (var value in values)
                        WriteStoredValue(rawBuf, value, encodeBuf);
                }
            }

            int rawLength = (int)rawBuf.WrittenCount;
            var rawData = rawBuf.WrittenSpan;

            var (compData, compLength) = StoredFieldCompression.Compress(rawData, compression);

            blockOffsets.Add(fdtOutput.Position);
            fdtOutput.WriteInt32(blockCount);
            fdtOutput.WriteInt32(rawLength);
            fdtOutput.WriteInt32(compLength);
            for (int i = 0; i < blockCount; i++)
                fdtOutput.WriteInt32(intraOffsets[i]);
            fdtOutput.WriteBytes(compData.AsSpan(0, compLength));
        }

        WriteFdx(fdxPath, blockSize, docCount, blockOffsets);
    }

    private static void WriteFdx(string fdxPath, int blockSize, int docCount, List<long> blockOffsets)
    {
        using var fdxOutput = new IndexOutput(fdxPath, durable: true);
        StoredFieldsFileHeader.WriteV3FdxHeader(fdxOutput, blockSize, docCount, blockOffsets.Count);
        foreach (var offset in blockOffsets)
            fdxOutput.WriteInt64(offset);
    }

    private static void WriteStoredValue(IBufferWriter<byte> writer, StoredFieldValue value, Span<byte> encodeBuf)
    {
        writer.WriteByte((byte)value.Kind);

        if (value.IsBinary)
        {
            var bytes = value.BinaryValue ?? [];
            writer.WriteInt32(bytes.Length);
            writer.WriteBytes(bytes);
            return;
        }

        if (value.IsLong)
        {
            writer.WriteInt32(sizeof(long));
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes, value.LongValue);
            writer.WriteBytes(bytes);
            return;
        }

        var text = value.StringValue ?? string.Empty;
        int valueByteCount = Encoding.UTF8.GetByteCount(text);
        Span<byte> valueBuf = valueByteCount <= encodeBuf.Length ? encodeBuf : new byte[valueByteCount];
        Encoding.UTF8.GetBytes(text, valueBuf);
        writer.WriteInt32(valueByteCount);
        writer.WriteBytes(valueBuf[..valueByteCount]);
    }
}
