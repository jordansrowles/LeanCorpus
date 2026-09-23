namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

internal readonly struct PackedBkdOrderedMemoryRecordSource : IPackedBkdRecordSource
{
    private readonly ReadOnlyMemory<byte> _records;
    private readonly int[] _order;
    private readonly int _baseIndex;
    private readonly int _recordBytes;

    internal PackedBkdOrderedMemoryRecordSource(
        ReadOnlyMemory<byte> records,
        int[] order,
        int baseIndex,
        int recordBytes)
    {
        _records = records;
        _order = order;
        _baseIndex = baseIndex;
        _recordBytes = recordBytes;
    }

    public int Count => _order.Length - _baseIndex;

    public void Read(int index, Span<byte> destination)
        => _records.Span.Slice(
            checked(_order[_baseIndex + index] * _recordBytes),
            _recordBytes).CopyTo(destination);
}
