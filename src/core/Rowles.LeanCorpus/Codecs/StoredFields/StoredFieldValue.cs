namespace Rowles.LeanCorpus.Codecs.StoredFields;

internal enum StoredFieldValueKind : byte
{
    String = 0,
    Binary = 1
}

internal readonly record struct StoredFieldValue
{
    private StoredFieldValue(StoredFieldValueKind kind, string? stringValue, byte[]? binaryValue)
    {
        Kind = kind;
        StringValue = stringValue;
        BinaryValue = binaryValue;
    }

    internal StoredFieldValueKind Kind { get; }

    internal string? StringValue { get; }

    internal byte[]? BinaryValue { get; }

    internal bool IsBinary => Kind == StoredFieldValueKind.Binary;

    internal static StoredFieldValue FromString(string value)
        => new(StoredFieldValueKind.String, value ?? throw new ArgumentNullException(nameof(value)), null);

    internal static StoredFieldValue FromBinary(ReadOnlySpan<byte> value)
        => new(StoredFieldValueKind.Binary, null, value.ToArray());

    internal int EstimatedSize
        => IsBinary
            ? (BinaryValue?.Length ?? 0) + 16
            : (StringValue?.Length ?? 0) * 2 + 16;
}
