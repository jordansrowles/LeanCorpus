using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Holds all in-memory document buffer state for <see cref="IndexWriter"/>.
/// Consolidates the ~25 buffer collections that were previously scattered
/// across IndexWriter into a single class, enabling the segment flusher
/// to operate as a pure function of buffer state  ->  files on disk.
/// </summary>
internal sealed class DocumentBufferState
{
    // Unified posting accumulator keyed by qualified term ("field\0term")
    public Dictionary<string, PostingAccumulator> Postings = new(8192, StringComparer.Ordinal);

    // Flat stored field buffer: parallel arrays indexed by entry position
    public List<int> StoredFieldIds = new(4096);
    public List<StoredFieldValue> StoredFieldValues = new(4096);
    public List<int> StoredDocStarts = new(256);
    public readonly Dictionary<string, int> StoredFieldNameToId = new(StringComparer.Ordinal);
    public readonly List<string> StoredFieldIdToName = new();

    // Buffered numeric fields per document
    public List<Dictionary<string, double>> NumericFields = [];

    // Per-field numeric values for range indexing: field  ->  docId  ->  value
    public Dictionary<string, Dictionary<int, double>> NumericIndex = new();

    // Buffered vectors: field  ->  docId  ->  vector
    public Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors = new(StringComparer.Ordinal);

    // Term intern pool
    public readonly HashSet<string> TermPool = new(4096, StringComparer.Ordinal);

    // Per-field per-doc token counts for O(1) per-field norm computation
    public Dictionary<string, int[]> DocTokenCounts = new(StringComparer.Ordinal);

    // Per-field per-doc index-time boosts
    public Dictionary<string, Dictionary<int, float>> FieldBoosts = new(StringComparer.Ordinal);

    // Track field names seen in this flush
    public readonly HashSet<string> FieldNames = new(StringComparer.Ordinal);

    // Cache qualified term strings to avoid repeated string.Concat
    public Dictionary<string, string> QualifiedTermPool = new(8192, StringComparer.Ordinal);

    // Cache field name prefixes ("fieldName\0") to avoid repeated prefix construction
    public readonly Dictionary<string, string> FieldPrefixCache = new(StringComparer.Ordinal);

    // DocValues accumulators: field  ->  per-doc values
    public Dictionary<string, List<double>> NumericDocValues = new(StringComparer.Ordinal);
    public Dictionary<string, List<string?>> SortedDocValues = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues = new(StringComparer.Ordinal);

    // Sorted terms buffer (used during flush)
    public readonly List<string> SortedTermsBuffer = new(capacity: 10000);

    // Parent bitset for block-join indexing
    public HashSet<int>? ParentDocIds;

    // Document count in the current buffer
    public int DocCount;

    // Incrementally tracked RAM estimates
    public long EstimatedRamBytes;
    public long PostingsRamBytes;

    public string CanonicaliseTerm(string term)
    {
        if (TermPool.TryGetValue(term, out var canonical))
            return canonical;
        TermPool.Add(term);
        return term;
    }

    /// <summary>
    /// Returns a pooled qualified term string ("field\0term") directly from a token span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetOrCreateQualifiedTerm(string fieldName, ReadOnlySpan<char> term)
    {
        if (!FieldPrefixCache.TryGetValue(fieldName, out var prefix))
        {
            prefix = string.Concat(fieldName, "\x00");
            FieldPrefixCache[fieldName] = prefix;
        }

        int totalLen = prefix.Length + term.Length;
        Span<char> buf = totalLen <= 256 ? stackalloc char[totalLen] : new char[totalLen];
        prefix.AsSpan().CopyTo(buf);
        term.CopyTo(buf[prefix.Length..]);

        var lookup = QualifiedTermPool.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(buf, out var pooled))
            return pooled;

        var qualifiedTerm = new string(buf);
        QualifiedTermPool[qualifiedTerm] = qualifiedTerm;
        return qualifiedTerm;
    }

    /// <summary>
    /// Accumulates a posting for a single token, combining qualified-term interning and
    /// postings lookup into one alternate-lookup probe to avoid a double hash computation.
    /// </summary>
    public void AccumulatePosting(string fieldName, ReadOnlySpan<char> term, int docId, int position, byte[]? payload, bool storePayloads)
    {
        if (!FieldPrefixCache.TryGetValue(fieldName, out var prefix))
        {
            prefix = string.Concat(fieldName, "\x00");
            FieldPrefixCache[fieldName] = prefix;
        }

        int totalLen = prefix.Length + term.Length;
        Span<char> buf = totalLen <= 256 ? stackalloc char[totalLen] : new char[totalLen];
        prefix.AsSpan().CopyTo(buf);
        term.CopyTo(buf[prefix.Length..]);

        var lookup = Postings.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(buf, out var acc))
        {
            // Cache hit  --  the key string is already interned inside Postings.
        }
        else
        {
            acc = new PostingAccumulator();
            var qualifiedTerm = new string(buf);
            Postings[qualifiedTerm] = acc;
            PostingsRamBytes += acc.EstimatedBytes;
        }

        long before = acc.EstimatedBytes;
        if (storePayloads && (acc.HasPayloads || payload is { Length: > 0 }))
            acc.AddWithPayload(docId, position, payload);
        else
            acc.Add(docId, position);
        PostingsRamBytes += acc.EstimatedBytes - before;
    }

    /// <summary>
    /// Resets all buffers to empty state after a flush.
    /// </summary>
    public void Reset()
    {
        foreach (var acc in Postings.Values)
            acc.ReturnBuffers();
        Postings.Clear();
        StoredFieldIds.Clear();
        StoredFieldValues.Clear();
        StoredDocStarts.Clear();
        NumericFields.Clear();
        TermPool.Clear();
        FieldNames.Clear();
        QualifiedTermPool.Clear();
        NumericIndex.Clear();
        Vectors.Clear();
        NumericDocValues.Clear();
        SortedDocValues.Clear();
        SortedSetDocValues.Clear();
        SortedNumericDocValues.Clear();
        BinaryDocValues.Clear();
        FieldBoosts.Clear();
        SortedTermsBuffer.Clear();
        DocCount = 0;
        EstimatedRamBytes = 0;
        PostingsRamBytes = 0;
        DocTokenCounts.Clear();
        ParentDocIds = null;
    }
}
