using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Writes deterministic multidimensional packed BKD field sections.</summary>
internal static class PackedBkdWriter
{
    private const uint FieldMagic = 0x3146_4250; // PBF1
    internal const uint FooterMagic = 0x444b_4250; // PBKD
    private const byte RawValues = 0;
    private const byte PrefixValues = 1;

    internal static void Write(
        string filePath,
        IReadOnlyDictionary<string, PackedBkdFieldBuffer> fields,
        PackedBkdBuildOptions options = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(fields);
        if (options.MemoryBudgetBytes == 0)
            options = new PackedBkdBuildOptions(16L * 1024 * 1024);
        options.Validate();
        if (options.SpillDirectory is null)
            options = options with
            {
                SpillDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Path.GetTempPath()
            };

        var names = fields
            .Where(static pair => pair.Value.Count > 0)
            .Select(static pair => pair.Key)
            .ToArray();
        Array.Sort(names, PackedBkdFieldNameComparer.Instance);
        for (int i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrEmpty(names[i]))
                throw new ArgumentException("Packed BKD field names must not be empty.", nameof(fields));
            if (i > 0 && PackedBkdFieldNameComparer.Instance.Compare(names[i - 1], names[i]) == 0)
                throw new ArgumentException($"Packed BKD field name '{names[i]}' occurs more than once.", nameof(fields));
        }

