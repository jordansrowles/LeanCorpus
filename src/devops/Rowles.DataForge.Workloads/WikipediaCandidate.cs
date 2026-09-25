using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.DataForge.Workloads;

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

