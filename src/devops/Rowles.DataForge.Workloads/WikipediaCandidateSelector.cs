using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.DataForge.Workloads;

public sealed record WikipediaIndexEntry(long Offset, ulong PageId, string Title);

public sealed class WikipediaCandidate : IComparable<WikipediaCandidate>
{
    private readonly byte[] selectionKey;

    public WikipediaCandidate(WikipediaIndexEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Span<byte> input = stackalloc byte[34];
        Encoding.UTF8.GetBytes("leancorpus-wikipedia-en-v1", input);
        BinaryPrimitives.WriteUInt64BigEndian(input[26..], entry.PageId);
        selectionKey = SHA256.HashData(input);
    }

    public WikipediaIndexEntry Entry { get; }

    public ReadOnlyMemory<byte> SelectionKey => selectionKey;

    public int CompareTo(WikipediaCandidate? other)
    {
        if (other is null) return 1;
        var comparison = selectionKey.AsSpan().SequenceCompareTo(other.selectionKey);
        return comparison != 0 ? comparison : Entry.PageId.CompareTo(other.Entry.PageId);
    }
}

/// <summary>Selects the exact lowest page-id hashes without retaining the complete dump index.</summary>
public static class WikipediaCandidateSelector
{
    public const int InitialCandidateLimit = 32_768;
    public const int MaximumCandidateLimit = 1_048_576;
    public const int MaximumIndexLineBytes = 64 * 1024;

    public static WikipediaCandidateSelection Select(TextReader indexReader, int candidateLimit)
    {
        ArgumentNullException.ThrowIfNull(indexReader);
        if (candidateLimit is < 1 or > MaximumCandidateLimit)
            throw new ArgumentOutOfRangeException(nameof(candidateLimit));

        var heap = new CandidateMaxHeap(candidateLimit);
        var pageIds = new HashSet<ulong>();
        var uniqueOffsets = new List<long>();
        long previousOffset = -1;
        long scanned = 0;
        string? line;
        while ((line = ReadBoundedLine(indexReader)) is not null)
        {
            scanned++;
            var lineBytes = WikipediaTextNormaliserV1.StrictUtf8ByteCount(line);
            if (lineBytes > MaximumIndexLineBytes)
                throw new InvalidDataException($"Wikipedia index line {scanned.ToString(CultureInfo.InvariantCulture)} exceeds 64 KiB.");
            var entry = ParseIndexLine(line, scanned);
            if (entry.Offset < previousOffset)
                throw new InvalidDataException($"Wikipedia index offset decreases at line {scanned.ToString(CultureInfo.InvariantCulture)}.");
            if (entry.Offset != previousOffset)
                uniqueOffsets.Add(entry.Offset);
            previousOffset = entry.Offset;
            if (!pageIds.Add(entry.PageId))
                throw new InvalidDataException($"Wikipedia index contains duplicate page ID {entry.PageId.ToString(CultureInfo.InvariantCulture)}.");
            heap.Add(new WikipediaCandidate(entry));
        }

        return new WikipediaCandidateSelection(heap.ToSortedArray(), scanned, uniqueOffsets);
    }

    private static string? ReadBoundedLine(TextReader reader)
    {
        var line = new StringBuilder(256);
        while (true)
        {
            var value = reader.Read();
            if (value < 0)
                return line.Length == 0 ? null : FinishLine(line);
            if (value == '\n')
                return FinishLine(line);
            if (line.Length >= MaximumIndexLineBytes)
                throw new InvalidDataException("Wikipedia index line exceeds 64 KiB.");
            line.Append((char)value);
        }
    }

    private static string FinishLine(StringBuilder line)
    {
        if (line.Length > 0 && line[^1] == '\r')
            line.Length--;
        return line.ToString();
    }

    public static WikipediaIndexEntry ParseIndexLine(string line, long lineNumber)
    {
        ArgumentNullException.ThrowIfNull(line);
        var first = line.IndexOf(':');
        var second = first < 0 ? -1 : line.IndexOf(':', first + 1);
        if (first <= 0 || second <= first + 1 || second == line.Length - 1)
            throw new InvalidDataException($"Malformed Wikipedia index line {lineNumber.ToString(CultureInfo.InvariantCulture)}.");
        if (!long.TryParse(line.AsSpan(0, first), NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
            throw new InvalidDataException($"Invalid offset on Wikipedia index line {lineNumber.ToString(CultureInfo.InvariantCulture)}.");
        if (!ulong.TryParse(line.AsSpan(first + 1, second - first - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var pageId) || pageId == 0)
            throw new InvalidDataException($"Invalid page ID on Wikipedia index line {lineNumber.ToString(CultureInfo.InvariantCulture)}.");
        var title = line[(second + 1)..];
        _ = WikipediaTextNormaliserV1.StrictUtf8ByteCount(title);
        return new WikipediaIndexEntry(offset, pageId, title);
    }

    private sealed class CandidateMaxHeap(int capacity)
    {
        private readonly WikipediaCandidate[] items = new WikipediaCandidate[capacity];
        private int count;

        public void Add(WikipediaCandidate candidate)
        {
            if (count < items.Length)
            {
                var index = count++;
                items[index] = candidate;
                SiftUp(index);
                return;
            }
            if (candidate.CompareTo(items[0]) >= 0)
                return;
            items[0] = candidate;
            SiftDown(0);
        }

        public WikipediaCandidate[] ToSortedArray()
        {
            var result = items.AsSpan(0, count).ToArray();
            Array.Sort(result, static (left, right) => left.CompareTo(right));
            return result;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (items[index].CompareTo(items[parent]) <= 0)
                    break;
                (items[index], items[parent]) = (items[parent], items[index]);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                var left = index * 2 + 1;
                if (left >= count) return;
                var right = left + 1;
                var largest = right < count && items[right].CompareTo(items[left]) > 0 ? right : left;
                if (items[largest].CompareTo(items[index]) <= 0) return;
                (items[index], items[largest]) = (items[largest], items[index]);
                index = largest;
            }
        }
    }
}

public sealed record WikipediaCandidateSelection(IReadOnlyList<WikipediaCandidate> Candidates, long IndexEntriesScanned, IReadOnlyList<long> UniqueOffsets);
