using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Counts document IDs with bounded bitset or temporary-file storage.</summary>
internal static class PackedBkdDocumentCounter
{
    internal static int Count<TSource>(
        TSource source,
        PackedBkdConfig config,
        PackedBkdBuildMemoryTracker tracker,
        string spillDirectory,
        CancellationToken cancellationToken)
        where TSource : IPackedBkdRecordSource
    {
        Span<byte> record = stackalloc byte[config.RecordBytes];
        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        for (int i = 0; i < source.Count; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            source.Read(i, record);
            int document = BinaryPrimitives.ReadInt32LittleEndian(record[config.PackedBytesLength..]);
            minimum = Math.Min(minimum, document);
            maximum = Math.Max(maximum, document);
        }

        long range = checked((long)maximum - minimum + 1);
        long wordCount = checked((range + 63) / 64);
        if (wordCount <= int.MaxValue)
        {
            int requestedWords = checked((int)wordCount);
            int reservedCapacity = GetArrayPoolCapacity(requestedWords);
            long reservedBytes = checked((long)reservedCapacity * sizeof(ulong));
            if (tracker.TryReserve(reservedBytes))
            {
                ulong[]? bits = null;
                long accountedBytes = reservedBytes;
                bool reservationReleased = false;
                try
                {
                    bits = ArrayPool<ulong>.Shared.Rent(requestedWords);
                    long bytes = checked((long)bits.LongLength * sizeof(ulong));
                    if (bytes > reservedBytes && !tracker.TryReserve(bytes - reservedBytes))
                    {
                        ArrayPool<ulong>.Shared.Return(bits, clearArray: false);
                        bits = null;
                        tracker.Release(reservedBytes);
                        reservationReleased = true;
                        return CountExternal(source, config, tracker, spillDirectory, cancellationToken);
                    }
                    if (bytes < reservedBytes)
                        tracker.Release(reservedBytes - bytes);
                    accountedBytes = bytes;
                    Array.Clear(bits, 0, bits.Length);
                    for (int i = 0; i < source.Count; i++)
                    {
                        if ((i & 0x3ff) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        source.Read(i, record);
                        int document = BinaryPrimitives.ReadInt32LittleEndian(record[config.PackedBytesLength..]);
                        long relative = (long)document - minimum;
                        bits[relative >> 6] |= 1UL << (int)(relative & 63);
                    }

                    int result = 0;
                    foreach (ulong word in bits)
                        result += BitOperations.PopCount(word);
                    return result;
                }
                finally
                {
                    if (bits is not null)
                    {
                        tracker.Release(accountedBytes);
                        ArrayPool<ulong>.Shared.Return(bits, clearArray: false);
                    }
                    else if (!reservationReleased)
                        tracker.Release(reservedBytes);
                }
            }
        }

        return CountExternal(source, config, tracker, spillDirectory, cancellationToken);
    }

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

    private static int CountExternal<TSource>(
        TSource source,
        PackedBkdConfig config,
        PackedBkdBuildMemoryTracker tracker,
        string spillDirectory,
        CancellationToken cancellationToken)
        where TSource : IPackedBkdRecordSource
    {
        FileOpenRetry.CreateDirectory(spillDirectory);
        string path = Path.Combine(spillDirectory, $"packed-bkd-{Guid.NewGuid():N}.docs");
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
                Span<byte> record = stackalloc byte[config.RecordBytes];
                Span<byte> document = stackalloc byte[sizeof(int)];
                for (int i = 0; i < source.Count; i++)
                {
                    if ((i & 0x3ff) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    source.Read(i, record);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        document,
                        BinaryPrimitives.ReadInt32LittleEndian(record[config.PackedBytesLength..]));
                    output.Write(document);
                }
                output.Flush();
            }

            using var store = new DocumentSpillStore(path, source.Count, tracker);
            store.Sort(cancellationToken);
            return store.CountDistinct(cancellationToken);
        }
        finally
        {
            try
            {
                FileOpenRetry.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "packed BKD document spill cleanup");
            }
        }
    }

    private sealed class DocumentSpillStore : IDisposable
    {
        private readonly Stream _stream;
        private readonly PackedBkdBuildMemoryTracker _tracker;
        private readonly byte[] _left;
        private readonly byte[] _swap;
        private readonly long _scratchBytes;
        private bool _disposed;

        internal DocumentSpillStore(string path, int count, PackedBkdBuildMemoryTracker tracker)
        {
            _stream = FileOpenRetry.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.RandomAccess);
            Count = count;
            _tracker = tracker;
            _scratchBytes = 2L * sizeof(int);
            bool scratchReserved = false;
            try
            {
                tracker.Reserve(_scratchBytes);
                scratchReserved = true;
                _left = new byte[sizeof(int)];
                _swap = new byte[sizeof(int)];
                if (_stream.Length != checked((long)count * sizeof(int)))
                    throw new InvalidDataException("The packed BKD document spill file has an invalid length.");
            }
            catch
            {
                _stream.Dispose();
                if (scratchReserved)
                    tracker.Release(_scratchBytes);
                throw;
            }
        }

        internal int Count { get; }

        internal void Sort(CancellationToken cancellationToken)
            => SortRange(0, Count, 0, cancellationToken);

        internal int CountDistinct(CancellationToken cancellationToken)
        {
            if (Count == 0)
                return 0;
            int result = 0;
            int previous = -1;
            for (int i = 0; i < Count; i++)
            {
                if ((i & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                int current = Read(i);
                if (i == 0 || current != previous)
                    result++;
                previous = current;
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _stream.Dispose();
            _tracker.Release(_scratchBytes);
        }

        private void SortRange(int start, int count, int keyByte, CancellationToken cancellationToken)
        {
            if (count < 2 || keyByte >= sizeof(int))
                return;
            cancellationToken.ThrowIfCancellationRequested();
            Span<int> counts = stackalloc int[256];
            Span<int> starts = stackalloc int[256];
            Span<int> next = stackalloc int[256];
            counts.Clear();
            for (int i = 0; i < count; i++)
            {
                if ((i & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                counts[GetByte(Read(start + i), keyByte)]++;
            }
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
                    int current = Read(position);
                    int currentBucket = GetByte(current, keyByte);
                    if (currentBucket == bucket)
                    {
                        next[bucket]++;
                        continue;
                    }
                    int target = next[currentBucket]++;
                    int other = Read(target);
                    Write(target, current);
                    Write(position, other);
                }
            }
            for (int bucket = 0; bucket < counts.Length; bucket++)
                if (counts[bucket] > 1)
                    SortRange(starts[bucket], counts[bucket], keyByte + 1, cancellationToken);
        }

        private int Read(int index)
        {
            _stream.Position = checked((long)index * sizeof(int));
            _stream.ReadExactly(_left);
            return BinaryPrimitives.ReadInt32LittleEndian(_left);
        }

        private void Write(int index, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_swap, value);
            _stream.Position = checked((long)index * sizeof(int));
            _stream.Write(_swap);
        }

        private static int GetByte(int value, int keyByte)
            => unchecked((int)((uint)value >> (24 - keyByte * 8)) & 0xff);
    }
}
