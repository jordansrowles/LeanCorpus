using System.Buffers;
using System.Diagnostics.CodeAnalysis;
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
    /// <summary>When <c>true</c>, character offsets from the analyser are accumulated for term vector storage. When <c>false</c> (default), offsets are discarded to avoid the ~5× allocation overhead of jagged offset arrays.</summary>
    internal bool StoreTermVectors { get; set; }

    // ─── Postings: open-addressing hash table + parallel accumulator array ───
    public readonly BytesRefHash TermHash = new(8192);
    public readonly List<PostingAccumulator> PostingAccumulators = new(8192);

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

    // ─── Postings lookup API ───

    /// <summary>Decodes the qualified term string for a compact ID.</summary>
    public string GetTermString(int id) => TermHash.GetTermString(id);

    /// <summary>Returns the number of unique terms in the hash table.</summary>
    public int PostingsCount => TermHash.Count;

    /// <summary>
    /// Looks up or creates a posting accumulator by qualified term (UTF-8 bytes).
    /// This is the hot path — no string allocations, no dictionary probing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PostingAccumulator GetOrCreateAccumulatorUtf8(ReadOnlySpan<byte> qualifiedTermUtf8, string qualifiedTerm)
    {
        int id = TermHash.Add(qualifiedTermUtf8);
        if (id >= 0)
        {
            // New term
            var acc = new PostingAccumulator();
            if (id >= PostingAccumulators.Count)
                PostingAccumulators.Add(acc);
            else
                PostingAccumulators[id] = acc;
            PostingsRamBytes += acc.EstimatedBytes;
            return acc;
        }
        // Existing term: id is -(index + 1)
        return PostingAccumulators[-(id + 1)];
    }

    /// <summary>
    /// Looks up or creates a posting accumulator by qualified term string.
    /// Used by non-hot-path callers (string field indexing, DWPT merge).
    /// </summary>
    public PostingAccumulator GetOrCreateAccumulator(string qualifiedTerm)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(qualifiedTerm);
        return GetOrCreateAccumulatorUtf8(utf8, qualifiedTerm);
    }

    /// <summary>
    /// Tries to find an existing posting accumulator by qualified term string.
    /// </summary>
    public bool TryGetAccumulator(string qualifiedTerm, [MaybeNullWhen(false)] out PostingAccumulator acc)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(qualifiedTerm);
        int id = TermHash.Find(utf8);
        if (id >= 0)
        {
            acc = PostingAccumulators[id];
            return true;
        }
        acc = null;
        return false;
    }

    /// <summary>Iterates all (term, accumulator) pairs for flush/merge.</summary>
    public IEnumerable<(string Term, PostingAccumulator Acc)> EnumeratePostings()
    {
        for (int i = 0; i < TermHash.Count; i++)
            yield return (GetTermString(i), PostingAccumulators[i]);
    }

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

        return string.Concat(prefix, term);
    }

    /// <summary>
    /// Accumulates a posting — the indexing hot path.
    /// Encodes the qualified term as UTF-8 and probes the open-addressing hash table.
    /// </summary>
    public void AccumulatePosting(string fieldName, ReadOnlySpan<char> term, int docId, int position, byte[]? payload, bool storePayloads,
        FieldIndexOptions indexOptions = FieldIndexOptions.DocsAndFreqsAndPositions,
        int startOffset = 0, int endOffset = 0)
    {
        // Encode "fieldName\0term" as UTF-8 bytes for hash table probe
        if (!FieldPrefixCache.TryGetValue(fieldName, out var prefix))
        {
            prefix = string.Concat(fieldName, "\x00");
            FieldPrefixCache[fieldName] = prefix;
        }

        // Build UTF-8 qualified term in a stack-allocated buffer
        int prefixUtf8Len = System.Text.Encoding.UTF8.GetByteCount(prefix);
        int termUtf8Len = System.Text.Encoding.UTF8.GetByteCount(term);
        int totalUtf8Len = prefixUtf8Len + termUtf8Len;

        byte[]? rented = null;
        Span<byte> utf8Buf = totalUtf8Len <= 256
            ? stackalloc byte[totalUtf8Len]
            : (rented = ArrayPool<byte>.Shared.Rent(totalUtf8Len)).AsSpan(0, totalUtf8Len);

        System.Text.Encoding.UTF8.GetBytes(prefix, utf8Buf);
        System.Text.Encoding.UTF8.GetBytes(term, utf8Buf[prefixUtf8Len..]);

        var acc = GetOrCreateAccumulatorUtf8(utf8Buf, string.Empty); // string param unused when found via hash

        if (rented is not null)
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);

        long before = acc.EstimatedBytes;
        if (StoreTermVectors)
        {
            if (storePayloads && (acc.HasPayloads || payload is { Length: > 0 }))
                acc.AddWithPayload(docId, position, payload, indexOptions, startOffset, endOffset);
            else
                acc.Add(docId, position, indexOptions, startOffset, endOffset);
        }
        else
        {
            if (storePayloads && (acc.HasPayloads || payload is { Length: > 0 }))
                acc.AddWithPayload(docId, position, payload, indexOptions);
            else
                acc.Add(docId, position, indexOptions);
        }
        PostingsRamBytes += acc.EstimatedBytes - before;
    }

    /// <summary>
    /// Resets all buffers to empty state after a flush.
    /// </summary>
    public void Reset()
    {
        foreach (var acc in PostingAccumulators)
            acc.ReturnBuffers();
        PostingAccumulators.Clear();
        TermHash.Clear();
        StoredFieldIds.Clear();
        StoredFieldValues.Clear();
        StoredDocStarts.Clear();
        NumericFields.Clear();
        TermPool.Clear();
        FieldNames.Clear();
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
