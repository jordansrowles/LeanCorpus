using System.Buffers;
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
        int blockSize = DefaultBlockSize,
        FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate,
        CodecCatalog? catalog = null)
    {
        CompressionCodecRegistry.MarkIndexOpened();
        var compressionCodec = (catalog ?? CodecCatalog.Default).GetCompressionCodec((byte)compression);
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
        int scratchLength = Math.Max(1, fieldNames.Count);
        int[] fieldCounts = ArrayPool<int>.Shared.Rent(scratchLength);
        int[] fieldEnds = ArrayPool<int>.Shared.Rent(scratchLength);
        int[] groupedEntryIndexes = ArrayPool<int>.Shared.Rent(Math.Max(1, Math.Min(fieldIds.Count, 256)));
        int docsInBlock = 0;
        try
        {
            for (int docId = 0; docId < docCount; docId++)
            {
                int entryStart = docStarts[docId];
                int entryEnd = docId + 1 < docCount ? docStarts[docId + 1] : fieldIds.Count;
                int entryCount = entryEnd - entryStart;
                if (entryCount > groupedEntryIndexes.Length)
                {
                    var grown = ArrayPool<int>.Shared.Rent(entryCount);
                    ArrayPool<int>.Shared.Return(groupedEntryIndexes);
                    groupedEntryIndexes = grown;
                }

                long documentRawLength = StoredFieldsBlockEncoder.GroupFlatDocument(
                    fieldIds, values, fieldNames, entryStart, entryEnd, distinctFieldIds,
                    fieldCounts, fieldEnds, groupedEntryIndexes);
                StoredFieldsBlockPolicy.ValidateRawLength(documentRawLength);

                if (documentRawLength > StoredFieldsBlockPolicy.TargetRawBytes)
                {
                    FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);
                    var oversizedBuffer = new ArrayBufferWriter<byte>(checked((int)documentRawLength));
                    StoredFieldsBlockEncoder.WriteFlatDocument(
                        oversizedBuffer, values, distinctFieldIds, fieldCounts, groupedEntryIndexes, encodeBuf);
                    WriteBlock(fdtScope.Output, blockOffsets, oversizedBuffer.WrittenSpan, [0], compressionCodec);
                    continue;
                }

                if (StoredFieldsBlockPolicy.ShouldFlushBeforeAdd(
                        docsInBlock, rawBuf.WrittenCount, documentRawLength, blockSize))
                    FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);

                intraOffsets.Add(rawBuf.WrittenCount);
                StoredFieldsBlockEncoder.WriteFlatDocument(
                    rawBuf, values, distinctFieldIds, fieldCounts, groupedEntryIndexes, encodeBuf);
                docsInBlock++;

                if (StoredFieldsBlockPolicy.ShouldFlushAfterAdd(docsInBlock, rawBuf.WrittenCount, blockSize))
                    FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(fieldCounts);
            ArrayPool<int>.Shared.Return(fieldEnds);
            ArrayPool<int>.Shared.Return(groupedEntryIndexes);
        }

        FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);
        fdtScope.Complete();
        WriteFdx(fdxPath, blockSize, docCount, fieldNames, blockOffsets);
    }

    internal static void Write(string fdtPath, string fdxPath, IReadOnlyList<Dictionary<string, List<string>>> docs,
        int blockSize = DefaultBlockSize,
        FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate,
        CodecCatalog? catalog = null)
        => Write(
            fdtPath,
            fdxPath,
            docs.Count,
            docId => docs[docId].ToDictionary(
                static kvp => kvp.Key,
                static kvp => kvp.Value.Select(StoredFieldValue.FromString).ToList()),
            blockSize,
            compression,
            catalog);

    internal static void Write(
        string fdtPath,
        string fdxPath,
        int docCount,
        Func<int, Dictionary<string, List<StoredFieldValue>>> readDocument,
        int blockSize = DefaultBlockSize,
        FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate,
        CodecCatalog? catalog = null)
    {
        CompressionCodecRegistry.MarkIndexOpened();
        var compressionCodec = (catalog ?? CodecCatalog.Default).GetCompressionCodec((byte)compression);
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
        var fieldNameToId = new Dictionary<string, int>(StringComparer.Ordinal);
        var fieldNames = new List<string>();
        Span<byte> encodeBuf = stackalloc byte[512];
        int docsInBlock = 0;

        for (int docId = 0; docId < docCount; docId++)
        {
            var fields = readDocument(docId);
            long documentRawLength = StoredFieldsBlockEncoder.GetDictionaryDocumentRawLength(
                fields, fieldNameToId, fieldNames);
            StoredFieldsBlockPolicy.ValidateRawLength(documentRawLength);

            if (documentRawLength > StoredFieldsBlockPolicy.TargetRawBytes)
            {
                FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);
                var oversizedBuffer = new ArrayBufferWriter<byte>(checked((int)documentRawLength));
                StoredFieldsBlockEncoder.WriteDictionaryDocument(
                    oversizedBuffer, fields, fieldNameToId, fieldNames, encodeBuf);
                WriteBlock(fdtScope.Output, blockOffsets, oversizedBuffer.WrittenSpan, [0], compressionCodec);
                continue;
            }

            if (StoredFieldsBlockPolicy.ShouldFlushBeforeAdd(
                    docsInBlock, rawBuf.WrittenCount, documentRawLength, blockSize))
                FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);

            intraOffsets.Add(rawBuf.WrittenCount);
            StoredFieldsBlockEncoder.WriteDictionaryDocument(
                rawBuf, fields, fieldNameToId, fieldNames, encodeBuf);
            docsInBlock++;

            if (StoredFieldsBlockPolicy.ShouldFlushAfterAdd(docsInBlock, rawBuf.WrittenCount, blockSize))
                FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);
        }

        FlushBlock(fdtScope.Output, blockOffsets, rawBuf, intraOffsets, ref docsInBlock, compressionCodec);
        fdtScope.Complete();
        WriteFdx(fdxPath, blockSize, docCount, fieldNames, blockOffsets);
    }

    private static void FlushBlock(
        ISequentialIndexOutput output,
        List<long> blockOffsets,
        ArrayBufferWriter<byte> rawBuf,
        List<int> intraOffsets,
        ref int docsInBlock,
        IFieldCompressionCodec compressionCodec)
    {
        if (docsInBlock == 0) return;
        WriteBlock(output, blockOffsets, rawBuf.WrittenSpan, intraOffsets, compressionCodec);
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
        => WriteBlock(
            output,
            blockOffsets,
            rawData,
            intraOffsets,
            CodecCatalog.Default.GetCompressionCodec((byte)compression));

    internal static void WriteBlock(
        ISequentialIndexOutput output,
        List<long> blockOffsets,
        ReadOnlySpan<byte> rawData,
        IReadOnlyList<int> intraOffsets,
        IFieldCompressionCodec compressionCodec)
    {
        int rawLength = rawData.Length;
        StoredFieldsBlockPolicy.ValidateRawLength(rawLength);
        var (compData, compLength) = StoredFieldCompression.Compress(rawData, compressionCodec);
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

    private static void WriteFdx(
        string fdxPath,
        int blockSize,
        int docCount,
        List<string> fieldNames,
        List<long> blockOffsets)
    {
        using var fdxOutput = new IndexOutput(fdxPath);
        using var fdxScope = CodecFileWriter.Begin(fdxOutput, StoredFieldsCodecFiles.Index);
        StoredFieldsBlockEncoder.WriteIndexBody(fdxScope.Output, blockSize, docCount, fieldNames, blockOffsets);
        fdxScope.Complete();
    }
}
