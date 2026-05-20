using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.TermDictionary.Legacy;

/// <summary>
/// Legacy v1 term dictionary reader. The v1 layout is a flat skip block followed by length-prefixed
/// (termLen:int32, termBytes, postingsOffset:int64) records. All terms and offsets are materialised
/// at open time; an optional <see cref="FrozenDictionary{TKey, TValue}"/> provides O(1) exact lookups.
/// Retained solely for use by <c>IndexCodecMigrator</c> when reading pre-v3 segments.
/// </summary>
internal sealed class TermDictionaryV1Reader
{
    private readonly string[] _allTerms;
    private readonly long[] _allOffsets;
    private readonly FrozenDictionary<string, int> _termHash;

    private TermDictionaryV1Reader(string[] allTerms, long[] allOffsets)
    {
        _allTerms = allTerms;
        _allOffsets = allOffsets;
        var tempHash = new Dictionary<string, int>(allTerms.Length, StringComparer.Ordinal);
        for (int i = 0; i < allTerms.Length; i++)
            tempHash[allTerms[i]] = i;
        _termHash = tempHash.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Opens a v1 dictionary from an <see cref="IndexInput"/> positioned just after the codec header.
    /// </summary>
    public static TermDictionaryV1Reader Open(IndexInput input)
    {
        int skipCount = input.ReadInt32();
        for (int i = 0; i < skipCount; i++)
        {
            int skipTermLen = input.ReadInt32();
            input.ReadSpan(skipTermLen);
            input.ReadInt64();
        }

        long dataEnd = input.Length;
        var terms = new List<string>();
        var offsets = new List<long>();

        while (input.Position < dataEnd)
        {
            int termLen = input.ReadInt32();
            string term = System.Text.Encoding.UTF8.GetString(input.ReadSpan(termLen));
            long offset = input.ReadInt64();
            terms.Add(term);
            offsets.Add(offset);
        }

        return new TermDictionaryV1Reader([.. terms], [.. offsets]);
    }

    public int Count => _allTerms.Length;

    public bool TryGetPostingsOffset(ReadOnlySpan<char> term, out long offset)
    {
        offset = 0;
        var termStr = term.ToString();
        if (_termHash.TryGetValue(termStr, out int idx))
        {
            offset = _allOffsets[idx];
            return true;
        }
        return false;
    }

    public List<(string Term, long Offset)> GetTermsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
    {
        var results = new List<(string, long)>();
        int start = LowerBound(qualifiedPrefix);
        for (int i = start; i < _allTerms.Length; i++)
        {
            if (_allTerms[i].AsSpan().StartsWith(qualifiedPrefix))
                results.Add((_allTerms[i], _allOffsets[i]));
            else
                break;
        }
        return results;
    }

    public List<long> GetTermOffsetsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
    {
        var results = new List<long>();
        int start = LowerBound(qualifiedPrefix);
        for (int i = start; i < _allTerms.Length; i++)
        {
            if (_allTerms[i].AsSpan().StartsWith(qualifiedPrefix))
                results.Add(_allOffsets[i]);
            else
                break;
        }
        return results;
    }

    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        var results = new List<(string, long)>();
        int start = LowerBound(fieldPrefix.AsSpan());
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;
            var bareTerm = term.AsSpan(fieldPrefix.Length);
            if (WildcardQuery.Matches(bareTerm, pattern))
                results.Add((term, _allOffsets[i]));
        }
        return results;
    }

    public List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        var results = new List<long>();
        int start = LowerBound(fieldPrefix.AsSpan());
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;
            var bareTerm = term.AsSpan(fieldPrefix.Length);
            if (WildcardQuery.Matches(bareTerm, pattern))
                results.Add(_allOffsets[i]);
        }
        return results;
    }

    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions)
    {
        var results = new List<(string, long, int)>();
        int start = LowerBound(fieldPrefix.AsSpan());
        int queryTermLen = queryTerm.Length;
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;
            var bareTerm = term.AsSpan(fieldPrefix.Length);
            if (Math.Abs(bareTerm.Length - queryTermLen) > maxEdits)
                continue;
            int distance = LevenshteinDistance.ComputeBounded(queryTerm, bareTerm, maxEdits);
            if (distance <= maxEdits)
                results.Add((term, _allOffsets[i], distance));
        }

        if (maxExpansions > 0 && results.Count > maxExpansions)
        {
            results.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            results.RemoveRange(maxExpansions, results.Count - maxExpansions);
        }

        return results;
    }

    public List<(string Term, long Offset)> EnumerateAllTerms()
    {
        var results = new List<(string, long)>(_allTerms.Length);
        for (int i = 0; i < _allTerms.Length; i++)
            results.Add((_allTerms[i], _allOffsets[i]));
        return results;
    }

    public List<(string Term, long Offset)> GetTermsInRange(
        string fieldPrefix,
        string? lower, string? upper,
        bool includeLower, bool includeUpper)
    {
        var results = new List<(string, long)>();
        string scanKey = lower is not null ? fieldPrefix + lower : fieldPrefix;
        int start = LowerBound(scanKey.AsSpan());
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;
            var bareTerm = term.AsSpan(fieldPrefix.Length);
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
            results.Add((term, _allOffsets[i]));
        }
        return results;
    }

    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex)
    {
        var results = new List<(string, long)>();
        int start = LowerBound(fieldPrefix.AsSpan());
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;
            var bareTerm = term.AsSpan(fieldPrefix.Length);
            if (regex.IsMatch(bareTerm))
                results.Add((term, _allOffsets[i]));
        }
        return results;
    }

    public List<long> GetTermOffsetsContaining(string fieldPrefix, ReadOnlySpan<char> literal)
    {
        var results = new List<long>();
        int start = LowerBound(fieldPrefix.AsSpan());
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;

            if (term.AsSpan(fieldPrefix.Length).Contains(literal, StringComparison.Ordinal))
                results.Add(_allOffsets[i]);
        }
        return results;
    }

    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton)
    {
        var results = new List<(string, long)>();
        int start = LowerBound(fieldPrefix.AsSpan());
        Span<byte> buf = stackalloc byte[4];
        for (int i = start; i < _allTerms.Length; i++)
        {
            var term = _allTerms[i];
            if (!term.StartsWith(fieldPrefix, StringComparison.Ordinal))
                break;

            var bareTerm = term.AsSpan(fieldPrefix.Length);
            int state = automaton.Start;
            bool dead = false;
            for (int j = 0; j < bareTerm.Length; j++)
            {
                int len = System.Text.Encoding.UTF8.GetBytes(bareTerm.Slice(j, 1), buf);
                for (int k = 0; k < len; k++)
                {
                    state = automaton.Step(state, buf[k]);
                    if (!automaton.CanMatch(state)) { dead = true; break; }
                }
                if (dead) break;
            }

            if (!dead && automaton.IsAccept(state))
                results.Add((term, _allOffsets[i]));
        }
        return results;
    }

    private int LowerBound(ReadOnlySpan<char> key)
    {
        int lo = 0, hi = _allTerms.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_allTerms[mid].AsSpan().SequenceCompareTo(key) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}
