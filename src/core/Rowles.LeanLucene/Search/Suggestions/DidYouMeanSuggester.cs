using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Rowles.LeanLucene.Search.Suggestions;

/// <summary>
/// "Did you mean?" spelling correction. Delegates to <see cref="SpellIndex"/>
/// which uses a pre-built character-trigram inverted index for fast candidate
/// filtering, then scores by docFreq / (1 + editDistance).
/// The <see cref="SpellIndex"/> is cached per searcher/field pair and released
/// when the <see cref="Searcher.IndexSearcher"/> is collected.
/// </summary>
public static class DidYouMeanSuggester
{
    private static readonly ConditionalWeakTable<Searcher.IndexSearcher, ConcurrentDictionary<string, SpellIndex>> _cache = new();

    /// <summary>
    /// Suggests corrections for <paramref name="queryTerm"/> in the given <paramref name="field"/>.
    /// The underlying <see cref="SpellIndex"/> is built once per searcher/field pair and cached
    /// for subsequent calls. For explicit control over the index lifecycle, use
    /// <see cref="Suggest(SpellIndex, string, int, int)"/> with a manually built index.
    /// </summary>
    public static List<Suggestion> Suggest(
        Searcher.IndexSearcher searcher,
        string field,
        string queryTerm,
        int maxEdits = 2,
        int topN = 5)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentException.ThrowIfNullOrEmpty(queryTerm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topN);

        var fieldCache = _cache.GetOrCreateValue(searcher);
        var index = fieldCache.GetOrAdd(field, f => SpellIndex.Build(searcher, f));
        return index.Suggest(queryTerm, maxEdits, topN);
    }

    /// <summary>
    /// Suggests corrections using a pre-built <see cref="SpellIndex"/>.
    /// Use this overload when issuing multiple suggestions against the same index
    /// to avoid rebuilding the trigram index on every call.
    /// </summary>
    public static List<Suggestion> Suggest(
        SpellIndex index,
        string queryTerm,
        int maxEdits = 2,
        int topN = 5)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrEmpty(queryTerm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topN);

        return index.Suggest(queryTerm, maxEdits, topN);
    }
}
