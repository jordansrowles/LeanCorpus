using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Builds deterministic Packed BKD trees using bounded selection state.</summary>
internal static class PackedBkdBuilder
{
    private const int MaximumTreeDepth = 64;

    internal static PackedBkdBuiltField Build(
        PackedBkdFieldBuffer source,
        PackedBkdBuildOptions options)
    {
        var config = source.Config;
        int pointCount = source.Count;
        int leafCount = checked((pointCount + config.MaxPointsPerLeaf - 1) / config.MaxPointsPerLeaf);
        int treeDepth = PackedBkdTreeMath.GetTreeDepth(leafCount);
        if (treeDepth > MaximumTreeDepth)
            throw new ArgumentOutOfRangeException(nameof(source), "The Packed BKD tree exceeds the supported depth.");

        long minimumBudget = GetMinimumBudget(config);
        if (options.MemoryBudgetBytes < minimumBudget)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MemoryBudgetBytes,
                $"The packed BKD build budget must accommodate one maximum leaf and its bounded scratch ({minimumBudget} bytes).");

        string spillDirectory = options.SpillDirectory ?? Path.GetTempPath();
        if (!options.ForceSpill && CanFitInMemory(pointCount, leafCount, treeDepth, config, options.MemoryBudgetBytes))
        {
            PackedBkdBuiltField? inMemory = TryBuildInMemory(
                source,
                pointCount,
                leafCount,
                treeDepth,
                options,
                spillDirectory);
            if (inMemory is not null)
                return inMemory;
        }

