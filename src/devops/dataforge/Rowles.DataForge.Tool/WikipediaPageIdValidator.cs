using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Rowles.DataForge.Workloads;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tool;

internal sealed record WikipediaPageIdValidationResult(
    long EntriesScanned,
    long TemporaryBytes,
    int RunCount,
    long PeakManagedBytes,
    long ElapsedMilliseconds,
    long DuplicateCount);

internal enum WikipediaPageIdValidationStage
{
    RunWritten,
    MergeStarted
}

/// <summary>Validates page ID uniqueness with bounded in-memory external sort runs.</summary>
internal static class WikipediaPageIdValidator
{
    internal const int MaximumRunPageIds = 1_048_576;
    private const int ValueBytes = sizeof(ulong);
    private const int WriteBufferBytes = 64 * 1024;
    private const int ValuesPerWrite = WriteBufferBytes / ValueBytes;

    internal static WikipediaPageIdValidationResult Validate(
        string indexPath,
        string cacheDirectory,
        int runPageIdCapacity = MaximumRunPageIds,
        CancellationToken cancellationToken = default,
        Action<WikipediaPageIdValidationStage, string?>? stageObserver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        if (runPageIdCapacity is < 1 or > MaximumRunPageIds)
            throw new ArgumentOutOfRangeException(nameof(runPageIdCapacity));

        var index = Path.GetFullPath(indexPath);
        var cache = Path.GetFullPath(cacheDirectory);
        if (!File.Exists(index))
            throw new FileNotFoundException("Pinned Wikipedia multistream index is missing.", index);
        if (!Directory.Exists(cache))
            throw new DirectoryNotFoundException($"Wikipedia source cache '{cache}' does not exist.");

        cancellationToken.ThrowIfCancellationRequested();
        var temporaryDirectory = Path.Combine(cache, ".page-id-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var timer = Stopwatch.StartNew();
        long entriesScanned = 0;
        long temporaryBytes = 0;
        long peakManagedBytes = 0;
        var runPaths = new List<string>();
        try
        {
            var pageIds = new ulong[runPageIdCapacity];
            var writeBuffer = new byte[WriteBufferBytes];
            SampleManagedMemory(ref peakManagedBytes);
            var count = 0;
            long previousOffset = -1;
            using (var file = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (var decompressor = BZip2Stream.Create(file, CompressionMode.Decompress, decompressConcatenated: false, leaveOpen: true))
            using (var reader = new StreamReader(decompressor, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, 64 * 1024, leaveOpen: true))
            {
                string? line;
                while ((line = WikipediaCandidateSelector.ReadBoundedLine(reader)) is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entriesScanned++;
                    var lineBytes = WikipediaTextNormaliserV1.StrictUtf8ByteCount(line);
                    if (lineBytes > WikipediaCandidateSelector.MaximumIndexLineBytes)
                        throw new InvalidDataException($"Wikipedia index line {entriesScanned.ToString(CultureInfo.InvariantCulture)} exceeds 64 KiB.");
                    var entry = WikipediaCandidateSelector.ParseIndexLine(line, entriesScanned);
                    if (entry.Offset < previousOffset)
                        throw new InvalidDataException($"Wikipedia index offset decreases at line {entriesScanned.ToString(CultureInfo.InvariantCulture)}.");
                    previousOffset = entry.Offset;
                    pageIds[count++] = entry.PageId;
                    if ((entriesScanned & 0x3fff) == 0)
                        SampleManagedMemory(ref peakManagedBytes);
                    if (count == pageIds.Length)
                    {
                        var runPath = WriteSortedRun(temporaryDirectory, runPaths.Count, pageIds, count, writeBuffer, cancellationToken, ref temporaryBytes);
                        runPaths.Add(runPath);
                        count = 0;
                        stageObserver?.Invoke(WikipediaPageIdValidationStage.RunWritten, runPath);
                    }
                }
            }

            if (count > 0)
            {
                var runPath = WriteSortedRun(temporaryDirectory, runPaths.Count, pageIds, count, writeBuffer, cancellationToken, ref temporaryBytes);
                runPaths.Add(runPath);
                stageObserver?.Invoke(WikipediaPageIdValidationStage.RunWritten, runPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            stageObserver?.Invoke(WikipediaPageIdValidationStage.MergeStarted, null);
            cancellationToken.ThrowIfCancellationRequested();
            SampleManagedMemory(ref peakManagedBytes);

            long merged = 0;
            ulong? previousPageId = null;
            var queue = new PriorityQueue<RunCursor, ulong>();
            var openCursors = new List<RunCursor>(runPaths.Count);
            try
            {
                foreach (var runPath in runPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cursor = new RunCursor(runPath);
                    openCursors.Add(cursor);
                    if (cursor.TryRead(out var value))
                        queue.Enqueue(cursor, value);
                    else
                        cursor.Dispose();
                }

                while (queue.TryDequeue(out var cursor, out var pageId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (previousPageId == pageId)
                        throw new InvalidDataException($"Wikipedia index contains duplicate page ID {pageId.ToString(CultureInfo.InvariantCulture)}.");
                    previousPageId = pageId;
                    merged++;
                    if (cursor.TryRead(out var next))
                        queue.Enqueue(cursor, next);
                    else
                        cursor.Dispose();
                    if ((merged & 0x3fff) == 0)
                        SampleManagedMemory(ref peakManagedBytes);
                }
            }
            finally
            {
                foreach (var cursor in openCursors)
                    cursor.Dispose();
            }

            if (merged != entriesScanned)
                throw new InvalidDataException($"Wikipedia page ID validation merged {merged} values after scanning {entriesScanned} entries.");
            timer.Stop();
            return new WikipediaPageIdValidationResult(entriesScanned, temporaryBytes, runPaths.Count,
                peakManagedBytes, timer.ElapsedMilliseconds, DuplicateCount: 0);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string WriteSortedRun(
        string temporaryDirectory,
        int runNumber,
        ulong[] pageIds,
        int count,
        byte[] buffer,
        CancellationToken cancellationToken,
        ref long temporaryBytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(pageIds, 0, count);
        for (var index = 1; index < count; index++)
        {
            if (pageIds[index] == pageIds[index - 1])
                throw new InvalidDataException($"Wikipedia index contains duplicate page ID {pageIds[index].ToString(CultureInfo.InvariantCulture)}.");
        }

        var path = Path.Combine(temporaryDirectory, $"run-{runNumber:D6}.bin");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, WriteBufferBytes, FileOptions.SequentialScan);
        var offset = 0;
        while (offset < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchCount = Math.Min(count - offset, ValuesPerWrite);
            var byteCount = batchCount * ValueBytes;
            for (var item = 0; item < batchCount; item++)
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(item * ValueBytes, ValueBytes), pageIds[offset + item]);
            output.Write(buffer, 0, byteCount);
            temporaryBytes = checked(temporaryBytes + byteCount);
            offset += batchCount;
        }
        output.Flush(flushToDisk: true);
        return path;
    }

    private static void SampleManagedMemory(ref long peakManagedBytes)
    {
        var current = GC.GetTotalMemory(forceFullCollection: false);
        if (current > peakManagedBytes)
            peakManagedBytes = current;
    }

    private sealed class RunCursor(string path) : IDisposable
    {
        private readonly FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024, FileOptions.SequentialScan);

        public bool TryRead(out ulong value)
        {
            Span<byte> bytes = stackalloc byte[ValueBytes];
            var totalRead = 0;
            while (totalRead < bytes.Length)
            {
                var read = stream.Read(bytes[totalRead..]);
                if (read == 0)
                {
                    if (totalRead != 0)
                        throw new InvalidDataException($"Wikipedia page ID run '{stream.Name}' ends with a truncated UInt64 value.");
                    value = 0;
                    return false;
                }
                totalRead += read;
            }
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return true;
        }

        public void Dispose() => stream.Dispose();
    }
}
