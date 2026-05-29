namespace Rowles.LeanCorpus.Analysis.Analysers;

internal sealed class TokenTextCache
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _maxSize;

    public TokenTextCache(int maxSize)
    {
        if (maxSize < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize));

        _maxSize = maxSize;
        _lookup = _cache.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public string GetOrAdd(ReadOnlySpan<char> text)
    {
        if (_lookup.TryGetValue(text, out var cached))
        {
            _hits++;
            return cached;
        }

        _misses++;
        string value = new(text);
        if (_cache.Count < _maxSize)
            _lookup[text] = value;

        return value;
    }

    /// <summary>
    /// Allocates a new string from the span, bypassing the intern cache.
    /// Use when the caller expects a near-zero cache hit rate, avoiding
    /// the hashing and dictionary-probe overhead of <see cref="GetOrAdd"/>.
    /// </summary>
    public static string Allocate(ReadOnlySpan<char> text) => new(text);

    private long _hits;
    private long _misses;

    /// <summary>
    /// Gets the number of times <see cref="GetOrAdd"/> returned a cached value.
    /// </summary>
    public long Hits => _hits;

    /// <summary>
    /// Gets the number of times <see cref="GetOrAdd"/> allocated a new string.
    /// </summary>
    public long Misses => _misses;

    /// <summary>
    /// Gets the cache hit ratio (0.0 to 1.0), or 0 if no lookups have occurred.
    /// </summary>
    public double HitRatio => _hits + _misses > 0 ? (double)_hits / (_hits + _misses) : 0;
}
