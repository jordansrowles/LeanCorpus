namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Flattens a <see cref="Query"/> tree into a per-field map of
/// <c>(term → weighted phrase positions)</c> for use by
/// <see cref="TermVectorHighlighter"/>.
/// </summary>
/// <remarks>
/// Construct once per query then query per field. Phrase slop is
/// ignored during flattening (it is validated at highlight time);
/// only the relative positions are recorded.
/// </remarks>
public sealed class FieldQuery
{
    private readonly Dictionary<string, Dictionary<string, PhraseInfo>> _fieldMap =
        new(StringComparer.Ordinal);

    /// <summary>Initialises a new <see cref="FieldQuery"/> by flattening the supplied query tree.</summary>
    /// <param name="query">The query whose terms and phrase positions should be extracted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is <see langword="null"/>.</exception>
    public FieldQuery(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Collect(query, 1.0f);
    }

    /// <summary>All distinct terms that appear in any clause for the given field.</summary>
    public IReadOnlySet<string> GetTerms(string field)
    {
        if (_fieldMap.TryGetValue(field, out var termMap))
            return new HashSet<string>(termMap.Keys, StringComparer.Ordinal);
        return ReadOnlySet.Empty;
    }

    /// <summary>Whether this field has any query clauses.</summary>
    public bool HasField(string field) => _fieldMap.ContainsKey(field);
    /// <summary>
    /// For a phrase clause, the relative positions the term must occupy.
    /// Returns <see langword="null"/> if the term appears only in non-phrase
    /// clauses (any position matches).
    /// </summary>
    public IReadOnlySet<int>? GetPhrasePositions(string field, string term)
    {
        if (_fieldMap.TryGetValue(field, out var termMap) &&
            termMap.TryGetValue(term, out var info))
        {
            return info.Positions;
        }
        return null;
    }

    /// <summary>All fields referenced by the query.</summary>
    public IEnumerable<string> Fields => _fieldMap.Keys;

    // -- internal helpers --

    private void Collect(Query query, float weight)
    {
        switch (query)
        {
            case TermQuery tq:
                AddTerm(tq.Field, tq.Term, positions: null, weight);
                break;
            case PhraseQuery pq:
                CollectPhrase(pq.Field, pq.Terms, weight);
                break;
            case MultiPhraseQuery mpq:
                CollectMultiPhrase(mpq, weight);
                break;
            case BooleanQuery bq:
                CollectBoolean(bq, weight);
                break;
            case DisjunctionMaxQuery dmq:
                foreach (var d in dmq.Disjuncts)
                    Collect(d, weight);
                break;
            case ConstantScoreQuery csq:
                Collect(csq.Inner, weight);
                break;
            case PrefixQuery preQ:
                AddTerm(preQ.Field, preQ.Prefix, positions: null, weight);
                break;
            case FuzzyQuery fzQ:
                AddTerm(fzQ.Field, fzQ.Term, positions: null, weight);
                break;
            case WildcardQuery wcQ:
                AddTerm(wcQ.Field, wcQ.Pattern, positions: null, weight);
                break;
            case TermInSetQuery tisQ:
                foreach (var t in tisQ.Terms)
                    AddTerm(tisQ.Field, t, positions: null, weight);
                break;
        }
    }

    private void CollectPhrase(string field, string[] terms, float weight)
    {
        for (int i = 0; i < terms.Length; i++)
            AddTerm(field, terms[i], new HashSet<int> { i }, weight);
    }

    private void CollectMultiPhrase(MultiPhraseQuery query, float weight)
    {
        var positions = query.Positions;
        var groups = query.TermGroups;
        for (int i = 0; i < groups.Count; i++)
        {
            int pos = positions[i];
            foreach (var term in groups[i])
                AddTerm(query.Field, term, new HashSet<int> { pos }, weight);
        }
    }

    private void CollectBoolean(BooleanQuery query, float weight)
    {
        foreach (var clause in query.Clauses)
        {
            if (clause.Occur == Occur.MustNot)
                continue;
            Collect(clause.Query, weight);
        }
    }

    private void AddTerm(string field, string term, HashSet<int>? positions, float weight)
    {
        if (!_fieldMap.TryGetValue(field, out var termMap))
        {
            termMap = new Dictionary<string, PhraseInfo>(StringComparer.Ordinal);
            _fieldMap[field] = termMap;
        }

        if (termMap.TryGetValue(term, out var existing))
        {
            // Merge positions: if either is null (any position), result is null.
            HashSet<int>? mergedPositions;
            if (existing.Positions is null || positions is null)
            {
                mergedPositions = null;
            }
            else
            {
                mergedPositions = new HashSet<int>(existing.Positions);
                mergedPositions.UnionWith(positions);
            }
            termMap[term] = new PhraseInfo(
                mergedPositions,
                existing.Weight + weight);
        }
        else
        {
            termMap[term] = new PhraseInfo(positions, weight);
        }
    }

    private sealed class PhraseInfo
    {
        public readonly HashSet<int>? Positions;
        public readonly float Weight;

        public PhraseInfo(HashSet<int>? positions, float weight)
        {
            Positions = positions;
            Weight = weight;
        }
    }
}

/// <summary>Singleton empty <see cref="IReadOnlySet{T}"/> for zero-allocation returns.</summary>
internal static class ReadOnlySet
{
    public static readonly IReadOnlySet<string> Empty = new HashSet<string>();
}
