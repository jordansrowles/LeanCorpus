using System.Collections.Concurrent;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Thread-safe cache for interned qualified term strings ("field\0term").
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}.AlternateLookup{TAlternateKey}"/>
/// for zero-allocation lookups with <see cref="ReadOnlySpan{T}"/> keys.
/// </summary>
internal sealed class QualifiedTermCache
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public QualifiedTermCache()
    {
        _lookup = _cache.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>
    /// Returns the cached string for the given qualified term span, or interns a new one.
    /// Zero-allocation on cache hit.
    /// </summary>
    public string GetOrAdd(ReadOnlySpan<char> qualifiedTerm)
    {
        if (_lookup.TryGetValue(qualifiedTerm, out string? cached))
            return cached;

        string value = new string(qualifiedTerm);
        _lookup.TryAdd(qualifiedTerm, value);
        return _cache.GetOrAdd(value, value);
    }
}
