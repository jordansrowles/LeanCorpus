namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

internal readonly struct PackedBkdMemoryRecordSource : IPackedBkdRecordSource
{
    private readonly ReadOnlyMemory<byte> _records;
    private readonly int _recordBytes;

    internal PackedBkdMemoryRecordSource(ReadOnlyMemory<byte> records, int recordBytes)
    {
        _records = records;
        _recordBytes = recordBytes;
    }

    public int Count => _records.Length / _recordBytes;

    public void Read(int index, Span<byte> destination)
        => _records.Span.Slice(checked(index * _recordBytes), _recordBytes).CopyTo(destination);
}
