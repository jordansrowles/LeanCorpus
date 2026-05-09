namespace Rowles.LeanLucene.Analysis.Analysers;

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
            return cached;

        string value = new(text);
        if (_cache.Count < _maxSize)
            _lookup[text] = value;

        return value;
    }
}
