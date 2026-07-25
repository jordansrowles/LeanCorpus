namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages pending deletes with O(1) deduplication via a content-hashed index.
/// Replaces the O(n²) linear scan in <see cref="IndexWriter.QueueDelete"/>.
/// </summary>
internal sealed class PendingDeleteQueue
{
    private readonly List<DeleteTerm> _ordered = [];
    private readonly Dictionary<DeleteKey, int> _index = new(DeleteKeyComparer.Instance);
    private readonly Dictionary<int, byte[]> _prefixCache = new();

    /// <summary>Number of queued deletes.</summary>
    public int Count => _ordered.Count;

    /// <summary>
    /// Queues a delete. If an identical (field, term) delete already exists:
    /// a hard delete upgrades a soft one; otherwise the call is a no-op.
    /// </summary>
    internal bool Queue(int fieldOrdinal, byte[] termUtf8, byte[] prefixUtf8, bool isSoftDelete)
    {
        var key = new DeleteKey(fieldOrdinal, termUtf8);

        if (_index.TryGetValue(key, out int existingIdx))
        {
            var existing = _ordered[existingIdx];
            if (!isSoftDelete && existing.IsSoftDelete)
            {
                // Upgrade soft to hard
                _ordered[existingIdx] = new DeleteTerm(fieldOrdinal, termUtf8, prefixUtf8, false);
                _prefixCache[fieldOrdinal] = prefixUtf8;
                return true;
            }
            return false;
        }

        int idx = _ordered.Count;
        _ordered.Add(new DeleteTerm(fieldOrdinal, termUtf8, prefixUtf8, isSoftDelete));
        _index[key] = idx;
        _prefixCache[fieldOrdinal] = prefixUtf8;
        return true;
    }

    /// <summary>
    /// Returns the ordered list for <see cref="DeletionApplier"/>.
    /// Callers must not mutate the list or its elements.
    /// </summary>
    internal List<DeleteTerm> GetOrderedList() => _ordered;

    /// <summary>Returns a cached prefix for the given field ordinal.</summary>
    internal byte[]? GetPrefix(int fieldOrdinal) =>
        _prefixCache.TryGetValue(fieldOrdinal, out var prefix) ? prefix : null;

    /// <summary>Clears the queue and the hash index.</summary>
    internal void Clear()
    {
        _ordered.Clear();
        _index.Clear();
        _prefixCache.Clear();
    }
}

/// <summary>
/// Content-keyed (field ordinal, UTF-8 term bytes) for the delete hash index.
/// Equality is determined by ordinal match + byte-content match,
/// not array-reference identity.
/// </summary>
internal readonly struct DeleteKey : IEquatable<DeleteKey>
{
    public readonly int FieldOrdinal;
    public readonly byte[] TermUtf8;

    public DeleteKey(int fieldOrdinal, byte[] termUtf8)
    {
        FieldOrdinal = fieldOrdinal;
        TermUtf8 = termUtf8;
    }
    
    public bool Equals(DeleteKey other) =>
        FieldOrdinal == other.FieldOrdinal &&
        TermUtf8.AsSpan().SequenceEqual(other.TermUtf8);

    public override bool Equals(object? obj) => obj is DeleteKey other && Equals(other);

    public override int GetHashCode()
    {
        int hash = FieldOrdinal;
        // FNV-1a content hash on the term bytes
        foreach (byte b in TermUtf8)
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }
}

internal sealed class DeleteKeyComparer : IEqualityComparer<DeleteKey>
{
    public static readonly DeleteKeyComparer Instance = new();

    public bool Equals(DeleteKey x, DeleteKey y) => x.Equals(y);
    public int GetHashCode(DeleteKey obj) => obj.GetHashCode();
}
