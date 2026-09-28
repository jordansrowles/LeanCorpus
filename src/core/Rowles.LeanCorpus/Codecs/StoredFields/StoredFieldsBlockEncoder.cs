using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>Encodes the shared v5 stored-field record and index layouts.</summary>
internal static class StoredFieldsBlockEncoder
{
    internal static int GetOrAddFieldId(
        string fieldName,
        Dictionary<string, int> fieldNameToId,
        List<string> fieldNames)
    {
        if (fieldNameToId.TryGetValue(fieldName, out int fieldId))
            return fieldId;

        fieldId = fieldNames.Count;
        fieldNames.Add(fieldName);
        fieldNameToId.Add(fieldName, fieldId);
        return fieldId;
    }

    internal static long GetDictionaryDocumentRawLength<TValues>(
        IReadOnlyDictionary<string, TValues> fields,
        Dictionary<string, int> fieldNameToId,
        List<string> fieldNames)
        where TValues : IReadOnlyCollection<StoredFieldValue>
    {
        long length = sizeof(int);
        foreach (var (name, values) in fields)
        {
            _ = GetOrAddFieldId(name, fieldNameToId, fieldNames);
            length = checked(length + 2L * sizeof(int));
            foreach (var value in values)
                length = checked(length + GetValueRawLength(value));
        }

        return length;
    }

    internal static long GroupFlatDocument(
        List<int> fieldIds,
        List<StoredFieldValue> values,
        IReadOnlyList<string> fieldNames,
        int entryStart,
        int entryEnd,
        List<int> distinctFieldIds,
        int[] fieldCounts,
        int[] fieldEnds,
        int[] groupedEntryIndexes)
    {
        if (entryStart < 0 || entryEnd < entryStart || entryEnd > fieldIds.Count || entryEnd > values.Count)
            throw new InvalidDataException("Stored fields flat document entry range is invalid.");

        Array.Clear(fieldCounts, 0, fieldNames.Count);
        distinctFieldIds.Clear();
        long rawLength = sizeof(int);

        for (int entry = entryStart; entry < entryEnd; entry++)
        {
            int fieldId = fieldIds[entry];
            if ((uint)fieldId >= (uint)fieldNames.Count)
                throw new InvalidDataException(
                    $"Stored fields flat document field ID {fieldId} is outside the {fieldNames.Count}-entry name table.");

            if (fieldCounts[fieldId] == 0)
            {
                distinctFieldIds.Add(fieldId);
                rawLength = checked(rawLength + 2L * sizeof(int));
            }

            fieldCounts[fieldId] = checked(fieldCounts[fieldId] + 1);
            rawLength = checked(rawLength + GetValueRawLength(values[entry]));
        }

        int groupedEnd = 0;
        foreach (int fieldId in distinctFieldIds)
        {
            groupedEnd = checked(groupedEnd + fieldCounts[fieldId]);
            fieldEnds[fieldId] = groupedEnd;
        }

        for (int entry = entryEnd - 1; entry >= entryStart; entry--)
        {
            int fieldId = fieldIds[entry];
            groupedEntryIndexes[--fieldEnds[fieldId]] = entry;
        }

        return rawLength;
    }

    internal static void WriteFlatDocument(
        IBufferWriter<byte> writer,
        List<StoredFieldValue> values,
        List<int> distinctFieldIds,
        int[] fieldCounts,
        int[] groupedEntryIndexes,
        Span<byte> encodeBuffer)
    {
        WriteDocumentHeader(writer, distinctFieldIds.Count);
        int groupedOffset = 0;
        foreach (int fieldId in distinctFieldIds)
        {
            int valueCount = fieldCounts[fieldId];
            WriteFieldHeader(writer, fieldId, valueCount);
            for (int i = 0; i < valueCount; i++)
                WriteValue(writer, values[groupedEntryIndexes[groupedOffset++]], encodeBuffer);
        }
    }

