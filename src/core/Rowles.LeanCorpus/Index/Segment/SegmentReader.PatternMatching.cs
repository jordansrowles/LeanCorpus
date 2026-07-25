using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Codecs.Fst;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Pattern matching and term scanning methods for SegmentReaderState.
/// </summary>
internal sealed partial class SegmentReaderState
{
    /// <summary>Intersects the term dictionary with an automaton, returning matching terms.</summary>
    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton)
    {
        return DictionaryReader.IntersectAutomaton(fieldPrefix, automaton);
    }

    /// <summary>Returns all terms matching a qualified prefix.</summary>
    public List<(string Term, long Offset)> GetTermsWithPrefix(string qualifiedPrefix)
    {
        return DictionaryReader.GetTermsWithPrefix(qualifiedPrefix.AsSpan());
    }

    /// <summary>Returns postings offsets for terms matching a qualified prefix.</summary>
    internal List<long> GetTermOffsetsWithPrefix(string qualifiedPrefix)
    {
        return DictionaryReader.GetTermOffsetsWithPrefix(qualifiedPrefix.AsSpan());
    }

    /// <summary>Returns all terms for a field matching a wildcard pattern.</summary>
    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        return DictionaryReader.GetTermsMatching(fieldPrefix, pattern);
    }

    /// <summary>Returns postings offsets for terms matching a wildcard pattern.</summary>
    internal List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        return DictionaryReader.GetTermOffsetsMatching(fieldPrefix, pattern);
    }

    /// <summary>
    /// Returns postings offsets for terms matching a wildcard pattern where the FST
    /// traversal is pre-narrowed by a known leading literal prefix (≥2 characters).
    /// Allocation-light: no per-term string materialisation.
    /// </summary>
    internal List<long> GetTermOffsetsMatchingWithPrefix(
        string field, ReadOnlySpan<char> leadingPrefix, ReadOnlySpan<char> fullPattern)
    {
        return DictionaryReader.GetTermOffsetsMatchingWithPrefix(field, leadingPrefix, fullPattern);
    }

    /// <summary>
    /// Returns qualified terms and offsets matching a wildcard pattern with prefix
    /// narrowing. Callers needing term strings (e.g. cross-segment DF lookup) should
    /// use this; callers needing only offsets should use
    /// <see cref="GetTermOffsetsMatchingWithPrefix"/>.
    /// </summary>
    internal List<(string Term, long Offset)> GetTermsMatchingWithPrefix(
        string field, ReadOnlySpan<char> leadingPrefix, ReadOnlySpan<char> fullPattern)
    {
        return DictionaryReader.GetTermsMatchingWithPrefix(field, leadingPrefix, fullPattern);
    }

    /// <summary>Returns all terms for a given field.</summary>
    public List<(string Term, long Offset)> GetAllTermsForField(string fieldPrefix)
    {
        return DictionaryReader.GetAllTermsForField(fieldPrefix);
    }

    /// <summary>Returns terms within Levenshtein distance of queryTerm, with edit distances.</summary>
    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions = 64)
    {
        return DictionaryReader.GetFuzzyMatches(fieldPrefix, queryTerm, maxEdits, maxExpansions);
    }

    /// <summary>Returns terms in lexicographic range [lower, upper] for a field.</summary>
    public List<(string Term, long Offset)> GetTermsInRange(string fieldPrefix,
        string? lower, string? upper, bool includeLower = true, bool includeUpper = true)
    {
        return DictionaryReader.GetTermsInRange(fieldPrefix, lower, upper, includeLower, includeUpper);
    }

    /// <summary>Returns terms for a field matching the compiled regex.</summary>
    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex)
    {
        return DictionaryReader.GetTermsMatchingRegex(fieldPrefix, regex);
    }

    /// <summary>Returns postings offsets for terms whose bare text contains the supplied literal.</summary>
    internal List<long> GetTermOffsetsContaining(string fieldPrefix, ReadOnlySpan<char> literal)
    {
        return DictionaryReader.GetTermOffsetsContaining(fieldPrefix, literal);
    }
}
