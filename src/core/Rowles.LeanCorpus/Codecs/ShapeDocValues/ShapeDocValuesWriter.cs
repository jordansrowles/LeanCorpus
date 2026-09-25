using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal static class ShapeDocValuesWriter
{
    private const int RecordHeaderLength = 72;
    private const int MaximumFieldNameBytes = 1 << 20;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Write(
        string filePath,
        int maxDoc,
        IReadOnlyDictionary<string, ShapeDocValuesFieldBuffer> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDoc);
        ArgumentNullException.ThrowIfNull(fields);

        string[] names = fields
            .Where(static pair => pair.Value.Records.Count > 0)
            .Select(static pair => pair.Key)
            .ToArray();
        Array.Sort(names, PackedBkdFieldNameComparer.Instance);

        CodecFileWriter.WriteAtomically(filePath, ShapeDocValuesCodecFiles.Data, durable: false, output =>
        {
            long bodyStart = output.Position;
            var directory = new DirectoryEntry[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                ShapeDocValuesFieldBuffer field = fields[names[i]];
                if (!string.Equals(field.FieldName, names[i], StringComparison.Ordinal))
                    throw new InvalidDataException("Shape DocValues dictionary key does not match its field buffer name.");
                long sectionOffset = checked(output.Position - bodyStart);
                WriteFieldSection(output, field, maxDoc);
                directory[i] = new DirectoryEntry(
                    names[i],
                    sectionOffset,
                    checked(output.Position - bodyStart - sectionOffset));
            }

            long directoryOffset = checked(output.Position - bodyStart);
            output.WriteInt32(names.Length);
            foreach (DirectoryEntry entry in directory)
            {
                WriteFieldName(output, entry.Name);
                output.WriteInt64(entry.Offset);
                output.WriteInt64(entry.Length);
            }

            output.WriteBytes("SHDV"u8);
            output.WriteInt32(names.Length);
            output.WriteInt64(directoryOffset);
        });
    }

    private static void WriteFieldSection(
        CodecBodyOutput output,
        ShapeDocValuesFieldBuffer field,
        int maxDoc)
    {
        IReadOnlyList<ShapeDocValuesRecord> records = field.Records;
        if (records.Count > maxDoc)
            throw new InvalidDataException($"Shape DocValues field '{field.FieldName}' has more records than maxDoc.");

        long sectionStart = output.Position;
        output.WriteBytes("SHF1"u8);
        output.WriteByte(field.Kind == SpatialFieldKind.GeoShape ? (byte)0 : (byte)1);
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteInt32(maxDoc);
        output.WriteInt32(records.Count);

        var directory = new RecordDirectoryEntry[records.Count];
        int previousDocId = -1;
        for (int i = 0; i < records.Count; i++)
        {
            ShapeDocValuesRecord record = records[i];
            if (record.DocumentId <= previousDocId || (uint)record.DocumentId >= (uint)maxDoc)
                throw new InvalidDataException("Shape DocValues records must have increasing in-range document IDs.");
            previousDocId = record.DocumentId;

            long recordOffset = checked(output.Position - sectionStart);
            int recordLength;
            if (field.UsesRawRecords)
            {
                ReadOnlyMemory<byte> rawRecord = field.GetRawRecord(i);
                if (record.ValueCount == 0 || record.PrimitiveCount == 0 || rawRecord.Length < RecordHeaderLength)
                    throw new InvalidDataException("Raw Shape DocValues record has invalid counts or length.");
                output.WriteBytes(rawRecord.Span);
                recordLength = rawRecord.Length;
            }
            else
            {
                ReadOnlyMemory<byte> primitives = field.GetPrimitives(record);
                if (record.ValueCount == 0 || record.PrimitiveCount == 0
                    || primitives.Length != checked(record.PrimitiveCount * ShapePrimitiveCodec.PackedValueLength))
                    throw new InvalidDataException("Shape DocValues record has invalid value or primitive counts.");

                byte[] tree = ShapeDocValuesTreeWriter.Build(primitives);
                if (tree.Length <= 0)
                    throw new InvalidDataException("Shape DocValues component tree cannot be empty.");
                WriteRecordHeader(output, field.Kind, record, primitives, checked((uint)tree.Length));
                output.WriteBytes(tree);
                recordLength = checked(RecordHeaderLength + tree.Length);
            }
            directory[i] = new RecordDirectoryEntry(
                checked((uint)record.DocumentId),
                checked((ulong)recordOffset),
                checked((uint)recordLength));
        }

        long directoryOffset = checked(output.Position - sectionStart);
        foreach (RecordDirectoryEntry entry in directory)
        {
            output.WriteInt32(checked((int)entry.DocumentId));
            output.WriteInt64(checked((long)entry.Offset));
            output.WriteInt32(checked((int)entry.Length));
        }

        output.WriteBytes("SDFT"u8);
        output.WriteInt32(records.Count);
        output.WriteInt64(directoryOffset);
    }

    private static void WriteRecordHeader(
        CodecBodyOutput output,
        SpatialFieldKind fieldKind,
        ShapeDocValuesRecord record,
        ReadOnlyMemory<byte> primitiveBytes,
        uint treeLength)
    {
        ShapeRecordMetadata metadata = ShapeDocValuesMetadata.Compute(fieldKind, record.ValueCount, primitiveBytes);
        output.WriteInt32(checked((int)record.ValueCount));
        output.WriteInt32(record.PrimitiveCount);
        output.WriteByte((byte)metadata.HighestDimension);
        output.WriteByte(metadata.Flags);
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteBytes(metadata.Bounds);
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Accumulator0));
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Accumulator1));
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Accumulator2));
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Weight));
        output.WriteInt32(checked((int)treeLength));
    }

    private static void WriteFieldName(CodecBodyOutput output, string fieldName)
    {
        byte[] encoded;
        try
        {
            encoded = StrictUtf8.GetBytes(fieldName);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Shape DocValues field names must contain valid UTF-16 for strict UTF-8 encoding.", nameof(fieldName), exception);
        }

        if (encoded.Length == 0 || encoded.Length > MaximumFieldNameBytes)
            throw new ArgumentException($"Shape DocValues field names must contain 1 to {MaximumFieldNameBytes} UTF-8 bytes.", nameof(fieldName));
        output.WriteVarInt(encoded.Length);
        output.WriteBytes(encoded);
    }

    private readonly record struct DirectoryEntry(string Name, long Offset, long Length);

    private readonly record struct RecordDirectoryEntry(uint DocumentId, ulong Offset, uint Length);
}
