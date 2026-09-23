using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Encodes the single Packed BKD v1 leaf representation.</summary>
internal static class PackedBkdLeafEncoder
{
    internal static byte[] Encode<TSource>(
        TSource source,
        int start,
        int count,
        PackedBkdConfig config,
        PackedBkdBuildMemoryTracker tracker,
        CancellationToken cancellationToken)
        where TSource : IPackedBkdRecordSource
    {
        Span<byte> firstRecord = stackalloc byte[config.RecordBytes];
        Span<byte> record = stackalloc byte[config.RecordBytes];
        Span<byte> actualMinimum = stackalloc byte[config.IndexedBytesLength];
        Span<byte> actualMaximum = stackalloc byte[config.IndexedBytesLength];
        bool first = true;
        int minimumDocument = int.MaxValue;
        int maximumDocument = int.MinValue;

        for (int i = 0; i < count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            source.Read(start + i, record);
            int document = BinaryPrimitives.ReadInt32LittleEndian(record[config.PackedBytesLength..]);
            minimumDocument = Math.Min(minimumDocument, document);
            maximumDocument = Math.Max(maximumDocument, document);
            for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
            {
                int offset = checked(dimension * config.BytesPerDimension);
                ReadOnlySpan<byte> value = record.Slice(offset, config.BytesPerDimension);
                if (first || value.SequenceCompareTo(actualMinimum.Slice(offset, config.BytesPerDimension)) < 0)
                    value.CopyTo(actualMinimum.Slice(offset, config.BytesPerDimension));
                if (first || value.SequenceCompareTo(actualMaximum.Slice(offset, config.BytesPerDimension)) > 0)
                    value.CopyTo(actualMaximum.Slice(offset, config.BytesPerDimension));
            }
            if (first)
                record.CopyTo(firstRecord);
            first = false;
        }

        int documentWidth = PackedBkdTreeMath.GetDocumentWidth(minimumDocument, maximumDocument);
        Span<byte> prefixes = stackalloc byte[config.Dimensions];
        Span<byte> commonPrefixes = stackalloc byte[config.PackedBytesLength];
        int prefixBytes = 0;
        for (int dimension = 0; dimension < config.Dimensions; dimension++)
        {
            int offset = checked(dimension * config.BytesPerDimension);
            int common = config.BytesPerDimension;
            for (int i = 1; i < count && common > 0; i++)
            {
                if ((i & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                source.Read(start + i, record);
                int current = 0;
                while (current < common && record[offset + current] == firstRecord[offset + current])
                    current++;
                common = current;
            }
            prefixes[dimension] = checked((byte)common);
            prefixBytes += common;
            firstRecord.Slice(offset, common).CopyTo(commonPrefixes.Slice(offset, common));
        }

        int rawValueBytes = checked(count * config.PackedBytesLength);
        int suffixValueBytes = checked(count * (config.PackedBytesLength - prefixBytes));
        bool usePrefixes = prefixBytes > 0
            && checked(config.Dimensions + prefixBytes + suffixValueBytes) < rawValueBytes;
        byte encoding = usePrefixes ? PackedBkdFormat.PrefixValues : PackedBkdFormat.RawValues;
        int headerBytes = checked(
            sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(int)
            + config.IndexedBytesLength * 2
            + (usePrefixes ? config.Dimensions + prefixBytes : 0));
        int valueBytes = usePrefixes ? suffixValueBytes : rawValueBytes;
        int length = checked(headerBytes + count * documentWidth + valueBytes);
        tracker.Reserve(length);
        byte[] leaf;
        try
        {
            leaf = new byte[length];
        }
        catch
        {
            tracker.Release(length);
            throw;
        }
        int cursor = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(leaf.AsSpan(cursor, sizeof(ushort)), checked((ushort)count));
        cursor += sizeof(ushort);
        leaf[cursor++] = checked((byte)documentWidth);
        leaf[cursor++] = encoding;
        BinaryPrimitives.WriteInt32LittleEndian(leaf.AsSpan(cursor, sizeof(int)), minimumDocument);
        cursor += sizeof(int);
        actualMinimum.CopyTo(leaf.AsSpan(cursor, actualMinimum.Length));
        cursor += actualMinimum.Length;
        actualMaximum.CopyTo(leaf.AsSpan(cursor, actualMaximum.Length));
        cursor += actualMaximum.Length;
        if (usePrefixes)
        {
            prefixes.CopyTo(leaf.AsSpan(cursor, prefixes.Length));
            cursor += prefixes.Length;
            for (int dimension = 0; dimension < config.Dimensions; dimension++)
            {
                int offset = checked(dimension * config.BytesPerDimension);
                int lengthForDimension = prefixes[dimension];
                commonPrefixes.Slice(offset, lengthForDimension)
                    .CopyTo(leaf.AsSpan(cursor, lengthForDimension));
                cursor += lengthForDimension;
            }
        }

        for (int i = 0; i < count; i++)
        {
            source.Read(start + i, record);
            uint delta = checked((uint)(BinaryPrimitives.ReadInt32LittleEndian(record[config.PackedBytesLength..]) - minimumDocument));
            for (int b = 0; b < documentWidth; b++)
            {
                leaf[cursor++] = (byte)delta;
                delta >>= 8;
            }
        }

        for (int i = 0; i < count; i++)
        {
            source.Read(start + i, record);
            if (!usePrefixes)
            {
                record[..config.PackedBytesLength].CopyTo(leaf.AsSpan(cursor, config.PackedBytesLength));
                cursor += config.PackedBytesLength;
                continue;
            }

            for (int dimension = 0; dimension < config.Dimensions; dimension++)
            {
                int offset = checked(dimension * config.BytesPerDimension);
                int suffixLength = config.BytesPerDimension - prefixes[dimension];
                record.Slice(offset + prefixes[dimension], suffixLength)
                    .CopyTo(leaf.AsSpan(cursor, suffixLength));
                cursor += suffixLength;
            }
        }

        return leaf;
    }
}
