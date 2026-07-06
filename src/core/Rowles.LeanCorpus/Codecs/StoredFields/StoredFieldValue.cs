using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

internal enum StoredFieldValueKind : byte
{
    String = 0,
    Binary = 1,
    Long = 2
}

internal readonly record struct StoredFieldValue
{
    private StoredFieldValue(StoredFieldValueKind kind, string? stringValue, byte[]? binaryValue, long longValue)
    {
        Kind = kind;
        StringValue = stringValue;
        BinaryValue = binaryValue;
        LongValue = longValue;
    }

    internal StoredFieldValueKind Kind { get; }

    internal string? StringValue { get; }

    internal byte[]? BinaryValue { get; }

    internal long LongValue { get; }

    internal bool IsBinary => Kind == StoredFieldValueKind.Binary;

    internal bool IsLong => Kind == StoredFieldValueKind.Long;

    internal static StoredFieldValue FromString(string value)
        => new(StoredFieldValueKind.String, value ?? throw new ArgumentNullException(nameof(value)), null, 0);

    internal static StoredFieldValue FromBinary(ReadOnlySpan<byte> value)
        => new(StoredFieldValueKind.Binary, null, value.ToArray(), 0);

    internal static StoredFieldValue FromLong(long value)
        => new(StoredFieldValueKind.Long, null, null, value);

    internal int EstimatedSize
        => Kind switch
        {
            StoredFieldValueKind.Binary => (BinaryValue?.Length ?? 0) + 16,
            StoredFieldValueKind.Long => 24,
            _ => (StringValue?.Length ?? 0) * 2 + 16
        };

    internal ReadOnlySpan<byte> GetEncodedBytes()
    {
        if (Kind == StoredFieldValueKind.Binary)
            return BinaryValue ?? [];

        if (Kind == StoredFieldValueKind.Long)
        {
            byte[] bytes = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, LongValue);
            return bytes;
        }

        return [];
    }
}
