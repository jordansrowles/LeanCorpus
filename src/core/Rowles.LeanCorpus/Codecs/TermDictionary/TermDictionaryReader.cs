using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Store;
namespace Rowles.LeanCorpus.Codecs.TermDictionary;

/// <summary>
/// Reads a v3 .dic file: a real FST (Daciuk minimal acyclic transducer) emitting postings
/// offsets as outputs. All query primitives are thin wrappers around <see cref="FstReader"/>:
/// exact lookups are O(key length) arc walks; prefix/wildcard/fuzzy queries are native
/// FST × automaton intersections. v1 and v2 dictionaries are not accepted in the live read
/// path; the migrator must upgrade them first.
/// </summary>
internal sealed class TermDictionaryReader : IDisposable
{
    private readonly FstReader _fst;
    private readonly Lock _fuzzyCacheLock = new();
    private readonly Dictionary<FuzzyCacheKey, List<(string Term, long Offset, int Distance)>> _fuzzyCache = new();
    private readonly Lock _wildcardCacheLock = new();
    private readonly Dictionary<string, WildcardAutomaton> _wildcardCache = new(StringComparer.Ordinal);
    private const int MaxFuzzyCacheEntries = 128;
    private const int MaxWildcardCacheEntries = 64;
    private bool _disposed;

    private TermDictionaryReader(FstReader fst)
    {
        _fst = fst;
    }

    /// <summary>Opens a v3 dictionary; throws with a clear "run migrate" hint for older versions.</summary>
    public static TermDictionaryReader Open(string filePath)
    {
        using var input = new IndexInput(filePath);

        int magic = input.ReadInt32();
        if (magic != CodecConstants.Magic)
            throw new InvalidDataException(
                $"Invalid term dictionary (.dic) file: expected magic 0x{CodecConstants.Magic:X8}, got 0x{magic:X8}.");

        byte version = input.ReadByte();
        if (version != CodecConstants.TermDictionaryVersion)
            throw new InvalidDataException(
                $"Unsupported term dictionary format version {version}. This build expects v{CodecConstants.TermDictionaryVersion}. " +
                "Run 'leancorpus-cli migrate' (or IndexCodecMigrator) to upgrade the segment.");

        long remaining = input.Length - input.Position;
        if (remaining < 0 || remaining > int.MaxValue)
            throw new InvalidDataException("Term dictionary FST blob length is out of range.");

        var span = input.ReadSpan((int)remaining);
        var blob = span.ToArray();
        var fst = FstReader.Open(blob);
        return new TermDictionaryReader(fst);
    }

    // -- Exact lookups -----------------------------------------------------

    public bool TryGetPostingsOffset(string term, out long offset) => TryGetPostingsOffset(term.AsSpan(), out offset);

