using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Owns one completed Packed BKD field before physical emission.</summary>
internal sealed class PackedBkdBuiltField : IDisposable
{
    private readonly PackedBkdBuildMemoryTracker _tracker;
    private readonly PackedBkdLeafDataStore? _leafDataStore;
    private readonly MemoryStream? _leafData;
    private readonly long[]? _leafOffsets;
    private readonly long _metadataBytes;
    private readonly long _leafCapacityBytes;
    private readonly long _boundsBytes;
    private int _nextLeaf;
    private int _disposed;

    internal PackedBkdBuiltField(
        PackedBkdConfig config,
        int pointCount,
        int leafCount,
        int maximumTreeDepth,
        PackedBkdBuildMemoryTracker tracker,
        bool useLeafFile,
        string? leafDirectory)
    {
        Config = config;
        PointCount = pointCount;
        LeafCount = leafCount;
        _tracker = tracker;

        RootMin = AllocateBytes(config.IndexedBytesLength);
        RootMax = AllocateBytes(config.IndexedBytesLength);
        SplitDimensions = AllocateBytes(Math.Max(0, leafCount - 1));
        SplitValues = AllocateBytes(checked(Math.Max(0, leafCount - 1) * config.BytesPerDimension));
        WorkMin = AllocateBytes(config.IndexedBytesLength);
        WorkMax = AllocateBytes(config.IndexedBytesLength);
        BoundsMinimum = AllocateBytes(checked((maximumTreeDepth + 1) * config.IndexedBytesLength));
        BoundsMaximum = AllocateBytes(checked((maximumTreeDepth + 1) * config.IndexedBytesLength));
        _boundsBytes = checked((long)BoundsMinimum.LongLength + BoundsMaximum.LongLength);
        _metadataBytes = checked(
            (long)RootMin.LongLength + RootMax.LongLength
            + SplitDimensions.LongLength + SplitValues.LongLength
            + WorkMin.LongLength + WorkMax.LongLength
            + _boundsBytes);

        if (useLeafFile)
        {
            if (string.IsNullOrWhiteSpace(leafDirectory))
                throw new ArgumentException("A leaf spill directory is required.", nameof(leafDirectory));
            _leafDataStore = new PackedBkdLeafDataStore(leafDirectory, leafCount, tracker);
        }
        else
        {
            int capacity = GetLeafDataCapacity(pointCount, leafCount, config);
            _leafCapacityBytes = capacity;
            tracker.Reserve(capacity);
            _leafData = new MemoryStream(capacity);
            _leafOffsets = AllocateLongs(checked(leafCount + 1));
            _metadataBytes = checked(_metadataBytes + (long)_leafOffsets.LongLength * sizeof(long));
        }
    }

    internal PackedBkdConfig Config { get; }
    internal int PointCount { get; }
    internal int LeafCount { get; }
    internal bool UsedSpill => _leafDataStore is not null;
    internal int DocumentCount { get; set; }
    internal byte[] RootMin { get; }
    internal byte[] RootMax { get; }
    internal byte[] SplitDimensions { get; }
    internal byte[] SplitValues { get; }
    internal byte[] WorkMin { get; }
    internal byte[] WorkMax { get; }
    internal byte[] BoundsMinimum { get; }
    internal byte[] BoundsMaximum { get; }
    internal long PeakBuildBytes => _tracker.PeakBytes;

    internal PackedBkdBuildMemoryTracker Tracker => _tracker;

    internal void SetLeaf(int index, ReadOnlySpan<byte> leaf)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if ((uint)index >= (uint)LeafCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (_leafDataStore is not null)
        {
            _leafDataStore.Write(index, leaf);
            return;
        }

        if (index != _nextLeaf)
            throw new InvalidOperationException("Packed BKD leaves must be produced in leaf order.");
        _leafOffsets![index] = _leafData!.Position;
        _leafData.Write(leaf);
        _nextLeaf++;
        _leafOffsets[_nextLeaf] = _leafData.Position;
    }

    internal long[] GetLeafOffsets()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_leafDataStore is not null)
            return _leafDataStore.Offsets;
        if (_nextLeaf != LeafCount)
            throw new InvalidOperationException("Packed BKD leaf data is incomplete.");
        return _leafOffsets!;
    }

    internal void WriteLeafData(CodecBodyOutput output)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_leafDataStore is not null)
        {
            _leafDataStore.WriteTo(output);
            return;
        }
        if (_nextLeaf != LeafCount)
            throw new InvalidOperationException("Packed BKD leaf data is incomplete.");
        _leafData!.Position = 0;
        using Stream destination = output.AsStream();
        _leafData.CopyTo(destination);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _leafDataStore?.Dispose();
        _leafData?.Dispose();
        if (_leafCapacityBytes != 0)
            _tracker.Release(_leafCapacityBytes);
        _tracker.Release(_metadataBytes);
    }

    private byte[] AllocateBytes(int length)
    {
        _tracker.Reserve(length);
        return new byte[length];
    }

    private long[] AllocateLongs(int length)
    {
        long bytes = checked((long)length * sizeof(long));
        _tracker.Reserve(bytes);
        return new long[length];
    }

    private static int GetLeafDataCapacity(int pointCount, int leafCount, PackedBkdConfig config)
    {
        long maximumHeader = checked(
            sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(int)
            + (long)config.IndexedBytesLength * 2
            + config.Dimensions + config.PackedBytesLength);
        long capacity = checked((long)pointCount * (config.PackedBytesLength + sizeof(int))
            + leafCount * maximumHeader);
        return checked((int)capacity);
    }
}
