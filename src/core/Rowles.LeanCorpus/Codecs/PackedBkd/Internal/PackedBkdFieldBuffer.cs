using System.Buffers;
using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>DWPT-owned fixed-width packed values for one multidimensional field.</summary>
internal sealed class PackedBkdFieldBuffer : IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private byte[]? _records;
    private int _count;
    private int _lastDocId = -1;
    private int _uniqueDocumentCount;
    private bool _docIdsAreOrdered = true;
    private int _disposed;

    internal PackedBkdFieldBuffer(PackedBkdConfig config, ArrayPool<byte>? pool = null)
    {
        Config = config;
        _pool = pool ?? ArrayPool<byte>.Shared;
        _records = _pool.Rent(checked(config.RecordBytes * 16));
    }

    internal PackedBkdConfig Config { get; }

    internal int Count => _count;

    internal long AllocatedBytes => Volatile.Read(ref _disposed) == 0 ? _records?.LongLength ?? 0 : 0;

    internal bool DocumentIdsAreOrdered => _docIdsAreOrdered;

    internal int UniqueDocumentCount => _uniqueDocumentCount;

    internal void Append(ReadOnlySpan<byte> packedValue, int docId)
    {
        ObjectDisposedException.ThrowIf(_records is null, this);
        if (packedValue.Length != Config.PackedBytesLength)
            throw new ArgumentException($"Packed value length must be {Config.PackedBytesLength} bytes.", nameof(packedValue));
        ArgumentOutOfRangeException.ThrowIfNegative(docId);

        int offset = checked(_count * Config.RecordBytes);
        EnsureCapacity(checked(offset + Config.RecordBytes));
        packedValue.CopyTo(_records.AsSpan(offset, Config.PackedBytesLength));
        BinaryPrimitives.WriteInt32LittleEndian(_records.AsSpan(offset + Config.PackedBytesLength, sizeof(int)), docId);
        if (_count > 0 && docId < _lastDocId)
        {
            _docIdsAreOrdered = false;
            _uniqueDocumentCount = -1;
        }
        if (_count == 0 || docId != _lastDocId)
        {
            if (_uniqueDocumentCount >= 0 && _docIdsAreOrdered)
                _uniqueDocumentCount++;
        }
        _lastDocId = docId;
        _count++;
    }

    internal void RemapDocumentIds(ReadOnlySpan<int> inversePermutation)
    {
        ObjectDisposedException.ThrowIf(_records is null, this);
        for (int i = 0; i < _count; i++)
        {
            int offset = i * Config.RecordBytes + Config.PackedBytesLength;
            int oldDocId = BinaryPrimitives.ReadInt32LittleEndian(_records.AsSpan(offset, sizeof(int)));
            if ((uint)oldDocId >= (uint)inversePermutation.Length)
                throw new InvalidDataException("A packed BKD document ID is outside the index-sort permutation.");
            BinaryPrimitives.WriteInt32LittleEndian(_records.AsSpan(offset, sizeof(int)), inversePermutation[oldDocId]);
        }
        _docIdsAreOrdered = false;
    }

    internal ReadOnlySpan<byte> Records
    {
        get
        {
            ObjectDisposedException.ThrowIf(_records is null, this);
            return _records.AsSpan(0, checked(_count * Config.RecordBytes));
        }
    }

    internal ReadOnlyMemory<byte> RecordsMemory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_records is null, this);
            return _records.AsMemory(0, checked(_count * Config.RecordBytes));
        }
    }

    internal int GetDocId(ReadOnlySpan<byte> record)
        => BinaryPrimitives.ReadInt32LittleEndian(record.Slice(Config.PackedBytesLength, sizeof(int)));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var records = Interlocked.Exchange(ref _records, null);
        if (records is not null)
            _pool.Return(records, clearArray: false);
        _count = 0;
        _lastDocId = -1;
        _uniqueDocumentCount = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (_records!.Length >= required)
            return;

        int nextLength = Math.Max(required, checked(_records.Length * 2));
        var replacement = _pool.Rent(nextLength);
        _records.AsSpan(0, checked(_count * Config.RecordBytes)).CopyTo(replacement);
        _pool.Return(_records, clearArray: false);
        _records = replacement;
    }
}
