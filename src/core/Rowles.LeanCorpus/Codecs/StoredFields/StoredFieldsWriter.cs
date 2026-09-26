using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Writes stored field data (.fdt) with configurable compression
/// and a parallel offset index (.fdx). Blocks are bounded by raw bytes and a
/// configurable maximum document count. Each field supports multiple values.
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
        StoredFieldsBlockPolicy.ValidateMaximumDocumentCount(blockSize);
        int docCount = docStarts.Count;

        using var fdtOutput = new IndexOutput(fdtPath);
        using var fdtScope = CodecFileWriter.Begin(fdtOutput, StoredFieldsCodecFiles.Data);
        fdtScope.Output.WriteInt32(blockSize);
        fdtScope.Output.WriteByte((byte)compression);

        var blockOffsets = new List<long>();
        var intraOffsets = new List<int>(blockSize);
        var rawBuf = new ArrayBufferWriter<byte>(4096);
        Span<byte> encodeBuf = stackalloc byte[512];

        var distinctFieldIds = new List<int>(16);
        bool[] seenFieldId = ArrayPool<bool>.Shared.Rent(Math.Max(16, fieldNames.Count));
        int docsInBlock = 0;
        try
        {
            Array.Clear(seenFieldId);
            for (int docId = 0; docId < docCount; docId++)
            {
                int entryStart = docStarts[docId];
                int entryEnd = docId + 1 < docCount ? docStarts[docId + 1] : fieldIds.Count;

                distinctFieldIds.Clear();
                for (int entry = entryStart; entry < entryEnd; entry++)
                {
                    int fieldId = fieldIds[entry];
                    if (fieldId >= seenFieldId.Length)
                    {
                        var grown = ArrayPool<bool>.Shared.Rent(fieldId + 1);
                        Array.Clear(grown);
                        foreach (int existing in distinctFieldIds) grown[existing] = true;
                        ArrayPool<bool>.Shared.Return(seenFieldId);
                        seenFieldId = grown;
                    }

                    if (!seenFieldId[fieldId])
                    {
                        seenFieldId[fieldId] = true;
                        distinctFieldIds.Add(fieldId);
                    }
                }
                foreach (int fieldId in distinctFieldIds) seenFieldId[fieldId] = false;

                long documentRawLength = StoredFieldsBlockPolicy.GetFlatDocumentRawLength(
                    fieldIds, values, fieldNames, entryStart, entryEnd, distinctFieldIds);
                StoredFieldsBlockPolicy.ValidateRawLength(documentRawLength);

                if (documentRawLength > StoredFieldsBlockPolicy.TargetRawBytes)
                {
                    FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);
                    var oversizedBuffer = new ArrayBufferWriter<byte>(checked((int)documentRawLength));
                    SerializeDocument(
                        oversizedBuffer, fieldIds, values, fieldNames,
                        entryStart, entryEnd, distinctFieldIds, encodeBuf);
                    WriteBlock(fdtScope.Output, blockOffsets, oversizedBuffer.WrittenSpan, [0], compression);
                    continue;
                }

                if (StoredFieldsBlockPolicy.ShouldFlushBeforeAdd(
                        docsInBlock, rawBuf.WrittenCount, documentRawLength, blockSize))
                    FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);

                intraOffsets.Add(rawBuf.WrittenCount);
                SerializeDocument(rawBuf, fieldIds, values, fieldNames, entryStart, entryEnd, distinctFieldIds, encodeBuf);
                docsInBlock++;

                if (StoredFieldsBlockPolicy.ShouldFlushAfterAdd(docsInBlock, rawBuf.WrittenCount, blockSize))
                    FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);
            }
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(seenFieldId);
        }

        FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);
        fdtScope.Complete();
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
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentNullException.ThrowIfNull(readDocument);
        StoredFieldsBlockPolicy.ValidateMaximumDocumentCount(blockSize);

        using var fdtOutput = new IndexOutput(fdtPath);
        using var fdtScope = CodecFileWriter.Begin(fdtOutput, StoredFieldsCodecFiles.Data);
        fdtScope.Output.WriteInt32(blockSize);
        fdtScope.Output.WriteByte((byte)compression);

        var blockOffsets = new List<long>();
        var intraOffsets = new List<int>(blockSize);
        var rawBuf = new ArrayBufferWriter<byte>(4096);
        Span<byte> encodeBuf = stackalloc byte[512];
        int docsInBlock = 0;

        for (int docId = 0; docId < docCount; docId++)
        {
            var fields = readDocument(docId);
            long documentRawLength = StoredFieldsBlockPolicy.GetDocumentRawLength(fields);
            StoredFieldsBlockPolicy.ValidateRawLength(documentRawLength);

            if (documentRawLength > StoredFieldsBlockPolicy.TargetRawBytes)
            {
                FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);
                var oversizedBuffer = new ArrayBufferWriter<byte>(checked((int)documentRawLength));
                StoredFieldsBlockSerializer.WriteDocument(oversizedBuffer, fields, encodeBuf);
                WriteBlock(fdtScope.Output, blockOffsets, oversizedBuffer.WrittenSpan, [0], compression);
                continue;
            }

            if (StoredFieldsBlockPolicy.ShouldFlushBeforeAdd(
                    docsInBlock, rawBuf.WrittenCount, documentRawLength, blockSize))
                FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);

            intraOffsets.Add(rawBuf.WrittenCount);
            StoredFieldsBlockSerializer.WriteDocument(rawBuf, fields, encodeBuf);
            docsInBlock++;

            if (StoredFieldsBlockPolicy.ShouldFlushAfterAdd(docsInBlock, rawBuf.WrittenCount, blockSize))
                FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);
        }

        FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compression);
        fdtScope.Complete();
        WriteFdx(fdxPath, blockSize, docCount, blockOffsets);
    }

    private static void SerializeDocument(
        IBufferWriter<byte> writer,
        List<int> fieldIds,
        List<StoredFieldValue> values,
        List<string> fieldNames,
        int entryStart,
        int entryEnd,
        List<int> distinctFieldIds,
        Span<byte> encodeBuf)
    {
        writer.WriteInt32(distinctFieldIds.Count);
        foreach (int fieldId in distinctFieldIds)
        {
            string name = fieldNames[fieldId];
            int nameByteCount = Encoding.UTF8.GetByteCount(name);
            Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
            Encoding.UTF8.GetBytes(name, nameBuf);
            writer.WriteInt32(nameByteCount);
            writer.WriteBytes(nameBuf[..nameByteCount]);

            int valueCount = 0;
            for (int entry = entryStart; entry < entryEnd; entry++)
                if (fieldIds[entry] == fieldId) valueCount++;
            writer.WriteInt32(valueCount);

            for (int entry = entryStart; entry < entryEnd; entry++)
                if (fieldIds[entry] == fieldId)
                    StoredFieldsBlockSerializer.WriteValue(writer, values[entry], encodeBuf);
        }
    }

    private static void FlushBlock(
        ISequentialIndexOutput output,
        List<long> blockOffsets,
        ArrayBufferWriter<byte> rawBuf,
        List<int> intraOffsets,
        ref int docsInBlock,
        FieldCompressionPolicy compression)
    {
        if (docsInBlock == 0) return;
        WriteBlock(output, blockOffsets, rawBuf.WrittenSpan, intraOffsets, compression);
        rawBuf.Clear();
        intraOffsets.Clear();
        docsInBlock = 0;
    }

    internal static void WriteBlock(
        ISequentialIndexOutput output,
        List<long> blockOffsets,
        ReadOnlySpan<byte> rawData,
        IReadOnlyList<int> intraOffsets,
        FieldCompressionPolicy compression)
    {
        int rawLength = rawData.Length;
        StoredFieldsBlockPolicy.ValidateRawLength(rawLength);
        var (compData, compLength) = StoredFieldCompression.Compress(rawData, compression);
        ValidateCompressedLength(rawLength, compLength);

        blockOffsets.Add(output.Position);
        output.WriteInt32(intraOffsets.Count);
        output.WriteInt32(rawLength);
        output.WriteInt32(compLength);
        for (int i = 0; i < intraOffsets.Count; i++)
            output.WriteInt32(intraOffsets[i]);
        output.WriteBytes(compData.AsSpan(0, compLength));
    }

    internal static void ValidateCompressedLength(int rawLength, int compLength)
    {
        if (compLength <= 0 || compLength > StoredFieldsBlockPolicy.MaximumRawBytes)
            throw new InvalidDataException(
                $"Stored fields block compLength {compLength} exceeds maximum {StoredFieldsBlockPolicy.MaximumRawBytes}.");
    }

    private static void WriteFdx(string fdxPath, int blockSize, int docCount, List<long> blockOffsets)
    {
        using var fdxOutput = new IndexOutput(fdxPath);
        using var fdxScope = CodecFileWriter.Begin(fdxOutput, StoredFieldsCodecFiles.Index);
        fdxScope.Output.WriteInt32(blockSize);
        fdxScope.Output.WriteInt32(docCount);
        fdxScope.Output.WriteInt32(blockOffsets.Count);
        foreach (var offset in blockOffsets)
            fdxScope.Output.WriteInt64(offset);
        fdxScope.Complete();
    }
}
