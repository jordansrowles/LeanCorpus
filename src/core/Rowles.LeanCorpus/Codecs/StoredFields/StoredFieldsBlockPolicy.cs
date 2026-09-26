using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Shared limits for the stored-fields v4 block layout. Keep these values stable
/// for a body version because they define which blocks its reader accepts.
/// </summary>
internal static class StoredFieldsBlockPolicy
{
    internal const int TargetRawBytes = 1024 * 1024;
    internal const int MaximumRawBytes = 256 * 1024 * 1024;
    internal const int MaximumDocumentCount = 100_000;

    internal static long GetDocumentRawLength<TValues>(IReadOnlyDictionary<string, TValues> fields)
        where TValues : IReadOnlyCollection<StoredFieldValue>
    {
        long length = sizeof(int);
        foreach (var (name, values) in fields)
        {
            length = checked(length + sizeof(int) + Encoding.UTF8.GetByteCount(name) + sizeof(int));
            foreach (var value in values)
                length = checked(length + sizeof(byte) + sizeof(int) + GetValuePayloadLength(value));
        }

        return length;
    }

    internal static long GetFlatDocumentRawLength(
        List<int> fieldIds,
        List<StoredFieldValue> values,
        List<string> fieldNames,
        int entryStart,
        int entryEnd,
        List<int> distinctFieldIds)
    {
        long length = sizeof(int);
        foreach (int fieldId in distinctFieldIds)
        {
            length = checked(length + sizeof(int) + Encoding.UTF8.GetByteCount(fieldNames[fieldId]) + sizeof(int));
            for (int entry = entryStart; entry < entryEnd; entry++)
            {
                if (fieldIds[entry] == fieldId)
                    length = checked(length + sizeof(byte) + sizeof(int) + GetValuePayloadLength(values[entry]));
            }
        }

        return length;
    }

    internal static int GetValuePayloadLength(StoredFieldValue value)
        => value.IsBinary
            ? value.BinaryValue?.Length ?? 0
            : value.IsLong
                ? sizeof(long)
                : Encoding.UTF8.GetByteCount(value.StringValue ?? string.Empty);

    internal static void ValidateRawLength(long rawLength)
    {
        if (rawLength < 0 || rawLength > MaximumRawBytes)
            throw new InvalidDataException(
                $"Stored fields raw length {rawLength} exceeds the maximum block size {MaximumRawBytes}.");
    }

    internal static bool IsValidMaximumDocumentCount(int maximumDocumentCount)
        => maximumDocumentCount is >= 1 and <= MaximumDocumentCount;

    internal static void ValidateMaximumDocumentCount(int maximumDocumentCount, string? parameterName = null)
    {
        if (!IsValidMaximumDocumentCount(maximumDocumentCount))
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(maximumDocumentCount), maximumDocumentCount,
                $"Stored fields block document limit must be in the range [1, {MaximumDocumentCount}].");
    }

    internal static bool ShouldFlushBeforeAdd(
        int documentCount,
        int rawBytes,
        long nextDocumentRawBytes,
        int maximumDocumentCount)
        => documentCount > 0 &&
            (documentCount >= maximumDocumentCount || nextDocumentRawBytes > TargetRawBytes - (long)rawBytes);

    internal static bool ShouldFlushAfterAdd(int documentCount, int rawBytes, int maximumDocumentCount)
        => documentCount >= maximumDocumentCount || rawBytes >= TargetRawBytes;
}

internal static class StoredFieldsBlockSerializer
{
    internal static void WriteDocument<TValues>(
        IBufferWriter<byte> writer,
        IReadOnlyDictionary<string, TValues> fields,
        Span<byte> encodeBuffer)
        where TValues : IReadOnlyCollection<StoredFieldValue>
    {
        writer.WriteInt32(fields.Count);
        foreach (var (name, values) in fields)
        {
            int nameByteCount = Encoding.UTF8.GetByteCount(name);
            Span<byte> nameBuffer = nameByteCount <= encodeBuffer.Length ? encodeBuffer : new byte[nameByteCount];
            Encoding.UTF8.GetBytes(name, nameBuffer);
            writer.WriteInt32(nameByteCount);
            writer.WriteBytes(nameBuffer[..nameByteCount]);

            writer.WriteInt32(values.Count);
            foreach (var value in values)
                WriteValue(writer, value, encodeBuffer);
        }
    }

    internal static void WriteValue(IBufferWriter<byte> writer, StoredFieldValue value, Span<byte> encodeBuffer)
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
        Span<byte> valueBuffer = valueByteCount <= encodeBuffer.Length ? encodeBuffer : new byte[valueByteCount];
        Encoding.UTF8.GetBytes(text, valueBuffer);
        writer.WriteInt32(valueByteCount);
        writer.WriteBytes(valueBuffer[..valueByteCount]);
    }
}