    internal static void WriteDictionaryDocument<TValues>(
        IBufferWriter<byte> writer,
        IReadOnlyDictionary<string, TValues> fields,
        Dictionary<string, int> fieldNameToId,
        List<string> fieldNames,
        Span<byte> encodeBuffer)
        where TValues : IReadOnlyCollection<StoredFieldValue>
    {
        WriteDocumentHeader(writer, fields.Count);
        foreach (var (name, values) in fields)
        {
            int fieldId = GetOrAddFieldId(name, fieldNameToId, fieldNames);
            WriteFieldHeader(writer, fieldId, values.Count);
            foreach (var value in values)
                WriteValue(writer, value, encodeBuffer);
        }
    }

    internal static void WriteIndexBody(
        ISequentialIndexOutput output,
        int blockSize,
        int documentCount,
        List<string> fieldNames,
        List<long> blockOffsets)
    {
        output.WriteInt32(blockSize);
        output.WriteInt32(documentCount);
        WriteFieldNameTable(output, fieldNames);
        output.WriteInt32(blockOffsets.Count);
        foreach (long offset in blockOffsets)
            output.WriteInt64(offset);
    }

    internal static string[] ReadFieldNameTable(IndexInput input, long bodyEnd)
    {
        long remaining = bodyEnd - input.Position;
        if (remaining < 2L * sizeof(int))
            throw new InvalidDataException("Stored fields index field-name table header is truncated.");

        int fieldNameCount = input.ReadInt32();
        remaining = bodyEnd - input.Position;
        if (fieldNameCount < 0 || fieldNameCount > (remaining - sizeof(int)) / sizeof(int))
            throw new InvalidDataException(
                $"Stored fields index field-name count {fieldNameCount} is invalid for its remaining body length.");

        var fieldNames = new string[fieldNameCount];
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < fieldNames.Length; i++)
        {
            if (bodyEnd - input.Position < sizeof(int))
                throw new InvalidDataException("Stored fields index field-name length extends beyond its body.");

            int byteCount = input.ReadInt32();
            if (byteCount < 0 || byteCount > bodyEnd - input.Position)
                throw new InvalidDataException(
                    $"Stored fields index field-name length {byteCount} is outside its remaining body.");

            string name = Encoding.UTF8.GetString(input.ReadBytes(byteCount));
            if (!uniqueNames.Add(name))
                throw new InvalidDataException($"Stored fields index field-name table contains duplicate name '{name}'.");
            fieldNames[i] = name;
        }

        return fieldNames;
    }

    private static void WriteFieldNameTable(ISequentialIndexOutput output, List<string> fieldNames)
    {
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
        output.WriteInt32(fieldNames.Count);
        Span<byte> encodeBuffer = stackalloc byte[512];
        foreach (string name in fieldNames)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (!uniqueNames.Add(name))
                throw new InvalidDataException($"Stored fields field-name table contains duplicate name '{name}'.");

            int byteCount = Encoding.UTF8.GetByteCount(name);
            output.WriteInt32(byteCount);
            Span<byte> bytes = byteCount <= encodeBuffer.Length ? encodeBuffer : new byte[byteCount];
            Encoding.UTF8.GetBytes(name, bytes);
            output.WriteBytes(bytes[..byteCount]);
        }
    }

    private static long GetValueRawLength(StoredFieldValue value)
        => sizeof(byte) + sizeof(int) + (value.IsBinary
            ? value.BinaryValue?.Length ?? 0
            : value.IsLong
                ? sizeof(long)
                : Encoding.UTF8.GetByteCount(value.StringValue ?? string.Empty));

    private static void WriteDocumentHeader(IBufferWriter<byte> writer, int fieldCount)
        => writer.WriteInt32(fieldCount);

    private static void WriteFieldHeader(IBufferWriter<byte> writer, int fieldId, int valueCount)
    {
        writer.WriteInt32(fieldId);
        writer.WriteInt32(valueCount);
    }

    private static void WriteValue(IBufferWriter<byte> writer, StoredFieldValue value, Span<byte> encodeBuffer)
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

        string text = value.StringValue ?? string.Empty;
        int byteCount = Encoding.UTF8.GetByteCount(text);
        Span<byte> valueBytes = byteCount <= encodeBuffer.Length ? encodeBuffer : new byte[byteCount];
        Encoding.UTF8.GetBytes(text, valueBytes);
        writer.WriteInt32(byteCount);
        writer.WriteBytes(valueBytes[..byteCount]);
    }
}