        return BuildFromSpill(source, pointCount, leafCount, treeDepth, options, spillDirectory);
    }

    internal static int GetKeyByte(
        ReadOnlySpan<byte> records,
        PackedBkdConfig config,
        int splitDimension,
        int keyByte)
    {
        int packedByteIndex = keyByte;
        if (splitDimension >= 0 && keyByte < config.PackedBytesLength)
        {
            if (keyByte < config.BytesPerDimension)
            {
                packedByteIndex = splitDimension * config.BytesPerDimension + keyByte;
            }
            else
            {
                int remaining = keyByte - config.BytesPerDimension;
                packedByteIndex = -1;
                for (int dimension = 0; dimension < config.Dimensions; dimension++)
                {
                    if (dimension == splitDimension)
                        continue;
                    if (remaining < config.BytesPerDimension)
                    {
                        packedByteIndex = dimension * config.BytesPerDimension + remaining;
                        break;
                    }
                    remaining -= config.BytesPerDimension;
                }
                if (packedByteIndex < 0)
                    throw new InvalidOperationException("Packed BKD radix key exceeded its packed dimensions.");
            }
        }

        if (keyByte < config.PackedBytesLength)
            return records[packedByteIndex];

        uint document = unchecked((uint)BinaryPrimitives.ReadInt32LittleEndian(
            records.Slice(config.PackedBytesLength, sizeof(int))));
        int documentByte = keyByte - config.PackedBytesLength;
        return (int)(document >> (24 - documentByte * 8) & 0xff);
    }

    private static PackedBkdBuiltField? TryBuildInMemory(
        PackedBkdFieldBuffer source,
        int pointCount,
        int leafCount,
        int treeDepth,
        PackedBkdBuildOptions options,
        string spillDirectory)
    {
        var config = source.Config;
        var tracker = new PackedBkdBuildMemoryTracker(options.MemoryBudgetBytes);
        int orderCapacity = GetArrayPoolCapacity(pointCount);
        long orderBytes = checked((long)orderCapacity * sizeof(int));
        if (!tracker.TryReserve(orderBytes))
            return null;

        int documentCount;
        try
        {
            if (source.UniqueDocumentCount >= 0)
            {
                documentCount = source.UniqueDocumentCount;
            }
            else
            {
                var recordsForCount = new PackedBkdMemoryRecordSource(source.RecordsMemory, config.RecordBytes);
                documentCount = PackedBkdDocumentCounter.Count(
                    recordsForCount,
                    config,
                    tracker,
                    spillDirectory,
                    options.CancellationToken);
            }
        }
        catch
        {
            tracker.Release(orderBytes);
            throw;
        }

        int[] order = ArrayPool<int>.Shared.Rent(pointCount);
        if (order.Length != orderCapacity)
        {
            tracker.Release(orderBytes);
            long actualOrderBytes = checked((long)order.LongLength * sizeof(int));
            if (!tracker.TryReserve(actualOrderBytes))
            {
                ArrayPool<int>.Shared.Return(order, clearArray: false);
                return null;
            }
            orderBytes = actualOrderBytes;
        }

        PackedBkdBuiltField? built = null;
        try
        {
            ReadOnlyMemory<byte> recordMemory = source.RecordsMemory;
            var records = recordMemory.Span;
            built = new PackedBkdBuiltField(
                config,
                pointCount,
                leafCount,
                treeDepth,
                tracker,
                useLeafFile: false,
                leafDirectory: null);
            ComputeBounds(records, 0, pointCount, config, built.RootMin, built.RootMax, options.CancellationToken);
            built.RootMin.CopyTo(built.BoundsMinimum, 0);
            built.RootMax.CopyTo(built.BoundsMaximum, 0);
            for (int i = 0; i < pointCount; i++)
            {
                if ((i & 0x3ff) == 0)
                    options.CancellationToken.ThrowIfCancellationRequested();
                order[i] = i;
            }

            long parentSplitBytes = checked((long)config.IndexedDimensions * sizeof(int));
            tracker.Reserve(parentSplitBytes);
            var parentSplits = new int[config.IndexedDimensions];
            try
            {
                BuildNode(
                    built,
                    recordMemory,
                    order,
                    0,
                    pointCount,
                    0,
                    leafCount,
                    depth: 0,
                    parentSplits,
                    options.CancellationToken);
            }
            finally
            {
                tracker.Release(parentSplitBytes);
            }
            built.DocumentCount = documentCount;
            return built;
        }
        catch
        {
            built?.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(order, clearArray: false);
            tracker.Release(orderBytes);
        }
    }

    private static PackedBkdBuiltField BuildFromSpill(
        PackedBkdFieldBuffer source,
        int pointCount,
        int leafCount,
        int treeDepth,
        PackedBkdBuildOptions options,
        string spillDirectory)
    {
        FileOpenRetry.CreateDirectory(spillDirectory);
        string path = Path.Combine(spillDirectory, $"packed-bkd-{Guid.NewGuid():N}.spill");
        var tracker = new PackedBkdBuildMemoryTracker(options.MemoryBudgetBytes);
        try
        {
            using (var output = FileOpenRetry.Open(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                const int copyChunkBytes = 64 * 1024;
                ReadOnlySpan<byte> sourceRecords = source.Records;
                for (int offset = 0; offset < sourceRecords.Length; offset += copyChunkBytes)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    int length = Math.Min(copyChunkBytes, sourceRecords.Length - offset);
                    output.Write(sourceRecords.Slice(offset, length));
                }
                output.Flush();
            }

            options.CancellationToken.ThrowIfCancellationRequested();
            using var records = new PackedBkdSpillRecordStore(path, pointCount, source.Config, tracker);
            PackedBkdBuiltField? built = null;
            try
            {
                built = new PackedBkdBuiltField(
                    source.Config,
                    pointCount,
                    leafCount,
                    treeDepth,
                    tracker,
                    useLeafFile: true,
                    spillDirectory);
                ScanBounds(records, 0, pointCount, source.Config, built.RootMin, built.RootMax, options.CancellationToken);
                built.RootMin.CopyTo(built.BoundsMinimum, 0);
                built.RootMax.CopyTo(built.BoundsMaximum, 0);

                int documentCount = source.UniqueDocumentCount >= 0
                    ? source.UniqueDocumentCount
                    : PackedBkdDocumentCounter.Count(
                        records,
                        source.Config,
                        tracker,
                        spillDirectory,
                        options.CancellationToken);

                long parentSplitBytes = checked((long)source.Config.IndexedDimensions * sizeof(int));
                tracker.Reserve(parentSplitBytes);
                var parentSplits = new int[source.Config.IndexedDimensions];
                try
                {
                    BuildSpillNode(
                        built,
                        records,
                        0,
                        pointCount,
                        0,
                        leafCount,
                        depth: 0,
                        parentSplits,
                        options.CancellationToken);
                }
                finally
                {
                    tracker.Release(parentSplitBytes);
                }
                built.DocumentCount = documentCount;
                return built;
            }
            catch
            {
                built?.Dispose();
                throw;
            }
        }
        finally
        {
            try
            {
                FileOpenRetry.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "packed BKD spill cleanup");
            }
        }
    }

    private static void BuildNode(
        PackedBkdBuiltField built,
        ReadOnlyMemory<byte> recordMemory,
        int[] order,
        int start,
        int count,
        int leavesOffset,
        int leafCount,
        int depth,
        int[] parentSplits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> records = recordMemory.Span;
        if (leafCount == 1)
        {
            RadixSortRange(order, records, start, count, built.Config, splitDimension: -1, cancellationToken);
            var source = new PackedBkdOrderedMemoryRecordSource(
                recordMemory,
                order,
                start,
                built.Config.RecordBytes);
            byte[] leaf = PackedBkdLeafEncoder.Encode(
                source,
                0,
                count,
                built.Config,
                GetTracker(built),
                cancellationToken);
            try
            {
                built.SetLeaf(leavesOffset, leaf);
            }
            finally
            {
                GetTracker(built).Release(leaf.Length);
            }
            return;
        }

        Span<byte> minimum = BoundsAtDepth(built.BoundsMinimum, depth, built.Config.IndexedBytesLength);
        Span<byte> maximum = BoundsAtDepth(built.BoundsMaximum, depth, built.Config.IndexedBytesLength);
        if (built.Config.IndexedDimensions > 2 && depth > 0 && depth % 4 == 0)
        {
            ComputeBounds(records, order, start, count, built.Config, built.WorkMin, built.WorkMax, cancellationToken);
            built.WorkMin.AsSpan().CopyTo(minimum);
            built.WorkMax.AsSpan().CopyTo(maximum);
        }

        int splitDimension = SelectSplitDimension(minimum, maximum, built.Config, parentSplits);
        int leftLeaves = PackedBkdTreeMath.GetLeftLeafCount(leafCount);
        int leftCount = Math.Clamp(checked(leftLeaves * built.Config.MaxPointsPerLeaf), 1, count - 1);
        int target = checked(start + leftCount);
        RadixSelectRange(order, records, start, count, target, built.Config, splitDimension, cancellationToken);

        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int splitIndex = checked(rightLeavesOffset - 1);
        built.SplitDimensions[splitIndex] = checked((byte)splitDimension);
        int splitValueOffset = checked(splitIndex * built.Config.BytesPerDimension);
        int recordOffset = checked(order[target] * built.Config.RecordBytes + splitDimension * built.Config.BytesPerDimension);
        records.Slice(recordOffset, built.Config.BytesPerDimension)
            .CopyTo(built.SplitValues.AsSpan(splitValueOffset, built.Config.BytesPerDimension));

        int childDepth = depth + 1;
        Span<byte> leftMinimum = BoundsAtDepth(built.BoundsMinimum, childDepth, built.Config.IndexedBytesLength);
        Span<byte> leftMaximum = BoundsAtDepth(built.BoundsMaximum, childDepth, built.Config.IndexedBytesLength);
        minimum.CopyTo(leftMinimum);
        maximum.CopyTo(leftMaximum);
        int dimensionOffset = checked(splitDimension * built.Config.BytesPerDimension);
        built.SplitValues.AsSpan(splitValueOffset, built.Config.BytesPerDimension)
            .CopyTo(leftMaximum.Slice(dimensionOffset, built.Config.BytesPerDimension));

        parentSplits[splitDimension]++;
        try
        {
            BuildNode(built, recordMemory, order, start, leftCount, leavesOffset, leftLeaves, childDepth, parentSplits, cancellationToken);

            Span<byte> rightMinimum = BoundsAtDepth(built.BoundsMinimum, childDepth, built.Config.IndexedBytesLength);
            Span<byte> rightMaximum = BoundsAtDepth(built.BoundsMaximum, childDepth, built.Config.IndexedBytesLength);
            minimum.CopyTo(rightMinimum);
            maximum.CopyTo(rightMaximum);
            built.SplitValues.AsSpan(splitValueOffset, built.Config.BytesPerDimension)
                .CopyTo(rightMinimum.Slice(dimensionOffset, built.Config.BytesPerDimension));
            BuildNode(built, recordMemory, order, target, count - leftCount, rightLeavesOffset, leafCount - leftLeaves, childDepth, parentSplits, cancellationToken);
        }
        finally
        {
            parentSplits[splitDimension]--;
        }
    }

    private static void BuildSpillNode(
        PackedBkdBuiltField built,
        PackedBkdSpillRecordStore records,
        int start,
        int count,
        int leavesOffset,
        int leafCount,
        int depth,
        int[] parentSplits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (leafCount == 1)
        {
            records.SortRange(start, count, splitDimension: -1, cancellationToken);
            byte[] leaf = PackedBkdLeafEncoder.Encode(
                records,
                start,
                count,
                built.Config,
                GetTracker(built),
                cancellationToken);
            try
            {
                built.SetLeaf(leavesOffset, leaf);
            }
            finally
            {
                GetTracker(built).Release(leaf.Length);
            }
            return;
        }

        Span<byte> minimum = BoundsAtDepth(built.BoundsMinimum, depth, built.Config.IndexedBytesLength);
        Span<byte> maximum = BoundsAtDepth(built.BoundsMaximum, depth, built.Config.IndexedBytesLength);
        if (built.Config.IndexedDimensions > 2 && depth > 0 && depth % 4 == 0)
        {
            ScanBounds(records, start, count, built.Config, built.WorkMin, built.WorkMax, cancellationToken);
            built.WorkMin.AsSpan().CopyTo(minimum);
            built.WorkMax.AsSpan().CopyTo(maximum);
        }

        int splitDimension = SelectSplitDimension(minimum, maximum, built.Config, parentSplits);
        int leftLeaves = PackedBkdTreeMath.GetLeftLeafCount(leafCount);
        int leftCount = Math.Clamp(checked(leftLeaves * built.Config.MaxPointsPerLeaf), 1, count - 1);
        int target = checked(start + leftCount);
        records.SelectRange(start, count, target, splitDimension, cancellationToken);

        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int splitIndex = checked(rightLeavesOffset - 1);
        built.SplitDimensions[splitIndex] = checked((byte)splitDimension);
        Span<byte> splitValue = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        records.ReadDimension(target, splitDimension, splitValue);
        int splitValueOffset = checked(splitIndex * built.Config.BytesPerDimension);
        splitValue[..built.Config.BytesPerDimension]
            .CopyTo(built.SplitValues.AsSpan(splitValueOffset, built.Config.BytesPerDimension));

        int childDepth = depth + 1;
        Span<byte> leftMinimum = BoundsAtDepth(built.BoundsMinimum, childDepth, built.Config.IndexedBytesLength);
        Span<byte> leftMaximum = BoundsAtDepth(built.BoundsMaximum, childDepth, built.Config.IndexedBytesLength);
        minimum.CopyTo(leftMinimum);
        maximum.CopyTo(leftMaximum);
        int dimensionOffset = checked(splitDimension * built.Config.BytesPerDimension);
        splitValue[..built.Config.BytesPerDimension].CopyTo(leftMaximum.Slice(dimensionOffset, built.Config.BytesPerDimension));

        parentSplits[splitDimension]++;
        try
        {
            BuildSpillNode(built, records, start, leftCount, leavesOffset, leftLeaves, childDepth, parentSplits, cancellationToken);
            Span<byte> rightMinimum = BoundsAtDepth(built.BoundsMinimum, childDepth, built.Config.IndexedBytesLength);
            Span<byte> rightMaximum = BoundsAtDepth(built.BoundsMaximum, childDepth, built.Config.IndexedBytesLength);
            minimum.CopyTo(rightMinimum);
            maximum.CopyTo(rightMaximum);
            splitValue[..built.Config.BytesPerDimension].CopyTo(rightMinimum.Slice(dimensionOffset, built.Config.BytesPerDimension));
            BuildSpillNode(built, records, target, count - leftCount, rightLeavesOffset, leafCount - leftLeaves, childDepth, parentSplits, cancellationToken);
        }
        finally
        {
            parentSplits[splitDimension]--;
        }
    }

    private static int SelectSplitDimension(
        ReadOnlySpan<byte> minimum,
        ReadOnlySpan<byte> maximum,
        PackedBkdConfig config,
        ReadOnlySpan<int> parentSplits)
    {
        int selected = -1;
        ulong selectedSpan = 0;
        int maximumSplits = 0;
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            maximumSplits = Math.Max(maximumSplits, parentSplits[dimension]);

        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            ulong span = UnsignedSpan(
                minimum.Slice(dimension * config.BytesPerDimension, config.BytesPerDimension),
                maximum.Slice(dimension * config.BytesPerDimension, config.BytesPerDimension));
            if (span != 0 && parentSplits[dimension] < maximumSplits / 2)
                return dimension;
        }

        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = dimension * config.BytesPerDimension;
            ulong span = UnsignedSpan(
                minimum.Slice(offset, config.BytesPerDimension),
                maximum.Slice(offset, config.BytesPerDimension));
            if (selected < 0 || span > selectedSpan)
            {
                selected = dimension;
                selectedSpan = span;
            }
        }
        return Math.Max(0, selected);
    }

    private static ulong UnsignedSpan(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        => BinaryPrimitives.ReadUInt32BigEndian(maximum) - BinaryPrimitives.ReadUInt32BigEndian(minimum);

    private static void RadixSelectRange(
        int[] order,
        ReadOnlySpan<byte> records,
        int start,
        int count,
        int target,
        PackedBkdConfig config,
        int splitDimension,
        CancellationToken cancellationToken,
        int keyByte = 0)
    {
        int keyLength = checked(config.PackedBytesLength + sizeof(int));
        if (count < 2 || keyByte >= keyLength)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        Span<int> counts = stackalloc int[256];
        Span<int> starts = stackalloc int[256];
        Span<int> next = stackalloc int[256];
        counts.Clear();
        for (int i = start; i < start + count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            counts[GetKeyByte(records.Slice(order[i] * config.RecordBytes), config, splitDimension, keyByte)]++;
        }
        PartitionOrder(order, records, start, count, config, splitDimension, keyByte, counts, starts, next, cancellationToken);
        int bucket = 0;
        while (target >= starts[bucket] + counts[bucket])
            bucket++;
        if (counts[bucket] > 1)
            RadixSelectRange(order, records, starts[bucket], counts[bucket], target, config, splitDimension, cancellationToken, keyByte + 1);
    }

    private static void RadixSortRange(
        int[] order,
        ReadOnlySpan<byte> records,
        int start,
        int count,
        PackedBkdConfig config,
        int splitDimension,
        CancellationToken cancellationToken,
        int keyByte = 0)
    {
        int keyLength = checked(config.PackedBytesLength + sizeof(int));
        if (count < 2 || keyByte >= keyLength)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        Span<int> counts = stackalloc int[256];
        Span<int> starts = stackalloc int[256];
        Span<int> next = stackalloc int[256];
        counts.Clear();
        for (int i = start; i < start + count; i++)
        {
            if (((i - start) & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            counts[GetKeyByte(records.Slice(order[i] * config.RecordBytes), config, splitDimension, keyByte)]++;
        }
        PartitionOrder(order, records, start, count, config, splitDimension, keyByte, counts, starts, next, cancellationToken);
        for (int bucket = 0; bucket < counts.Length; bucket++)
            if (counts[bucket] > 1)
                RadixSortRange(order, records, starts[bucket], counts[bucket], config, splitDimension, cancellationToken, keyByte + 1);
    }

    private static void PartitionOrder(
        int[] order,
        ReadOnlySpan<byte> records,
        int start,
        int count,
        PackedBkdConfig config,
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
                int recordIndex = order[position];
                int recordBucket = GetKeyByte(records.Slice(recordIndex * config.RecordBytes), config, splitDimension, keyByte);
                if (recordBucket == bucket)
                {
                    next[bucket]++;
                    continue;
                }
                int target = next[recordBucket]++;
                (order[position], order[target]) = (order[target], order[position]);
            }
        }
    }

    private static void ComputeBounds(
        ReadOnlySpan<byte> records,
        int start,
        int count,
        PackedBkdConfig config,
        byte[] minimum,
        byte[] maximum,
        CancellationToken cancellationToken)
    {
        int firstRecord = checked(start * config.RecordBytes);
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = checked(dimension * config.BytesPerDimension);
            records.Slice(firstRecord + offset, config.BytesPerDimension).CopyTo(minimum.AsSpan(offset, config.BytesPerDimension));
            records.Slice(firstRecord + offset, config.BytesPerDimension).CopyTo(maximum.AsSpan(offset, config.BytesPerDimension));
        }
        for (int point = 1; point < count; point++)
        {
            if ((point & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int recordOffset = checked((start + point) * config.RecordBytes);
            UpdateBounds(records.Slice(recordOffset), config, minimum, maximum);
        }
    }

    private static void ComputeBounds(
        ReadOnlySpan<byte> records,
        int[] order,
        int start,
        int count,
        PackedBkdConfig config,
        byte[] minimum,
        byte[] maximum,
        CancellationToken cancellationToken)
    {
        int firstRecord = checked(order[start] * config.RecordBytes);
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = checked(dimension * config.BytesPerDimension);
            records.Slice(firstRecord + offset, config.BytesPerDimension).CopyTo(minimum.AsSpan(offset, config.BytesPerDimension));
            records.Slice(firstRecord + offset, config.BytesPerDimension).CopyTo(maximum.AsSpan(offset, config.BytesPerDimension));
        }
        for (int i = 1; i < count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int recordOffset = checked(order[start + i] * config.RecordBytes);
            UpdateBounds(records.Slice(recordOffset), config, minimum, maximum);
        }
    }

    private static void ScanBounds(
        PackedBkdSpillRecordStore records,
        int start,
        int count,
        PackedBkdConfig config,
        byte[] minimum,
        byte[] maximum,
        CancellationToken cancellationToken)
    {
        Span<byte> record = stackalloc byte[config.RecordBytes];
        records.Read(start, record);
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = checked(dimension * config.BytesPerDimension);
            record.Slice(offset, config.BytesPerDimension).CopyTo(minimum.AsSpan(offset, config.BytesPerDimension));
            record.Slice(offset, config.BytesPerDimension).CopyTo(maximum.AsSpan(offset, config.BytesPerDimension));
        }
        for (int i = 1; i < count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            records.Read(start + i, record);
            UpdateBounds(record, config, minimum, maximum);
        }
    }

    private static void UpdateBounds(
        ReadOnlySpan<byte> record,
        PackedBkdConfig config,
        byte[] minimum,
        byte[] maximum)
    {
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = checked(dimension * config.BytesPerDimension);
            ReadOnlySpan<byte> value = record.Slice(offset, config.BytesPerDimension);
            if (value.SequenceCompareTo(minimum.AsSpan(offset, config.BytesPerDimension)) < 0)
                value.CopyTo(minimum.AsSpan(offset, config.BytesPerDimension));
            if (value.SequenceCompareTo(maximum.AsSpan(offset, config.BytesPerDimension)) > 0)
                value.CopyTo(maximum.AsSpan(offset, config.BytesPerDimension));
        }
    }

    private static Span<byte> BoundsAtDepth(byte[] bounds, int depth, int bytesPerNode)
        => bounds.AsSpan(checked(depth * bytesPerNode), bytesPerNode);

    private static PackedBkdBuildMemoryTracker GetTracker(PackedBkdBuiltField built)
        => built.Tracker;

    private static int GetArrayPoolCapacity(int requested)
    {
        if (requested <= 16)
            return 16;
        if (requested <= 1_048_576)
        {
            int capacity = 16;
            while (capacity < requested)
                capacity <<= 1;
            return capacity;
        }
        return requested;
    }

    private static bool CanFitInMemory(
        int pointCount,
        int leafCount,
        int treeDepth,
        PackedBkdConfig config,
        long budget)
    {
        try
        {
            long order = checked((long)GetArrayPoolCapacity(pointCount) * sizeof(int));
            long metadata = checked(
                2L * config.IndexedBytesLength
                + Math.Max(0, leafCount - 1)
                + (long)Math.Max(0, leafCount - 1) * config.BytesPerDimension
                + 2L * config.IndexedBytesLength
                + 2L * (treeDepth + 1) * config.IndexedBytesLength
                + (long)(leafCount + 1) * sizeof(long));
            long maximumHeader = checked(
                sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(int)
                + (long)config.IndexedBytesLength * 2
                + config.Dimensions + config.PackedBytesLength);
            long leafData = checked((long)pointCount * (config.PackedBytesLength + sizeof(int))
                + leafCount * maximumHeader);
            long maximumLeaf = checked(maximumHeader + (long)config.MaxPointsPerLeaf * (config.PackedBytesLength + sizeof(int)));
            long parentSplits = checked((long)config.IndexedDimensions * sizeof(int));
            long documentScratch = 2L * sizeof(int);
            return checked(order + metadata + leafData + maximumLeaf + parentSplits + documentScratch) <= budget;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static long GetMinimumBudget(PackedBkdConfig config)
    {
        long maximumHeader = checked(
            sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(int)
            + (long)config.IndexedBytesLength * 2
            + config.Dimensions + config.PackedBytesLength);
        long maximumLeaf = checked(maximumHeader
            + (long)config.MaxPointsPerLeaf * (config.PackedBytesLength + sizeof(int)));
        return checked(maximumLeaf + 3L * config.RecordBytes + config.MaxPointsPerLeaf * sizeof(int) + 128);
    }
}
