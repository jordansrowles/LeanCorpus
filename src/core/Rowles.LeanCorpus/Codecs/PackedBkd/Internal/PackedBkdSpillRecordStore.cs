using System.Buffers.Binary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Provides bounded fixed-record temporary storage for spill construction.</summary>
internal sealed class PackedBkdSpillRecordStore : IDisposable, IPackedBkdRecordSource
{
    private readonly Stream _stream;
    private readonly PackedBkdConfig _config;
    private readonly PackedBkdBuildMemoryTracker _tracker;
    private readonly byte[] _left;
    private readonly byte[] _right;
    private readonly byte[] _swap;
    private readonly long _scratchBytes;
    private bool _disposed;

    internal PackedBkdSpillRecordStore(
        string path,
        int count,
        PackedBkdConfig config,
        PackedBkdBuildMemoryTracker tracker)
    {
        _stream = FileOpenRetry.Open(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.RandomAccess);
        _config = config;
        _tracker = tracker;
        Count = count;
        _scratchBytes = checked((long)config.RecordBytes * 3);
        bool scratchReserved = false;
        try
        {
            tracker.Reserve(_scratchBytes);
            scratchReserved = true;
            _left = new byte[config.RecordBytes];
            _right = new byte[config.RecordBytes];
            _swap = new byte[config.RecordBytes];
            long expectedLength = checked((long)count * config.RecordBytes);
            if (_stream.Length != expectedLength)
                throw new InvalidDataException("The packed BKD spill record file has an invalid length.");
        }
        catch
        {
            _stream.Dispose();
            if (scratchReserved)
                tracker.Release(_scratchBytes);
            throw;
        }
    }

    public int Count { get; }

    public void Read(int index, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (destination.Length < _config.RecordBytes)
            throw new ArgumentException("The destination is shorter than one packed BKD record.", nameof(destination));
        _stream.Position = checked((long)index * _config.RecordBytes);
        _stream.ReadExactly(destination[.._config.RecordBytes]);
    }

    internal void ReadDimension(int index, int dimension, Span<byte> destination)
    {
        Read(index, _left);
        int offset = checked(dimension * _config.BytesPerDimension);
        _left.AsSpan(offset, _config.BytesPerDimension).CopyTo(destination);
    }

    internal void SelectRange(
        int start,
        int count,
        int target,
        int splitDimension,
        CancellationToken cancellationToken)
        => RadixSelectRange(start, count, target, splitDimension, keyByte: 0, cancellationToken);

    internal void SortRange(
        int start,
        int count,
        int splitDimension,
        CancellationToken cancellationToken)
        => RadixSortRange(start, count, splitDimension, keyByte: 0, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
        _tracker.Release(_scratchBytes);
    }

    private void RadixSelectRange(
        int start,
        int count,
        int target,
        int splitDimension,
        int keyByte,
        CancellationToken cancellationToken)
    {
        int keyLength = checked(_config.PackedBytesLength + sizeof(int));
        if (count < 2 || keyByte >= keyLength)
            return;
        if (target < start || target >= start + count)
            throw new ArgumentOutOfRangeException(nameof(target));

        cancellationToken.ThrowIfCancellationRequested();
        Span<int> counts = stackalloc int[256];
        Span<int> starts = stackalloc int[256];
        Span<int> next = stackalloc int[256];
        BuildHistogram(start, count, splitDimension, keyByte, counts, cancellationToken);
        Partition(start, count, splitDimension, keyByte, counts, starts, next, cancellationToken);

        int bucket = 0;
        while (target >= starts[bucket] + counts[bucket])
            bucket++;
        if (counts[bucket] > 1)
            RadixSelectRange(starts[bucket], counts[bucket], target, splitDimension, keyByte + 1, cancellationToken);
    }

    private void RadixSortRange(
        int start,
        int count,
        int splitDimension,
        int keyByte,
        CancellationToken cancellationToken)
    {
        int keyLength = checked(_config.PackedBytesLength + sizeof(int));
        if (count < 2 || keyByte >= keyLength)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        Span<int> counts = stackalloc int[256];
        Span<int> starts = stackalloc int[256];
        Span<int> next = stackalloc int[256];
        BuildHistogram(start, count, splitDimension, keyByte, counts, cancellationToken);
        Partition(start, count, splitDimension, keyByte, counts, starts, next, cancellationToken);
        for (int bucket = 0; bucket < counts.Length; bucket++)
        {
            if (counts[bucket] > 1)
                RadixSortRange(starts[bucket], counts[bucket], splitDimension, keyByte + 1, cancellationToken);
        }
    }

    private void BuildHistogram(
        int start,
        int count,
        int splitDimension,
        int keyByte,
        Span<int> counts,
        CancellationToken cancellationToken)
    {
        counts.Clear();
        for (int i = 0; i < count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            Read(start + i, _left);
            counts[PackedBkdBuilder.GetKeyByte(_left, _config, splitDimension, keyByte)]++;
        }
    }

    private void Partition(
        int start,
        int count,
        int splitDimension,
        int keyByte,
        ReadOnlySpan<int> counts,
        Span<int> starts,
        Span<int> next,
        CancellationToken cancellationToken)
    {
        int offset = start;
        for (int bucket = 0; bucket < counts.Length; bucket++)
        {
            starts[bucket] = offset;
            next[bucket] = offset;
            offset += counts[bucket];
        }

        for (int bucket = 0; bucket < counts.Length; bucket++)
        {
            int end = starts[bucket] + counts[bucket];
            while (next[bucket] < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int position = next[bucket];
                Read(position, _left);
                int recordBucket = PackedBkdBuilder.GetKeyByte(_left, _config, splitDimension, keyByte);
                if (recordBucket == bucket)
                {
                    next[bucket]++;
                    continue;
                }

                int target = next[recordBucket]++;
                Read(target, _right);
                _left.CopyTo(_swap, 0);
                Write(target, _swap);
                Write(position, _right);
            }
        }
    }

    private void Write(int index, ReadOnlySpan<byte> source)
    {
        _stream.Position = checked((long)index * _config.RecordBytes);
        _stream.Write(source[.._config.RecordBytes]);
    }
}