        CodecFileWriter.WriteAtomically(
            filePath,
            PackedBkdCodecFiles.Descriptor,
            durable: false,
            output =>
            {
                long bodyStart = output.Position;
                var directory = new DirectoryEntry[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var field = fields[names[i]];
                    long fieldOffset = checked(output.Position - bodyStart);
                    var built = BuildField(field, options);
                    WriteField(output, built);
                    directory[i] = new DirectoryEntry(names[i], fieldOffset, checked(output.Position - bodyStart - fieldOffset));
                }

                long directoryOffset = checked(output.Position - bodyStart);
                output.WriteInt32(names.Length);
                foreach (var entry in directory)
                {
                    WriteString(output, entry.Name);
                    output.WriteInt64(entry.Offset);
                    output.WriteInt64(entry.Length);
                }

                output.WriteInt32(unchecked((int)FooterMagic));
                output.WriteInt32(names.Length);
                output.WriteInt64(directoryOffset);
            });
    }

    private static BuiltField BuildField(PackedBkdFieldBuffer source, PackedBkdBuildOptions options)
    {
        var config = source.Config;
        long minimumBudget = checked((long)config.MaxPointsPerLeaf * config.RecordBytes
            + (long)config.MaxPointsPerLeaf * sizeof(int) + 256);
        if (options.MemoryBudgetBytes < minimumBudget)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MemoryBudgetBytes,
                $"The packed BKD build budget must accommodate one maximum leaf and its ordering scratch ({minimumBudget} bytes).");

        long estimatedInMemoryBuildBytes = EstimateInMemoryBuildBytes(source.Count, config);
        if (options.ForceSpill || estimatedInMemoryBuildBytes > options.MemoryBudgetBytes)
            return BuildFieldFromSpill(source, options);

        byte[] records = source.CopyRecords();
        try
        {
            int pointCount = checked(source.Count);
            int leafCount = checked((pointCount + config.MaxPointsPerLeaf - 1) / config.MaxPointsPerLeaf);
            var built = new BuiltField(config, pointCount, leafCount);
            ComputeBounds(records, pointCount, config, built.RootMin, built.RootMax);

            var order = new int[pointCount];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            BuildNode(built, records, order, 0, pointCount, 0, leafCount,
                new int[config.IndexedDimensions], options.CancellationToken);
            built.DocumentCount = source.UniqueDocumentCount >= 0
                ? source.UniqueDocumentCount
                : CountDistinctDocuments(records, pointCount, config);
            return built;
        }
        finally
        {
            // The build owns this copy, not the DWPT buffer.
            Array.Clear(records, 0, records.Length);
        }
    }

    private static long EstimateInMemoryBuildBytes(int pointCount, PackedBkdConfig config)
    {
        int leafCount = checked((pointCount + config.MaxPointsPerLeaf - 1) / config.MaxPointsPerLeaf);
        long records = checked((long)pointCount * config.RecordBytes);
        long order = checked((long)pointCount * sizeof(int));
        long bounds = checked((long)config.IndexedBytesLength * 4);
        long splits = checked((long)Math.Max(0, leafCount - 1) * (sizeof(byte) + config.BytesPerDimension));
        return checked(records + order + bounds + splits + 256);
    }

    private static BuiltField BuildFieldFromSpill(PackedBkdFieldBuffer source, PackedBkdBuildOptions options)
    {
        string directory = options.SpillDirectory ?? Path.GetTempPath();
        FileOpenRetry.CreateDirectory(directory);
        string path = Path.Combine(directory, $"packed-bkd-{Guid.NewGuid():N}.spill");
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
                output.Write(source.Records);
                output.Flush();
            }

            options.CancellationToken.ThrowIfCancellationRequested();
            using var records = new SpillRecordStore(path, source.Count, source.Config);
            var config = source.Config;
            int pointCount = source.Count;
            int leafCount = checked((pointCount + config.MaxPointsPerLeaf - 1) / config.MaxPointsPerLeaf);
            var built = new BuiltField(config, pointCount, leafCount);
            ScanBounds(records, 0, pointCount, config, built.RootMin, built.RootMax, options.CancellationToken);
            BuildSpillNode(
                built,
                records,
                0,
                pointCount,
                0,
                leafCount,
                new int[config.IndexedDimensions],
                options.CancellationToken);
            built.DocumentCount = source.UniqueDocumentCount >= 0
                ? source.UniqueDocumentCount
                : CountDistinctDocuments(records, pointCount, config, options.CancellationToken);
            return built;
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

    private static void BuildSpillNode(
        BuiltField built,
        SpillRecordStore records,
        int start,
        int count,
        int leavesOffset,
        int leafCount,
        int[] parentSplits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (leafCount == 1)
        {
            records.SortRange(start, count, built.Config, splitDimension: -1, cancellationToken);
            built.Leaves[leavesOffset] = EncodeLeaf(records, start, count, built.Config, cancellationToken);
            return;
        }

        ScanBounds(records, start, count, built.Config, built.WorkMin, built.WorkMax, cancellationToken);
        int splitDimension = SelectSplitDimension(built.WorkMin, built.WorkMax, built.Config, parentSplits);
        int leftLeaves = LeftLeafCount(leafCount);
        int leftCount = checked(leftLeaves * built.Config.MaxPointsPerLeaf);
        leftCount = Math.Clamp(leftCount, 1, count - 1);
        records.SortRange(start, count, built.Config, splitDimension, cancellationToken);

        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int splitIndex = checked(rightLeavesOffset - 1);
        built.SplitDimensions[splitIndex] = checked((byte)splitDimension);
        Span<byte> splitValue = stackalloc byte[built.Config.BytesPerDimension];
        records.ReadDimension(start + leftCount, splitDimension, splitValue);
        splitValue.CopyTo(built.SplitValues.AsSpan(splitIndex * built.Config.BytesPerDimension, built.Config.BytesPerDimension));

        parentSplits[splitDimension]++;
        BuildSpillNode(built, records, start, leftCount, leavesOffset, leftLeaves, parentSplits, cancellationToken);
        BuildSpillNode(built, records, start + leftCount, count - leftCount, rightLeavesOffset, leafCount - leftLeaves, parentSplits, cancellationToken);
        parentSplits[splitDimension]--;
    }

    private static void ScanBounds(
        SpillRecordStore records,
        int start,
        int count,
        PackedBkdConfig config,
        byte[] min,
        byte[] max,
        CancellationToken cancellationToken)
    {
        Span<byte> record = stackalloc byte[config.RecordBytes];
        bool first = true;
        for (int index = start; index < start + count; index++)
        {
            if ((index & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            records.Read(index, record);
            for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                var value = record.Slice(offset, config.BytesPerDimension);
                if (first || value.SequenceCompareTo(min.AsSpan(offset, config.BytesPerDimension)) < 0)
                    value.CopyTo(min.AsSpan(offset, config.BytesPerDimension));
                if (first || value.SequenceCompareTo(max.AsSpan(offset, config.BytesPerDimension)) > 0)
                    value.CopyTo(max.AsSpan(offset, config.BytesPerDimension));
            }
            first = false;
        }
    }

    private static void BuildNode(
        BuiltField built,
        byte[] records,
        int[] order,
        int start,
        int count,
        int leavesOffset,
        int leafCount,
        int[] parentSplits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (leafCount == 1)
        {
            SortRange(order, records, start, count, built.Config, splitDimension: -1);
            built.Leaves[leavesOffset] = EncodeLeaf(records, order, start, count, built.Config);
            return;
        }

        ComputeBounds(records, order, start, count, built.Config, built.WorkMin, built.WorkMax);
        int splitDimension = SelectSplitDimension(built.WorkMin, built.WorkMax, built.Config, parentSplits);
        int leftLeaves = LeftLeafCount(leafCount);
        int leftCount = checked(leftLeaves * built.Config.MaxPointsPerLeaf);
        leftCount = Math.Clamp(leftCount, 1, count - 1);
        SortRange(order, records, start, count, built.Config, splitDimension);

        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int splitIndex = checked(rightLeavesOffset - 1);
        built.SplitDimensions[splitIndex] = checked((byte)splitDimension);
        int splitValueOffset = checked(splitIndex * built.Config.BytesPerDimension);
        int rightRecordOffset = checked(order[start + leftCount] * built.Config.RecordBytes + splitDimension * built.Config.BytesPerDimension);
        records.AsSpan(rightRecordOffset, built.Config.BytesPerDimension)
            .CopyTo(built.SplitValues.AsSpan(splitValueOffset, built.Config.BytesPerDimension));

        parentSplits[splitDimension]++;
        BuildNode(built, records, order, start, leftCount, leavesOffset, leftLeaves, parentSplits, cancellationToken);
        BuildNode(built, records, order, start + leftCount, count - leftCount, rightLeavesOffset, leafCount - leftLeaves, parentSplits, cancellationToken);
        parentSplits[splitDimension]--;
    }

    private static int LeftLeafCount(int leafCount)
    {
        int highestPower = 1;
        while ((highestPower << 1) <= leafCount)
            highestPower <<= 1;
        int baseLeft = highestPower >> 1;
        int extra = leafCount - highestPower;
        return extra < baseLeft ? baseLeft + extra : highestPower;
    }

    private static int SelectSplitDimension(
        ReadOnlySpan<byte> min,
        ReadOnlySpan<byte> max,
        PackedBkdConfig config,
        ReadOnlySpan<int> parentSplits)
    {
        int selected = -1;
        ulong selectedSpan = 0;
        int maxSplits = 0;
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            maxSplits = Math.Max(maxSplits, parentSplits[dimension]);

        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            ulong span = UnsignedSpan(
                min.Slice(dimension * config.BytesPerDimension, config.BytesPerDimension),
                max.Slice(dimension * config.BytesPerDimension, config.BytesPerDimension));
            if (span != 0 && parentSplits[dimension] < maxSplits / 2)
                return dimension;
        }

        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = dimension * config.BytesPerDimension;
            ulong span = UnsignedSpan(min.Slice(offset, config.BytesPerDimension), max.Slice(offset, config.BytesPerDimension));
            if (selected < 0 || span > selectedSpan)
            {
                selected = dimension;
                selectedSpan = span;
            }
        }
        return Math.Max(0, selected);
    }

    private static ulong UnsignedSpan(ReadOnlySpan<byte> min, ReadOnlySpan<byte> max)
    {
        ulong left = BinaryPrimitives.ReadUInt32BigEndian(min);
        ulong right = BinaryPrimitives.ReadUInt32BigEndian(max);
        return right - left;
    }

    private static void SortRange(int[] order, byte[] records, int start, int count, PackedBkdConfig config, int splitDimension)
    {
        Array.Sort(order, start, count, new RecordComparer(records, config, splitDimension));
    }

    private static void ComputeBounds(byte[] records, int pointCount, PackedBkdConfig config, byte[] min, byte[] max)
    {
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int sourceOffset = dimension * config.BytesPerDimension;
            records.AsSpan(sourceOffset, config.BytesPerDimension).CopyTo(min.AsSpan(sourceOffset, config.BytesPerDimension));
            records.AsSpan(sourceOffset, config.BytesPerDimension).CopyTo(max.AsSpan(sourceOffset, config.BytesPerDimension));
        }
        for (int point = 1; point < pointCount; point++)
        {
            int recordOffset = point * config.RecordBytes;
            for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            {
                int dimensionOffset = dimension * config.BytesPerDimension;
                var value = records.AsSpan(recordOffset + dimensionOffset, config.BytesPerDimension);
                if (value.SequenceCompareTo(min.AsSpan(dimensionOffset, config.BytesPerDimension)) < 0)
                    value.CopyTo(min.AsSpan(dimensionOffset, config.BytesPerDimension));
                if (value.SequenceCompareTo(max.AsSpan(dimensionOffset, config.BytesPerDimension)) > 0)
                    value.CopyTo(max.AsSpan(dimensionOffset, config.BytesPerDimension));
            }
        }
    }

    private static void ComputeBounds(byte[] records, int[] order, int start, int count, PackedBkdConfig config, byte[] min, byte[] max)
    {
        int firstRecord = order[start] * config.RecordBytes;
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            int offset = dimension * config.BytesPerDimension;
            records.AsSpan(firstRecord + offset, config.BytesPerDimension).CopyTo(min.AsSpan(offset, config.BytesPerDimension));
            records.AsSpan(firstRecord + offset, config.BytesPerDimension).CopyTo(max.AsSpan(offset, config.BytesPerDimension));
        }
        for (int i = 1; i < count; i++)
        {
            int recordOffset = order[start + i] * config.RecordBytes;
            for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                var value = records.AsSpan(recordOffset + offset, config.BytesPerDimension);
                if (value.SequenceCompareTo(min.AsSpan(offset, config.BytesPerDimension)) < 0)
                    value.CopyTo(min.AsSpan(offset, config.BytesPerDimension));
                if (value.SequenceCompareTo(max.AsSpan(offset, config.BytesPerDimension)) > 0)
                    value.CopyTo(max.AsSpan(offset, config.BytesPerDimension));
            }
        }
    }

    private static byte[] EncodeLeaf(byte[] records, int[] order, int start, int count, PackedBkdConfig config)
    {
        int minDoc = int.MaxValue;
        int maxDoc = int.MinValue;
        var actualMin = new byte[config.IndexedBytesLength];
        var actualMax = new byte[config.IndexedBytesLength];
        bool first = true;
        for (int i = 0; i < count; i++)
        {
            int recordOffset = order[start + i] * config.RecordBytes;
            int docId = BinaryPrimitives.ReadInt32LittleEndian(records.AsSpan(recordOffset + config.PackedBytesLength, sizeof(int)));
            minDoc = Math.Min(minDoc, docId);
            maxDoc = Math.Max(maxDoc, docId);
            for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                var value = records.AsSpan(recordOffset + offset, config.BytesPerDimension);
                if (first || value.SequenceCompareTo(actualMin.AsSpan(offset, config.BytesPerDimension)) < 0)
                    value.CopyTo(actualMin.AsSpan(offset, config.BytesPerDimension));
                if (first || value.SequenceCompareTo(actualMax.AsSpan(offset, config.BytesPerDimension)) > 0)
                    value.CopyTo(actualMax.AsSpan(offset, config.BytesPerDimension));
            }
            first = false;
        }

        int docWidth = DocumentWidth(minDoc, maxDoc);
        var prefixes = new byte[config.Dimensions];
        int prefixBytes = 0;
        var commonPrefixes = new byte[config.PackedBytesLength];
        for (int dimension = 0; dimension < config.Dimensions; dimension++)
        {
            int offset = dimension * config.BytesPerDimension;
            int common = config.BytesPerDimension;
            int firstRecord = order[start] * config.RecordBytes + offset;
            for (int i = 1; i < count && common > 0; i++)
            {
                int recordOffset = order[start + i] * config.RecordBytes + offset;
                int current = 0;
                while (current < common && records[recordOffset + current] == records[firstRecord + current])
                    current++;
                common = current;
            }
            prefixes[dimension] = checked((byte)common);
            prefixBytes += common;
            records.AsSpan(firstRecord, common).CopyTo(commonPrefixes.AsSpan(offset, common));
        }

        int rawValueBytes = checked(count * config.PackedBytesLength);
        int suffixValueBytes = checked(count * (config.PackedBytesLength - prefixBytes));
        bool usePrefixes = checked(config.Dimensions + prefixBytes + suffixValueBytes) < checked(config.Dimensions + rawValueBytes);
        byte encoding = usePrefixes ? PrefixValues : RawValues;
        int headerBytes = checked(2 + 1 + 1 + sizeof(int) + config.IndexedBytesLength * 2 + (usePrefixes ? config.Dimensions + prefixBytes : 0));
        int valueBytes = usePrefixes ? suffixValueBytes : rawValueBytes;
        int length = checked(headerBytes + count * docWidth + valueBytes);
        var leaf = new byte[length];
        int cursor = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(leaf.AsSpan(cursor, sizeof(ushort)), checked((ushort)count));
        cursor += sizeof(ushort);
        leaf[cursor++] = checked((byte)docWidth);
        leaf[cursor++] = encoding;
        BinaryPrimitives.WriteInt32LittleEndian(leaf.AsSpan(cursor, sizeof(int)), minDoc);
        cursor += sizeof(int);
        actualMin.CopyTo(leaf, cursor);
        cursor += actualMin.Length;
        actualMax.CopyTo(leaf, cursor);
        cursor += actualMax.Length;
        if (usePrefixes)
        {
            prefixes.CopyTo(leaf, cursor);
            cursor += prefixes.Length;
            for (int dimension = 0; dimension < config.Dimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                int lengthForDimension = prefixes[dimension];
                commonPrefixes.AsSpan(offset, lengthForDimension).CopyTo(leaf.AsSpan(cursor, lengthForDimension));
                cursor += lengthForDimension;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int recordOffset = order[start + i] * config.RecordBytes + config.PackedBytesLength;
            uint delta = checked((uint)(BinaryPrimitives.ReadInt32LittleEndian(records.AsSpan(recordOffset, sizeof(int))) - minDoc));
            for (int b = 0; b < docWidth; b++)
            {
                leaf[cursor++] = (byte)delta;
                delta >>= 8;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int recordOffset = order[start + i] * config.RecordBytes;
            if (!usePrefixes)
            {
                records.AsSpan(recordOffset, config.PackedBytesLength).CopyTo(leaf.AsSpan(cursor, config.PackedBytesLength));
                cursor += config.PackedBytesLength;
                continue;
            }

            for (int dimension = 0; dimension < config.Dimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                int suffixLength = config.BytesPerDimension - prefixes[dimension];
                records.AsSpan(recordOffset + offset + prefixes[dimension], suffixLength)
                    .CopyTo(leaf.AsSpan(cursor, suffixLength));
                cursor += suffixLength;
            }
        }

        return leaf;
    }

    private static byte[] EncodeLeaf(
        SpillRecordStore records,
        int start,
        int count,
        PackedBkdConfig config,
        CancellationToken cancellationToken)
    {
        int minDoc = int.MaxValue;
        int maxDoc = int.MinValue;
        var actualMin = new byte[config.IndexedBytesLength];
        var actualMax = new byte[config.IndexedBytesLength];
        var firstRecord = new byte[config.RecordBytes];
        var record = new byte[config.RecordBytes];
        bool first = true;
        for (int i = 0; i < count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            records.Read(start + i, record);
            int docId = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(config.PackedBytesLength, sizeof(int)));
            minDoc = Math.Min(minDoc, docId);
            maxDoc = Math.Max(maxDoc, docId);
            for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                var value = record.AsSpan(offset, config.BytesPerDimension);
                if (first || value.SequenceCompareTo(actualMin.AsSpan(offset, config.BytesPerDimension)) < 0)
                    value.CopyTo(actualMin.AsSpan(offset, config.BytesPerDimension));
                if (first || value.SequenceCompareTo(actualMax.AsSpan(offset, config.BytesPerDimension)) > 0)
                    value.CopyTo(actualMax.AsSpan(offset, config.BytesPerDimension));
            }
            if (first)
                record.CopyTo(firstRecord);
            first = false;
        }

        int docWidth = DocumentWidth(minDoc, maxDoc);
        var prefixes = new byte[config.Dimensions];
        int prefixBytes = 0;
        var commonPrefixes = new byte[config.PackedBytesLength];
        for (int dimension = 0; dimension < config.Dimensions; dimension++)
        {
            int offset = dimension * config.BytesPerDimension;
            int common = config.BytesPerDimension;
            for (int i = 1; i < count && common > 0; i++)
            {
                records.Read(start + i, record);
                int current = 0;
                while (current < common && record[offset + current] == firstRecord[offset + current])
                    current++;
                common = current;
            }
            prefixes[dimension] = checked((byte)common);
            prefixBytes += common;
            firstRecord.AsSpan(offset, common).CopyTo(commonPrefixes.AsSpan(offset, common));
        }

        int rawValueBytes = checked(count * config.PackedBytesLength);
        int suffixValueBytes = checked(count * (config.PackedBytesLength - prefixBytes));
        bool usePrefixes = checked(config.Dimensions + prefixBytes + suffixValueBytes) < checked(config.Dimensions + rawValueBytes);
        byte encoding = usePrefixes ? PrefixValues : RawValues;
        int headerBytes = checked(2 + 1 + 1 + sizeof(int) + config.IndexedBytesLength * 2 + (usePrefixes ? config.Dimensions + prefixBytes : 0));
        int valueBytes = usePrefixes ? suffixValueBytes : rawValueBytes;
        int length = checked(headerBytes + count * docWidth + valueBytes);
        var leaf = new byte[length];
        int cursor = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(leaf.AsSpan(cursor, sizeof(ushort)), checked((ushort)count));
        cursor += sizeof(ushort);
        leaf[cursor++] = checked((byte)docWidth);
        leaf[cursor++] = encoding;
        BinaryPrimitives.WriteInt32LittleEndian(leaf.AsSpan(cursor, sizeof(int)), minDoc);
        cursor += sizeof(int);
        actualMin.CopyTo(leaf, cursor);
        cursor += actualMin.Length;
        actualMax.CopyTo(leaf, cursor);
        cursor += actualMax.Length;
        if (usePrefixes)
        {
            prefixes.CopyTo(leaf, cursor);
            cursor += prefixes.Length;
            for (int dimension = 0; dimension < config.Dimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                int lengthForDimension = prefixes[dimension];
                commonPrefixes.AsSpan(offset, lengthForDimension).CopyTo(leaf.AsSpan(cursor, lengthForDimension));
                cursor += lengthForDimension;
            }
        }

        for (int i = 0; i < count; i++)
        {
            records.Read(start + i, record);
            uint delta = checked((uint)(BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(config.PackedBytesLength, sizeof(int))) - minDoc));
            for (int b = 0; b < docWidth; b++)
            {
                leaf[cursor++] = (byte)delta;
                delta >>= 8;
            }
        }

        for (int i = 0; i < count; i++)
        {
            records.Read(start + i, record);
            if (!usePrefixes)
            {
                record.AsSpan(0, config.PackedBytesLength).CopyTo(leaf.AsSpan(cursor, config.PackedBytesLength));
                cursor += config.PackedBytesLength;
                continue;
            }

            for (int dimension = 0; dimension < config.Dimensions; dimension++)
            {
                int offset = dimension * config.BytesPerDimension;
                int suffixLength = config.BytesPerDimension - prefixes[dimension];
                record.AsSpan(offset + prefixes[dimension], suffixLength).CopyTo(leaf.AsSpan(cursor, suffixLength));
                cursor += suffixLength;
            }
        }

        return leaf;
    }

    private static int DocumentWidth(int minDoc, int maxDoc)
    {
        long range = (long)maxDoc - minDoc;
        if (range == 0) return 0;
        if (range <= byte.MaxValue) return 1;
        if (range <= ushort.MaxValue) return 2;
        if (range <= 0x00ff_ffff) return 3;
        return 4;
    }

    private static int CountDistinctDocuments(byte[] records, int pointCount, PackedBkdConfig config)
    {
        var docs = new HashSet<int>();
        for (int i = 0; i < pointCount; i++)
            docs.Add(BinaryPrimitives.ReadInt32LittleEndian(records.AsSpan(i * config.RecordBytes + config.PackedBytesLength, sizeof(int))));
        return docs.Count;
    }

    private static int CountDistinctDocuments(
        SpillRecordStore records,
        int pointCount,
        PackedBkdConfig config,
        CancellationToken cancellationToken)
    {
        var docs = new HashSet<int>();
        Span<byte> record = stackalloc byte[config.RecordBytes];
        for (int i = 0; i < pointCount; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            records.Read(i, record);
            docs.Add(BinaryPrimitives.ReadInt32LittleEndian(record.Slice(config.PackedBytesLength, sizeof(int))));
        }
        return docs.Count;
    }

    private static void WriteField(CodecBodyOutput output, BuiltField field)
    {
        output.WriteInt32(unchecked((int)FieldMagic));
        output.WriteByte(checked((byte)field.Config.Dimensions));
        output.WriteByte(checked((byte)field.Config.IndexedDimensions));
        output.WriteByte(checked((byte)field.Config.BytesPerDimension));
        output.WriteByte(0);
        output.WriteInt32(field.Config.MaxPointsPerLeaf);
        output.WriteInt32(field.LeafCount);
        output.WriteInt64(field.PointCount);
        output.WriteInt32(field.DocumentCount);
        output.WriteInt32(field.SplitDimensions.Length);
        output.WriteBytes(field.RootMin);
        output.WriteBytes(field.RootMax);
        output.WriteBytes(field.SplitDimensions);
        output.WriteBytes(field.SplitValues);

        long leafOffset = 0;
        output.WriteInt64(leafOffset);
        foreach (var leaf in field.Leaves)
        {
            leafOffset = checked(leafOffset + leaf.Length);
            output.WriteInt64(leafOffset);
        }
        foreach (var leaf in field.Leaves)
            output.WriteBytes(leaf);
    }

    private static void WriteString(CodecBodyOutput output, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        output.WriteVarInt(bytes.Length);
        output.WriteBytes(bytes);
    }

    private sealed class SpillRecordStore : IDisposable
    {
        private readonly Stream _stream;
        private readonly PackedBkdConfig _config;
        private readonly byte[] _left;
        private readonly byte[] _right;
        private readonly byte[] _swap;
        private bool _disposed;

        internal SpillRecordStore(string path, int count, PackedBkdConfig config)
        {
            _stream = FileOpenRetry.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.RandomAccess);
            _config = config;
            Count = count;
            _left = new byte[config.RecordBytes];
            _right = new byte[config.RecordBytes];
            _swap = new byte[config.RecordBytes];
            long expectedLength = checked((long)count * config.RecordBytes);
            if (_stream.Length != expectedLength)
                throw new InvalidDataException("The packed BKD spill record file has an invalid length.");
        }

        internal int Count { get; }

        internal void Read(int index, Span<byte> destination)
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

        internal void SortRange(
            int start,
            int count,
            PackedBkdConfig config,
            int splitDimension,
            CancellationToken cancellationToken)
        {
            if (count < 2)
                return;

            // Heapsort keeps the spill path deterministic without allocating an
            // index array proportional to the number of records.
            for (int root = count / 2 - 1; root >= 0; root--)
                SiftDown(root, count, start, config, splitDimension, cancellationToken);
            for (int end = count - 1; end > 0; end--)
            {
                if ((end & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Swap(start, start + end);
                SiftDown(0, end, start, config, splitDimension, cancellationToken);
            }
        }

        private void SiftDown(
            int root,
            int count,
            int start,
            PackedBkdConfig config,
            int splitDimension,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int child = checked(root * 2 + 1);
                if (child >= count)
                    return;
                if (child + 1 < count && Compare(start + child, start + child + 1, config, splitDimension) < 0)
                    child++;
                if (Compare(start + root, start + child, config, splitDimension) >= 0)
                    return;
                Swap(start + root, start + child);
                root = child;
            }
        }

        private int Compare(int leftIndex, int rightIndex, PackedBkdConfig config, int splitDimension)
        {
            Read(leftIndex, _left);
            Read(rightIndex, _right);
            int leftOffset = 0;
            int rightOffset = 0;
            if (splitDimension >= 0)
            {
                int comparison = CompareDimension(_left, _right, splitDimension, config);
                if (comparison != 0)
                    return comparison;
                for (int dimension = 0; dimension < config.Dimensions; dimension++)
                {
                    if (dimension == splitDimension)
                        continue;
                    comparison = CompareDimension(_left, _right, dimension, config);
                    if (comparison != 0)
                        return comparison;
                }
            }
            else
            {
                int comparison = _left.AsSpan(0, config.PackedBytesLength)
                    .SequenceCompareTo(_right.AsSpan(0, config.PackedBytesLength));
                if (comparison != 0)
                    return comparison;
            }

            leftOffset = config.PackedBytesLength;
            rightOffset = config.PackedBytesLength;
            return BinaryPrimitives.ReadInt32LittleEndian(_left.AsSpan(leftOffset, sizeof(int)))
                .CompareTo(BinaryPrimitives.ReadInt32LittleEndian(_right.AsSpan(rightOffset, sizeof(int))));
        }

        private static int CompareDimension(byte[] left, byte[] right, int dimension, PackedBkdConfig config)
        {
            int offset = checked(dimension * config.BytesPerDimension);
            return left.AsSpan(offset, config.BytesPerDimension)
                .SequenceCompareTo(right.AsSpan(offset, config.BytesPerDimension));
        }

        private void Swap(int leftIndex, int rightIndex)
        {
            if (leftIndex == rightIndex)
                return;
            Read(leftIndex, _left);
            Read(rightIndex, _right);
            _left.CopyTo(_swap, 0);
            Write(leftIndex, _right);
            Write(rightIndex, _swap);
        }

        private void Write(int index, ReadOnlySpan<byte> source)
        {
            _stream.Position = checked((long)index * _config.RecordBytes);
            _stream.Write(source[.._config.RecordBytes]);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _stream.Dispose();
        }
    }

    private sealed class BuiltField
    {
        internal BuiltField(PackedBkdConfig config, int pointCount, int leafCount)
        {
            Config = config;
            PointCount = pointCount;
            LeafCount = leafCount;
            RootMin = new byte[config.IndexedBytesLength];
            RootMax = new byte[config.IndexedBytesLength];
            SplitDimensions = new byte[Math.Max(0, leafCount - 1)];
            SplitValues = new byte[Math.Max(0, leafCount - 1) * config.BytesPerDimension];
            WorkMin = new byte[config.IndexedBytesLength];
            WorkMax = new byte[config.IndexedBytesLength];
            Leaves = new byte[leafCount][];
        }

        internal PackedBkdConfig Config { get; }
        internal int PointCount { get; }
        internal int LeafCount { get; }
        internal int DocumentCount { get; set; }
        internal byte[] RootMin { get; }
        internal byte[] RootMax { get; }
        internal byte[] SplitDimensions { get; }
        internal byte[] SplitValues { get; }
        internal byte[] WorkMin { get; }
        internal byte[] WorkMax { get; }
        internal byte[][] Leaves { get; }
    }

    private readonly record struct DirectoryEntry(string Name, long Offset, long Length);

    private sealed class RecordComparer : Comparer<int>
    {
        private readonly byte[] _records;
        private readonly PackedBkdConfig _config;
        private readonly int _splitDimension;

        internal RecordComparer(byte[] records, PackedBkdConfig config, int splitDimension)
        {
            _records = records;
            _config = config;
            _splitDimension = splitDimension;
        }

        public override int Compare(int left, int right)
        {
            if (left == right) return 0;
            int leftOffset = left * _config.RecordBytes;
            int rightOffset = right * _config.RecordBytes;
            if (_splitDimension >= 0)
            {
                int comparison = CompareDimension(leftOffset, rightOffset, _splitDimension);
                if (comparison != 0) return comparison;
                for (int dimension = 0; dimension < _config.Dimensions; dimension++)
                {
                    if (dimension == _splitDimension) continue;
                    comparison = CompareDimension(leftOffset, rightOffset, dimension);
                    if (comparison != 0) return comparison;
                }
            }
            else
            {
                int comparison = _records.AsSpan(leftOffset, _config.PackedBytesLength)
                    .SequenceCompareTo(_records.AsSpan(rightOffset, _config.PackedBytesLength));
                if (comparison != 0) return comparison;
            }

            return BinaryPrimitives.ReadInt32LittleEndian(_records.AsSpan(leftOffset + _config.PackedBytesLength, sizeof(int)))
                .CompareTo(BinaryPrimitives.ReadInt32LittleEndian(_records.AsSpan(rightOffset + _config.PackedBytesLength, sizeof(int))));
        }

        private int CompareDimension(int leftOffset, int rightOffset, int dimension)
            => _records.AsSpan(leftOffset + dimension * _config.BytesPerDimension, _config.BytesPerDimension)
                .SequenceCompareTo(_records.AsSpan(rightOffset + dimension * _config.BytesPerDimension, _config.BytesPerDimension));
    }
}
