using System.Text;
using System.Text.RegularExpressions;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Store;
namespace Rowles.LeanLucene.Codecs.Fst;

/// <summary>
/// Reads a v2 .dic file: compact byte-keyed sorted dictionary.
/// All data is loaded into contiguous arrays at open time — no per-term string allocation.
/// Binary search operates on raw UTF-8 bytes (~3× faster than char-span comparison).
/// </summary>
internal sealed class FSTReader
{
    private readonly long[] _offsets;
    private readonly int[] _keyStarts;
    private readonly byte[] _keyData;
    private readonly int _termCount;

    // Hash table for O(1) average-case exact term lookups
    private readonly int[] _hashBuckets;
    private readonly int[] _hashNext;
    private readonly int _hashMask;

    private FSTReader(long[] offsets, int[] keyStarts, byte[] keyData, int termCount,
        int[] hashBuckets, int[] hashNext, int hashMask)
    {
        _offsets = offsets;
        _keyStarts = keyStarts;
        _keyData = keyData;
        _termCount = termCount;
        _hashBuckets = hashBuckets;
        _hashNext = hashNext;
        _hashMask = hashMask;
    }

    /// <summary>
    /// Opens a v2 dictionary from an <see cref="IndexInput"/> positioned just after the codec header.
    /// </summary>
    public static FSTReader Open(IndexInput input)
    {
        int termCount = input.ReadInt32();
        if (termCount == 0)
            return new FSTReader([], [], [], 0, [-1], [], 0);

        // Read postings offsets (N × int64)
        var offsets = new long[termCount];
        for (int i = 0; i < termCount; i++)
            offsets[i] = input.ReadInt64();

        // Read key starts ((N+1) × int32)
        var keyStarts = new int[termCount + 1];
        for (int i = 0; i <= termCount; i++)
            keyStarts[i] = input.ReadInt32();

        // Read concatenated UTF-8 key data
        int totalKeyBytes = keyStarts[termCount];
        var keyData = new byte[totalKeyBytes];
        if (totalKeyBytes > 0)
        {
            var span = input.ReadSpan(totalKeyBytes);
            span.CopyTo(keyData);
        }

        // Build hash table for O(1) exact lookups
        int capacity = 1;
        while (capacity < termCount * 2) capacity <<= 1;
        int hashMask = capacity - 1;
        var hashBuckets = new int[capacity];
        var hashNext = new int[termCount];
        Array.Fill(hashBuckets, -1);
        Array.Fill(hashNext, -1);
        for (int i = 0; i < termCount; i++)
        {
            int h = HashBytes(keyData.AsSpan(keyStarts[i], keyStarts[i + 1] - keyStarts[i])) & hashMask;
            hashNext[i] = hashBuckets[h];
            hashBuckets[h] = i;
        }

        return new FSTReader(offsets, keyStarts, keyData, termCount, hashBuckets, hashNext, hashMask);
    }

    /// <summary>O(1) average-case hash lookup on UTF-8 byte keys (falls back to chain walk on collision).</summary>
    public bool TryGetPostingsOffset(ReadOnlySpan<byte> termUtf8, out long offset)
    {
        offset = 0;
        int h = HashBytes(termUtf8) & _hashMask;
        int idx = _hashBuckets[h];
        while (idx >= 0)
        {
            if (GetKeySpan(idx).SequenceEqual(termUtf8))
            {
                offset = _offsets[idx];
                return true;
            }
            idx = _hashNext[idx];
        }
        return false;
    }

    /// <summary>O(log N) binary search accepting a char span (encodes to UTF-8 internally).</summary>
    public bool TryGetPostingsOffset(ReadOnlySpan<char> term, out long offset)
    {
        int byteCount = Encoding.UTF8.GetByteCount(term);
        Span<byte> utf8 = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(term, utf8);
        return TryGetPostingsOffset(utf8, out offset);
    }

