using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>Parses wildcard, fuzzy and alternative clauses inside quoted phrases.</summary>
public sealed class ComplexPhraseQueryParser : QueryParser
{
    /// <summary>Gets or sets whether phrase slots must match in query order.</summary>
    public bool InOrder { get; set; } = true;

    /// <summary>Initialises a complex-phrase query parser.</summary>
    public ComplexPhraseQueryParser(
        string defaultField,
        IAnalyser analyser,
        bool lenient = false)
        : base(defaultField, analyser, lenient)
    {
    }

    /// <inheritdoc/>
    protected override Query BuildPhraseQuery(string field, string phraseText, int slop)
    {
        if (phraseText.IndexOfAny(['(', ')', '*', '?', '~', '/']) < 0)
            return base.BuildPhraseQuery(field, phraseText, slop);

        var clauses = new List<SpanQuery>();
        int position = 0;
        while (position < phraseText.Length)
        {
            while (position < phraseText.Length && char.IsWhiteSpace(phraseText[position]))
                position++;
            if (position >= phraseText.Length)
                break;

            if (phraseText[position] == '(')
            {
                int close = FindClosingParenthesis(phraseText, position);
                clauses.Add(BuildAlternatives(
                    field,
                    phraseText.AsSpan(position + 1, close - position - 1)));
                position = close + 1;
                continue;
            }

            int start = position;
            while (position < phraseText.Length && !char.IsWhiteSpace(phraseText[position]))
                position++;
            clauses.Add(ParseSpanClause(field, phraseText[start..position]));
        }

        if (clauses.Count == 0)
            return base.BuildPhraseQuery(field, phraseText, slop);
        if (clauses.Count == 1)
            return clauses[0];
        return new SpanNearQuery(clauses.ToArray(), slop, InOrder);
    }

    private SpanQuery BuildAlternatives(string field, ReadOnlySpan<char> content)
    {
        var alternatives = new List<SpanQuery>();
        int position = 0;
        while (position < content.Length)
        {
            while (position < content.Length && char.IsWhiteSpace(content[position]))
                position++;
            int start = position;
            while (position < content.Length && !char.IsWhiteSpace(content[position]))
                position++;
            if (position == start)
                break;

            string token = content[start..position].ToString();
            if (token.Equals("OR", StringComparison.OrdinalIgnoreCase))
                continue;
            alternatives.Add(ParseSpanClause(field, token));
        }

        if (alternatives.Count == 0)
            throw new QueryParseException("A complex phrase alternative group cannot be empty.");
        return alternatives.Count == 1
            ? alternatives[0]
            : new SpanOrQuery(alternatives.ToArray());
    }

    private SpanQuery ParseSpanClause(string field, string text)
    {
        var parsed = new AnalysingQueryParser(field, Analyser).Parse(text);
        var span = ConvertToSpan(parsed);
        if (!string.Equals(span.Field, field, StringComparison.Ordinal))
            throw new QueryParseException("Every complex phrase clause must target the same field.");
        return span;
    }

    private static SpanQuery ConvertToSpan(Query query)
    {
        SpanQuery span = query switch
        {
            TermQuery term => new SpanTermQuery(term.Field, term.Term),
            PhraseQuery phrase => new SpanNearQuery(
                phrase.Terms
                    .Select(term => (SpanQuery)new SpanTermQuery(phrase.Field, term))
                    .ToArray(),
                phrase.Slop,
                inOrder: true),
            PrefixQuery or WildcardQuery or FuzzyQuery or RegexpQuery or TermRangeQuery
                => new SpanMultiTermQueryWrapper(query),
            ConstantScoreQuery constantScore => ConvertToSpan(constantScore.Inner),
            DisjunctionMaxQuery disjunction => new SpanOrQuery(
                disjunction.Disjuncts.Select(ConvertToSpan).ToArray()),
            BooleanQuery boolean => ConvertBooleanToSpan(boolean),
            SpanQuery existing => existing,
            _ => throw new QueryParseException(
                $"Query type '{query.GetType().Name}' cannot be used inside a complex phrase.")
        };
        span.Boost = query.Boost;
        return span;
    }

    private static SpanQuery ConvertBooleanToSpan(BooleanQuery query)
    {
        var included = new List<SpanQuery>();
        var excluded = new List<SpanQuery>();
        foreach (var clause in query.Clauses)
        {
            var span = ConvertToSpan(clause.Query);
            if (clause.Occur == Occur.MustNot)
                excluded.Add(span);
            else
                included.Add(span);
        }

        if (included.Count == 0)
            throw new QueryParseException("A complex phrase Boolean clause must include a positive term.");

        SpanQuery result = included.Count == 1
            ? included[0]
            : new SpanOrQuery(included.ToArray());
        if (excluded.Count == 0)
            return result;

        SpanQuery exclusion = excluded.Count == 1
            ? excluded[0]
            : new SpanOrQuery(excluded.ToArray());
        return new SpanNotQuery(result, exclusion);
    }

    private static int FindClosingParenthesis(string text, int open)
    {
        int depth = 1;
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return i;
        }
        throw new QueryParseException("Unmatched opening parenthesis in complex phrase.", open);
    }
}