    public bool TryGetPostingsOffset(ReadOnlySpan<char> term, out long offset)
    {
        int byteCount = Encoding.UTF8.GetByteCount(term);
        if (byteCount <= 256)
        {
            Span<byte> utf8 = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(term, utf8);
            return _fst.TryGetOutput(utf8, out offset);
        }

        var rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            int written = Encoding.UTF8.GetBytes(term, rented);
            return _fst.TryGetOutput(rented.AsSpan(0, written), out offset);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // -- Prefix scans ------------------------------------------------------

    public List<(string Term, long Offset)> GetTermsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
    {
        var prefixUtf8 = Encoding.UTF8.GetBytes(qualifiedPrefix.ToArray());
        var results = new List<(string, long)>();
        foreach (var (key, output) in _fst.EnumerateWithPrefix(prefixUtf8))
            results.Add((Encoding.UTF8.GetString(key), output));
        return results;
    }

    public List<long> GetTermOffsetsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
    {
        Span<byte> prefixStack = stackalloc byte[256];
        var prefixUtf8 = EncodeUtf8(qualifiedPrefix, prefixStack, out byte[]? rented);
        var results = new List<long>();
        try
        {
            _fst.CollectOutputsWithPrefix(prefixUtf8, results);
        }
        finally
        {
            if (rented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
        return results;
    }

    // -- Wildcard ---------------------------------------------------------

    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        var qualifier = Encoding.UTF8.GetBytes(fieldPrefix);
        var wildcard = new WildcardAutomaton(pattern.ToString());
        var results = new List<(string, long)>();
        foreach (var (key, output, _) in _fst.IntersectAutomaton(wildcard, qualifier))
            results.Add((Encoding.UTF8.GetString(key), output));
        return results;
    }

    internal List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        Span<byte> qualStack = stackalloc byte[256];
        var qualifier = EncodeUtf8(fieldPrefix.AsSpan(), qualStack, out byte[]? rented);
        var wildcard = GetOrAddWildcardAutomaton(pattern);
        var results = new List<long>();
        try
        {
            _fst.CollectIntersectOutputs(wildcard, qualifier, results);
        }
        finally
        {
            if (rented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
        return results;
    }

    private WildcardAutomaton GetOrAddWildcardAutomaton(ReadOnlySpan<char> pattern)
    {
        var key = pattern.ToString();
        lock (_wildcardCacheLock)
        {
            if (_wildcardCache.TryGetValue(key, out var cached)) return cached;
            if (_wildcardCache.Count >= MaxWildcardCacheEntries)
                _wildcardCache.Clear();
            var built = new WildcardAutomaton(key);
            _wildcardCache[key] = built;
            return built;
        }
    }

    private static ReadOnlySpan<byte> EncodeUtf8(ReadOnlySpan<char> chars, Span<byte> stackBuffer, out byte[]? rented)
    {
        rented = null;
        int max = Encoding.UTF8.GetMaxByteCount(chars.Length);
        Span<byte> target;
        if (max <= stackBuffer.Length)
        {
            target = stackBuffer;
        }
        else
        {
            rented = System.Buffers.ArrayPool<byte>.Shared.Rent(max);
            target = rented;
        }
        int written = Encoding.UTF8.GetBytes(chars, target);
        return target.Slice(0, written);
    }

    // -- Fuzzy ------------------------------------------------------------

    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(
        string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions = 64)
    {
        var key = new FuzzyCacheKey(fieldPrefix, queryTerm.ToString(), maxEdits, maxExpansions);
        if (TryGetFuzzyCache(key, out var cached)) return cached;

        var qualifier = Encoding.UTF8.GetBytes(fieldPrefix);
        var lev = new LevenshteinAutomaton(queryTerm.ToString(), maxEdits);

        var results = new List<(string, long, int)>();
        foreach (var (k, output, finalState) in _fst.IntersectAutomaton(lev, qualifier))
        {
            int distance = lev.MinDistance(finalState);
            if (distance > maxEdits) continue;
            results.Add((Encoding.UTF8.GetString(k), output, distance));
        }

        // Cap to maxExpansions closest matches.
        if (results.Count > maxExpansions)
        {
            results.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            results = results.GetRange(0, maxExpansions);
        }

        StoreFuzzyCache(key, results);
        return results;
    }

    private bool TryGetFuzzyCache(FuzzyCacheKey key, out List<(string Term, long Offset, int Distance)> results)
    {
        lock (_fuzzyCacheLock)
        {
            if (_fuzzyCache.TryGetValue(key, out var cached))
            {
                results = cached;
                return true;
            }
        }
        results = [];
        return false;
    }

    private void StoreFuzzyCache(FuzzyCacheKey key, List<(string Term, long Offset, int Distance)> results)
    {
        lock (_fuzzyCacheLock)
        {
            if (_fuzzyCache.Count >= MaxFuzzyCacheEntries)
                _fuzzyCache.Clear();
            _fuzzyCache[key] = results;
        }
    }

    private readonly record struct FuzzyCacheKey(string FieldPrefix, string QueryTerm, int MaxEdits, int MaxExpansions);

    // -- Range / regex / contains / enumerate -----------------------------

    public List<(string Term, long Offset)> GetAllTermsForField(string fieldPrefix) => GetTermsWithPrefix(fieldPrefix.AsSpan());

    public List<(string Term, long Offset)> EnumerateAllTerms()
    {
        var results = new List<(string, long)>();
        foreach (var (key, output) in _fst.EnumerateAll())
            results.Add((Encoding.UTF8.GetString(key), output));
        return results;
    }

    public List<(string Term, long Offset)> GetTermsInRange(
        string fieldPrefix, string? lower, string? upper, bool includeLower = true, bool includeUpper = true)
    {
        var qualifier = Encoding.UTF8.GetBytes(fieldPrefix);
        var results = new List<(string, long)>();
        foreach (var (key, output) in _fst.EnumerateWithPrefix(qualifier))
        {
            var bare = Encoding.UTF8.GetString(key.AsSpan(qualifier.Length));
            if (lower is not null)
            {
                int cmp = string.CompareOrdinal(bare, lower);
                if (cmp < 0 || (cmp == 0 && !includeLower)) continue;
            }
            if (upper is not null)
            {
                int cmp = string.CompareOrdinal(bare, upper);
                if (cmp > 0 || (cmp == 0 && !includeUpper)) continue;
            }
            results.Add((Encoding.UTF8.GetString(key), output));
        }
        return results;
    }

    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex)
    {
        var qualifier = Encoding.UTF8.GetBytes(fieldPrefix);
        var results = new List<(string, long)>();
        foreach (var (key, output) in _fst.EnumerateWithPrefix(qualifier))
        {
            var bare = Encoding.UTF8.GetString(key.AsSpan(qualifier.Length));
            if (regex.IsMatch(bare))
                results.Add((Encoding.UTF8.GetString(key), output));
        }
        return results;
    }

    public List<long> GetTermOffsetsContaining(string fieldPrefix, ReadOnlySpan<char> literal)
    {
        Span<byte> qualStack = stackalloc byte[256];
        Span<byte> needleStack = stackalloc byte[256];
        var qualifier = EncodeUtf8(fieldPrefix.AsSpan(), qualStack, out byte[]? qualRented);
        var needleUtf8 = EncodeUtf8(literal, needleStack, out byte[]? needleRented);
        var results = new List<long>();
        try
        {
            _fst.CollectContainsOutputs(qualifier, needleUtf8, results);
        }
        finally
        {
            if (qualRented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(qualRented);
            if (needleRented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(needleRented);
        }
        return results;
    }

    /// <summary>
    /// Intersects the term dictionary with an automaton operating on the bare term bytes
    /// (after <paramref name="fieldPrefix"/>). Driven by <see cref="FstReader.IntersectAutomaton(IAutomaton, ReadOnlySpan{byte})"/>.
    /// </summary>
    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton)
    {
        var qualifier = Encoding.UTF8.GetBytes(fieldPrefix);
        var results = new List<(string, long)>();
        foreach (var (key, output, _) in _fst.IntersectAutomaton(automaton, qualifier))
            results.Add((Encoding.UTF8.GetString(key), output));
        return results;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