    /// <summary>Returns all terms sharing the given qualified prefix.</summary>
    public List<(string Term, long Offset)> GetTermsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
    {
        int byteCount = Encoding.UTF8.GetByteCount(qualifiedPrefix);
        Span<byte> prefixUtf8 = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(qualifiedPrefix, prefixUtf8);

        var results = new List<(string, long)>();
        int start = LowerBound(prefixUtf8);

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (key.StartsWith(prefixUtf8))
                results.Add((DecodeKey(i), _offsets[i]));
            else
                break;
        }
        return results;
    }

    /// <summary>Returns all terms matching a wildcard pattern for a given field.</summary>
    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        int prefixByteCount = Encoding.UTF8.GetByteCount(fieldPrefix);
        Span<byte> prefixUtf8 = prefixByteCount <= 256 ? stackalloc byte[prefixByteCount] : new byte[prefixByteCount];
        Encoding.UTF8.GetBytes(fieldPrefix, prefixUtf8);

        var scanPrefix = BuildWildcardScanPrefix(prefixUtf8, pattern, out byte[]? scanBuffer);
        var results = new List<(string, long)>();
        bool patternIsAscii = IsAscii(pattern);
        int start = LowerBound(scanPrefix);

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (!key.StartsWith(prefixUtf8))
                break;

            if (!key.StartsWith(scanPrefix))
                break;

            var bareTerm = key[prefixByteCount..];
            if (patternIsAscii && IsAscii(bareTerm))
            {
                if (!MatchesAsciiWildcard(bareTerm, pattern))
                    continue;

                results.Add((DecodeKey(i), _offsets[i]));
                continue;
            }

            var fullTerm = DecodeKey(i);
            if (WildcardQuery.Matches(fullTerm.AsSpan(fieldPrefix.Length), pattern))
                results.Add((fullTerm, _offsets[i]));
        }

        _ = scanBuffer;
        return results;
    }

    /// <summary>Returns postings offsets for wildcard matches without materialising term strings.</summary>
    internal List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        int prefixByteCount = Encoding.UTF8.GetByteCount(fieldPrefix);
        Span<byte> prefixUtf8 = prefixByteCount <= 256 ? stackalloc byte[prefixByteCount] : new byte[prefixByteCount];
        Encoding.UTF8.GetBytes(fieldPrefix, prefixUtf8);

        var scanPrefix = BuildWildcardScanPrefix(prefixUtf8, pattern, out byte[]? scanBuffer);
        var results = new List<long>();
        bool patternIsAscii = IsAscii(pattern);
        int start = LowerBound(scanPrefix);

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (!key.StartsWith(prefixUtf8))
                break;

            if (!key.StartsWith(scanPrefix))
                break;

            var bareTerm = key[prefixByteCount..];
            if (patternIsAscii && IsAscii(bareTerm))
            {
                if (MatchesAsciiWildcard(bareTerm, pattern))
                    results.Add(_offsets[i]);
                continue;
            }

            var fullTerm = DecodeKey(i);
            if (WildcardQuery.Matches(fullTerm.AsSpan(fieldPrefix.Length), pattern))
                results.Add(_offsets[i]);
        }

        _ = scanBuffer;
        return results;
    }

    /// <summary>Returns all terms within Levenshtein distance for a field, with edit distances.
    /// Uses prefix-sharing DP on sorted terms: consecutive terms sharing a prefix reuse
    /// the Levenshtein row up to the longest common prefix. Dead prefixes (row min > maxEdits)
    /// skip ahead via binary search.
    /// When more than <paramref name="maxExpansions"/> terms match, only the closest are kept.</summary>
    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions = 64)
    {
        int prefixByteCount = Encoding.UTF8.GetByteCount(fieldPrefix);
        Span<byte> prefixUtf8 = prefixByteCount <= 256 ? stackalloc byte[prefixByteCount] : new byte[prefixByteCount];
        Encoding.UTF8.GetBytes(fieldPrefix, prefixUtf8);

        int queryByteCount = Encoding.UTF8.GetByteCount(queryTerm);
        Span<byte> queryUtf8 = queryByteCount <= 256 ? stackalloc byte[queryByteCount] : new byte[queryByteCount];
        Encoding.UTF8.GetBytes(queryTerm, queryUtf8);
        bool queryIsAscii = queryByteCount == queryTerm.Length;

        int start = LowerBound(prefixUtf8);
        if (start >= _termCount) return [];

        // Non-ASCII queries fall back to the original per-term bounded Levenshtein
        if (!queryIsAscii)
            return GetFuzzyMatchesFallback(fieldPrefix, queryTerm, maxEdits, maxExpansions, prefixUtf8, start);

        // Single-edit queries: generate all 1-edit variants and do O(1) hash lookups
        if (maxEdits == 1)
            return GetFuzzyMatchesSingleEdit(prefixUtf8, queryUtf8, maxExpansions);

        // Phase 1: collect (ordinal, distance) pairs without decoding strings
        var candidates = new List<(int Ordinal, int Distance)>();
        int qLen = queryByteCount; // ASCII: byte count == char count
        int rowSize = qLen + 1;

        // dpStack[d] holds the DP row after processing d bytes of the bare term.
        // Consecutive sorted terms share a prefix → reuse rows up to the LCP.
        int stackCapacity = qLen + maxEdits + 4;
        var dpStack = new int[stackCapacity][];
        for (int r = 0; r < stackCapacity; r++)
            dpStack[r] = new int[rowSize];
        // Row 0 (empty prefix): row[j] = j
        for (int j = 0; j <= qLen; j++)
            dpStack[0][j] = j;

        // Build byte histogram of the query for character overlap pre-check.
        // Only effective for large dictionaries; at small scale the DP prefix-sharing
        // already prunes efficiently and the per-term histogram overhead is a net loss.
        bool useOverlapFilter = _termCount >= 50_000;
        Span<int> qHist = stackalloc int[256];
        Span<int> bareHist = stackalloc int[256];
        Span<byte> touchedByteKeys = stackalloc byte[256];
        if (useOverlapFilter)
        {
            qHist.Clear();
            for (int j = 0; j < qLen; j++)
                qHist[queryUtf8[j]]++;
            bareHist.Clear();
        }

        // Track the dpStack source term separately from prevBare.
        // Length-filtered and dead terms may not fully compute dpStack rows,
        // so LCP must be computed against the term that last modified the stack.
        int dpBareStart = -1;
        int dpBareLen = 0;
        int dpValidDepth = 0; // dpStack[0..dpValidDepth] are valid

        for (int idx = start; idx < _termCount; idx++)
        {
            var key = GetKeySpan(idx);
            if (!key.StartsWith(prefixUtf8))
                break;

            var bare = key[prefixByteCount..];
            int bareLen = bare.Length;

            // Length filter: edit distance is at least |len difference|
            if (Math.Abs(bareLen - qLen) > maxEdits)
                continue;

            // Character multiset overlap pre-check: skip terms that cannot
            // possibly be within maxEdits, avoiding the expensive DP loop
            if (useOverlapFilter)
            {
                int overlap = 0;
                int touchedByteCount = 0;
                for (int j = 0; j < bareLen; j++)
                {
                    byte b = bare[j];
                    if (bareHist[b] == 0)
                        touchedByteKeys[touchedByteCount++] = b;
                    bareHist[b]++;
                    if (bareHist[b] <= qHist[b])
                        overlap++;
                }
                for (int j = 0; j < touchedByteCount; j++)
                    bareHist[touchedByteKeys[j]] = 0;
                int minOverlapNeeded = Math.Min(qLen, bareLen) - maxEdits;
                if (overlap < minOverlapNeeded)
                    continue;
            }

            // Find LCP with the last term that modified dpStack
            int lcp = 0;
            if (dpBareStart >= 0)
            {
                var dpBare = _keyData.AsSpan(dpBareStart, dpBareLen);
                int minLen = Math.Min(Math.Min(dpBareLen, bareLen), dpValidDepth);
                while (lcp < minLen && dpBare[lcp] == bare[lcp])
                    lcp++;
            }

            // Grow dpStack if bare term is longer than expected
            if (bareLen + 1 > stackCapacity)
            {
                int newCap = bareLen + 4;
                var newStack = new int[newCap][];
                Array.Copy(dpStack, newStack, stackCapacity);
                for (int r = stackCapacity; r < newCap; r++)
                    newStack[r] = new int[rowSize];
                dpStack = newStack;
                stackCapacity = newCap;
            }

            // Advance DP from LCP depth to full bare length
            bool dead = false;
            int lastComputedDepth = lcp;
            for (int d = lcp; d < bareLen; d++)
            {
                var prevRow = dpStack[d];
                var currRow = dpStack[d + 1];
                currRow[0] = d + 1;
                int rowMin = d + 1;
                byte b = bare[d];

                for (int j = 1; j <= qLen; j++)
                {
                    int cost = queryUtf8[j - 1] == b ? 0 : 1;
                    int val = prevRow[j - 1] + cost;
                    int del = prevRow[j] + 1;
                    if (del < val) val = del;
                    int ins = currRow[j - 1] + 1;
                    if (ins < val) val = ins;
                    currRow[j] = val;
                    if (val < rowMin) rowMin = val;
                }

                lastComputedDepth = d + 1;
                if (rowMin > maxEdits)
                {
                    dead = true;
                    break;
                }
            }

            // Update dpStack source to this term (it actually modified the stack)
            dpBareStart = _keyStarts[idx] + prefixByteCount;
            dpBareLen = bareLen;
            dpValidDepth = lastComputedDepth;

            if (dead)
                continue;

            int finalDist = dpStack[bareLen][qLen];
            if (finalDist <= maxEdits)
                candidates.Add((idx, finalDist));
        }

        // Phase 2: truncate to maxExpansions closest matches, then decode strings
        if (maxExpansions > 0 && candidates.Count > maxExpansions)
        {
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            candidates.RemoveRange(maxExpansions, candidates.Count - maxExpansions);
        }

        var results = new List<(string, long, int)>(candidates.Count);
        foreach (var (ordinal, distance) in candidates)
            results.Add((DecodeKey(ordinal), _offsets[ordinal], distance));

        return results;
    }

    /// <summary>
    /// Optimised path for maxEdits == 1 on ASCII queries. Generates all possible
    /// single-edit variants (substitution, deletion, insertion with lowercase a-z)
    /// and performs O(1) hash lookups instead of scanning the entire term dictionary.
    /// Total lookups: 25L + L + 26(L+1) = 52L + 26 where L is the query length.
    /// </summary>
    private List<(string Term, long Offset, int Distance)> GetFuzzyMatchesSingleEdit(
        ReadOnlySpan<byte> prefixUtf8, ReadOnlySpan<byte> queryUtf8, int maxExpansions)
    {
        var results = new List<(string, long, int)>();
        var seenOffsets = new HashSet<long>();
        int qLen = queryUtf8.Length;
        int prefixLen = prefixUtf8.Length;

        // Buffer large enough for prefix + query + 1 byte (insertion case)
        int maxBufLen = prefixLen + qLen + 1;
        Span<byte> buffer = maxBufLen <= 256 ? stackalloc byte[maxBufLen] : new byte[maxBufLen];
        prefixUtf8.CopyTo(buffer);

        // Exact match (distance 0)
        queryUtf8.CopyTo(buffer[prefixLen..]);
        if (TryGetPostingsOffset(buffer[..(prefixLen + qLen)], out long exactOffset))
        {
            results.Add((Encoding.UTF8.GetString(buffer[..(prefixLen + qLen)]), exactOffset, 0));
            seenOffsets.Add(exactOffset);
        }

        // Substitutions: replace each query byte with each of a-z (excluding original)
        for (int i = 0; i < qLen; i++)
        {
            // Reset the term portion each time
            queryUtf8.CopyTo(buffer[prefixLen..]);
            byte original = queryUtf8[i];
            for (byte c = (byte)'a'; c <= (byte)'z'; c++)
            {
                if (c == original) continue;
                buffer[prefixLen + i] = c;
                if (TryGetPostingsOffset(buffer[..(prefixLen + qLen)], out long offset) && seenOffsets.Add(offset))
                {
                    results.Add((Encoding.UTF8.GetString(buffer[..(prefixLen + qLen)]), offset, 1));
                    if (maxExpansions > 0 && results.Count >= maxExpansions) return results;
                }
            }
        }

        // Deletions: remove each byte position to produce a (qLen-1) term
        if (qLen > 1)
        {
            for (int i = 0; i < qLen; i++)
            {
                queryUtf8[..i].CopyTo(buffer[prefixLen..]);
                queryUtf8[(i + 1)..].CopyTo(buffer[(prefixLen + i)..]);
                int variantLen = prefixLen + qLen - 1;
                if (TryGetPostingsOffset(buffer[..variantLen], out long offset) && seenOffsets.Add(offset))
                {
                    results.Add((Encoding.UTF8.GetString(buffer[..variantLen]), offset, 1));
                    if (maxExpansions > 0 && results.Count >= maxExpansions) return results;
                }
            }
        }

        // Insertions: insert each letter at each position to produce a (qLen+1) term
        for (int i = 0; i <= qLen; i++)
        {
            for (byte c = (byte)'a'; c <= (byte)'z'; c++)
            {
                queryUtf8[..i].CopyTo(buffer[prefixLen..]);
                buffer[prefixLen + i] = c;
                queryUtf8[i..].CopyTo(buffer[(prefixLen + i + 1)..]);
                int variantLen = prefixLen + qLen + 1;
                if (TryGetPostingsOffset(buffer[..variantLen], out long offset) && seenOffsets.Add(offset))
                {
                    results.Add((Encoding.UTF8.GetString(buffer[..variantLen]), offset, 1));
                    if (maxExpansions > 0 && results.Count >= maxExpansions) return results;
                }
            }
        }

        return results;
    }

    /// <summary>Original O(T) linear scan fallback for non-ASCII queries.</summary>
    private List<(string Term, long Offset, int Distance)> GetFuzzyMatchesFallback(
        string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions,
        ReadOnlySpan<byte> prefixUtf8, int start)
    {
        int queryTermLen = queryTerm.Length;
        var candidates = new List<(int Ordinal, int Distance)>();

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (!key.StartsWith(prefixUtf8))
                break;

            var fullTerm = DecodeKey(i);
            var bareTerm = fullTerm.AsSpan(fieldPrefix.Length);

            if (Math.Abs(bareTerm.Length - queryTermLen) > maxEdits)
                continue;

            int distance = LevenshteinDistance.ComputeBounded(queryTerm, bareTerm, maxEdits);
            if (distance <= maxEdits)
                candidates.Add((i, distance));
        }

        // Truncate to maxExpansions closest matches
        if (maxExpansions > 0 && candidates.Count > maxExpansions)
        {
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            candidates.RemoveRange(maxExpansions, candidates.Count - maxExpansions);
        }

        var results = new List<(string, long, int)>(candidates.Count);
        foreach (var (ordinal, distance) in candidates)
            results.Add((DecodeKey(ordinal), _offsets[ordinal], distance));

        return results;
    }

    /// <summary>Returns all terms for a field (all terms with prefix "field\0").</summary>
    public List<(string Term, long Offset)> GetAllTermsForField(string fieldPrefix)
    {
        return GetTermsWithPrefix(fieldPrefix.AsSpan());
    }

    /// <summary>Enumerates all terms and their postings offsets in sorted order.</summary>
    public List<(string Term, long Offset)> EnumerateAllTerms()
    {
        var results = new List<(string, long)>(_termCount);
        for (int i = 0; i < _termCount; i++)
        {
            int start = _keyStarts[i];
            int len = _keyStarts[i + 1] - start;
            string term = System.Text.Encoding.UTF8.GetString(_keyData, start, len);
            results.Add((term, _offsets[i]));
        }
        return results;
    }

    /// <summary>Returns terms whose bare value falls within a lexicographic range.</summary>
    public List<(string Term, long Offset)> GetTermsInRange(
        string fieldPrefix,
        string? lower, string? upper,
        bool includeLower = true, bool includeUpper = true)
    {
        string scanKey = lower is not null ? fieldPrefix + lower : fieldPrefix;
        int scanByteCount = Encoding.UTF8.GetByteCount(scanKey);
        Span<byte> scanUtf8 = scanByteCount <= 256 ? stackalloc byte[scanByteCount] : new byte[scanByteCount];
        Encoding.UTF8.GetBytes(scanKey, scanUtf8);

        int prefixByteCount = Encoding.UTF8.GetByteCount(fieldPrefix);
        Span<byte> prefixUtf8 = prefixByteCount <= 256 ? stackalloc byte[prefixByteCount] : new byte[prefixByteCount];
        Encoding.UTF8.GetBytes(fieldPrefix, prefixUtf8);

        var results = new List<(string, long)>();
        int start = LowerBound(scanUtf8);

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (!key.StartsWith(prefixUtf8))
                break;

            var fullTerm = DecodeKey(i);
            var bareTerm = fullTerm.AsSpan(fieldPrefix.Length);

            if (lower is not null)
            {
                int cmp = bareTerm.SequenceCompareTo(lower.AsSpan());
                if (cmp < 0 || (cmp == 0 && !includeLower)) continue;
            }

            if (upper is not null)
            {
                int cmp = bareTerm.SequenceCompareTo(upper.AsSpan());
                if (cmp > 0 || (cmp == 0 && !includeUpper)) break;
            }

            results.Add((fullTerm, _offsets[i]));
        }
        return results;
    }

    /// <summary>Returns terms for a field whose bare text matches the given compiled regex.</summary>
    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex)
    {
        int prefixByteCount = Encoding.UTF8.GetByteCount(fieldPrefix);
        Span<byte> prefixUtf8 = prefixByteCount <= 256 ? stackalloc byte[prefixByteCount] : new byte[prefixByteCount];
        Encoding.UTF8.GetBytes(fieldPrefix, prefixUtf8);

        // Extract literal prefix from the regex pattern for byte-level gating.
        var pattern = regex.ToString().AsSpan();
        if (pattern.Length > 0 && pattern[0] == '^')
            pattern = pattern[1..];
        int litEnd = 0;
        while (litEnd < pattern.Length && !IsRegexMeta(pattern[litEnd]))
            litEnd++;

        int scanPrefixLen;
        byte[]? scanBuf = null;
        if (litEnd > 0)
        {
            var literalSpan = pattern[..litEnd];
            int literalByteCount = Encoding.UTF8.GetByteCount(literalSpan);
            scanPrefixLen = prefixByteCount + literalByteCount;
            scanBuf = new byte[scanPrefixLen];
            prefixUtf8.CopyTo(scanBuf);
            Encoding.UTF8.GetBytes(literalSpan, scanBuf.AsSpan(prefixByteCount));
        }
        else
        {
            scanPrefixLen = prefixByteCount;
        }

        ReadOnlySpan<byte> scanPrefix = scanBuf is not null ? scanBuf.AsSpan(0, scanPrefixLen) : prefixUtf8;

        var results = new List<(string, long)>();
        int start = LowerBound(scanPrefix);

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (!key.StartsWith(prefixUtf8))
                break;

            if (scanBuf is not null && !key.StartsWith(scanPrefix))
                break;

            var fullTerm = DecodeKey(i);
            var bareTerm = fullTerm.AsSpan(fieldPrefix.Length);
            if (regex.IsMatch(bareTerm))
                results.Add((fullTerm, _offsets[i]));
        }
        return results;
    }

    private static bool IsRegexMeta(char c) =>
        c is '.' or '*' or '+' or '?' or '[' or '(' or '{' or '|' or '\\' or '^' or '$';

    /// <summary>
    /// Intersects the term dictionary with an automaton, returning matching terms.
    /// Operates on bare term bytes (after fieldPrefix). Uses CanMatch for pruning.
    /// </summary>
    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton)
    {
        int prefixByteCount = Encoding.UTF8.GetByteCount(fieldPrefix);
        Span<byte> prefixUtf8 = prefixByteCount <= 256 ? stackalloc byte[prefixByteCount] : new byte[prefixByteCount];
        Encoding.UTF8.GetBytes(fieldPrefix, prefixUtf8);

        var results = new List<(string, long)>();
        int start = LowerBound(prefixUtf8);

        for (int i = start; i < _termCount; i++)
        {
            var key = GetKeySpan(i);
            if (!key.StartsWith(prefixUtf8))
                break;

            // Run bare term bytes through the automaton
            var bareBytes = key[prefixByteCount..];
            int state = automaton.Start;
            bool dead = false;

            for (int j = 0; j < bareBytes.Length; j++)
            {
                state = automaton.Step(state, bareBytes[j]);
                if (!automaton.CanMatch(state))
                {
                    dead = true;
                    break;
                }
            }

            if (!dead && automaton.IsAccept(state))
                results.Add((DecodeKey(i), _offsets[i]));
        }

        return results;
    }

    /// <summary>FNV-1a hash of a byte span.</summary>
    private static int HashBytes(ReadOnlySpan<byte> data)
    {
        uint hash = 2166136261;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 16777619;
        }
        return (int)(hash & 0x7FFFFFFF);
    }

    private static ReadOnlySpan<byte> BuildWildcardScanPrefix(
        ReadOnlySpan<byte> fieldPrefixUtf8,
        ReadOnlySpan<char> pattern,
        out byte[]? scanBuffer)
    {
        scanBuffer = null;

        int literalEnd = 0;
        while (literalEnd < pattern.Length && pattern[literalEnd] != '*' && pattern[literalEnd] != '?')
            literalEnd++;

        if (literalEnd == 0)
            return fieldPrefixUtf8;

        var literalPrefix = pattern[..literalEnd];
        int literalByteCount = Encoding.UTF8.GetByteCount(literalPrefix);
        scanBuffer = new byte[fieldPrefixUtf8.Length + literalByteCount];
        fieldPrefixUtf8.CopyTo(scanBuffer);
        Encoding.UTF8.GetBytes(literalPrefix, scanBuffer.AsSpan(fieldPrefixUtf8.Length));
        return scanBuffer;
    }

    private static bool MatchesAsciiWildcard(ReadOnlySpan<byte> term, ReadOnlySpan<char> pattern)
    {
        if (!MatchesAsciiTrailingLiteral(term, pattern))
            return false;

        int t = 0, p = 0;
        int starT = -1, starP = -1;

        while (t < term.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || (byte)pattern[p] == term[t]))
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }

    private static bool MatchesAsciiTrailingLiteral(ReadOnlySpan<byte> term, ReadOnlySpan<char> pattern)
    {
        int suffixStart = pattern.Length;
        while (suffixStart > 0 && pattern[suffixStart - 1] != '*' && pattern[suffixStart - 1] != '?')
            suffixStart--;

        int suffixLength = pattern.Length - suffixStart;
        if (suffixLength == 0)
            return true;
        if (term.Length < suffixLength)
            return false;

        int termStart = term.Length - suffixLength;
        for (int i = 0; i < suffixLength; i++)
        {
            if (term[termStart + i] != (byte)pattern[suffixStart + i])
                return false;
        }

        return true;
    }

    private static bool IsAscii(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (c > 0x7F)
                return false;
        }

        return true;
    }

    private static bool IsAscii(ReadOnlySpan<byte> value)
    {
        foreach (byte b in value)
        {
            if (b > 0x7F)
                return false;
        }

        return true;
    }

    /// <summary>Returns the raw UTF-8 byte span for term at ordinal index.</summary>
    private ReadOnlySpan<byte> GetKeySpan(int ordinal)
    {
        int start = _keyStarts[ordinal];
        int end = _keyStarts[ordinal + 1];
        return _keyData.AsSpan(start, end - start);
    }

    /// <summary>Decodes the UTF-8 key at ordinal index to a string.</summary>
    private string DecodeKey(int ordinal)
    {
        var span = GetKeySpan(ordinal);
        return Encoding.UTF8.GetString(span);
    }

    /// <summary>Returns the first ordinal where term >= key (lower bound on UTF-8 bytes).</summary>
    private int LowerBound(ReadOnlySpan<byte> key)
    {
        int lo = 0, hi = _termCount;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (GetKeySpan(mid).SequenceCompareTo(key) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>Returns the first ordinal >= lo where term >= key.</summary>
    private int LowerBound(ReadOnlySpan<byte> key, int lo)
    {
        int hi = _termCount;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (GetKeySpan(mid).SequenceCompareTo(key) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}
