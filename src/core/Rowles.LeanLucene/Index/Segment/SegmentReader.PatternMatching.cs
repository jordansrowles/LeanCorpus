using System.Text.RegularExpressions;
using Rowles.LeanLucene.Codecs.Fst;

namespace Rowles.LeanLucene.Index.Segment;

/// <summary>
/// Pattern matching and term scanning methods for SegmentReader.
/// </summary>
public sealed partial class SegmentReader
{
    /// <summary>Intersects the term dictionary with an automaton, returning matching terms.</summary>
    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton)
    {
        return _dicReader.IntersectAutomaton(fieldPrefix, automaton);
    }

    /// <summary>Returns all terms matching a qualified prefix.</summary>
    public List<(string Term, long Offset)> GetTermsWithPrefix(string qualifiedPrefix)
    {
        return _dicReader.GetTermsWithPrefix(qualifiedPrefix.AsSpan());
    }

    /// <summary>Returns all terms for a field matching a wildcard pattern.</summary>
    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        return _dicReader.GetTermsMatching(fieldPrefix, pattern);
    }

    /// <summary>Returns postings offsets for terms matching a wildcard pattern.</summary>
    internal List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
    {
        return _dicReader.GetTermOffsetsMatching(fieldPrefix, pattern);
    }

    /// <summary>Returns all terms for a given field.</summary>
    public List<(string Term, long Offset)> GetAllTermsForField(string fieldPrefix)
    {
        return _dicReader.GetAllTermsForField(fieldPrefix);
    }

    /// <summary>Returns terms within Levenshtein distance of queryTerm, with edit distances.</summary>
    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions = 64)
    {
        return _dicReader.GetFuzzyMatches(fieldPrefix, queryTerm, maxEdits, maxExpansions);
    }

    /// <summary>Returns terms in lexicographic range [lower, upper] for a field.</summary>
    public List<(string Term, long Offset)> GetTermsInRange(string fieldPrefix,
        string? lower, string? upper, bool includeLower = true, bool includeUpper = true)
    {
        return _dicReader.GetTermsInRange(fieldPrefix, lower, upper, includeLower, includeUpper);
    }

    /// <summary>Returns terms for a field matching the compiled regex.</summary>
    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex)
    {
        return _dicReader.GetTermsMatchingRegex(fieldPrefix, regex);
    }
}
