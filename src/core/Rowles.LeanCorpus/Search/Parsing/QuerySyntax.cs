namespace Rowles.LeanCorpus.Search.Parsing;

using Rowles.LeanCorpus.Analysis.Analysers;

internal readonly record struct QuerySourceSpan(int Start, int End)
{
    public int Length => Math.Max(0, End - Start);

    public static QuerySourceSpan Cover(QuerySourceSpan first, QuerySourceSpan last)
    {
        if (first.Length == 0)
            return last;
        if (last.Length == 0)
            return first;
        return new QuerySourceSpan(Math.Min(first.Start, last.Start), Math.Max(first.End, last.End));
    }
}

internal abstract record QuerySyntax
{
    public QuerySourceSpan SourceSpan { get; init; }

    internal QuerySyntax WithSourceSpan(QuerySourceSpan sourceSpan) =>
        this with { SourceSpan = sourceSpan };
}
internal enum QueryClauseState
{
    SyntaxMissing,
    Parsed,
    AnalysedEmpty,
    RecoveredError
}

internal sealed record EmptyQuerySyntax : QuerySyntax;
internal sealed record AnalysedEmptyQuerySyntax : QuerySyntax;
internal sealed record RecoveredQuerySyntax : QuerySyntax;
internal sealed record GroupQuerySyntax(QuerySyntax Inner) : QuerySyntax;
internal sealed record UnanalysedTermQuerySyntax(string Field, string Term) : QuerySyntax;
internal sealed record UnanalysedFuzzyQuerySyntax(string Field, string Term, int MaxEdits, int ModifierOffset) : QuerySyntax;
internal sealed record TermQuerySyntax(string Field, string Term) : QuerySyntax;
internal sealed record FuzzyQuerySyntax(string Field, string Term, int MaxEdits, int ModifierOffset) : QuerySyntax;
internal sealed record MultiTermQuerySyntax(string Field, string Pattern, string Term) : QuerySyntax;
internal sealed record PhraseQuerySyntax(
    string Field,
    string Text,
    int Slop,
    PhraseQuerySyntaxExpansion? Expansion = null,
    string? RawText = null) : QuerySyntax;
internal sealed record PhraseGraphPlan(
    int StartPosition,
    int EndPosition,
    int[] StartPositions,
    IReadOnlyDictionary<int, Analysis.TokenGraph.TokenEdge[]> EdgesByStart);
internal sealed record PhraseQuerySyntaxExpansion(
    string[]? DirectTerms,
    PhraseGraphPlan? Graph,
    int PathCount,
    int CompiledTermCount)
{
    public bool IsNoClause => DirectTerms is null && Graph is null;
}
internal sealed record RegexpQuerySyntax(string Field, string Pattern) : QuerySyntax;
internal sealed record TermRangeQuerySyntax(string Field, string? LowerTerm, string? UpperTerm, bool IncludeLower, bool IncludeUpper) : QuerySyntax;
internal sealed record FieldExistsQuerySyntax(string Field) : QuerySyntax;
internal sealed record BoostQuerySyntax(QuerySyntax Inner, float Boost, bool ConstantScore) : QuerySyntax;
internal sealed record BooleanQuerySyntax(IReadOnlyList<QuerySyntaxClause> Clauses) : QuerySyntax;
internal sealed record DisjunctionMaxQuerySyntax(IReadOnlyList<QuerySyntax> Clauses) : QuerySyntax;
internal readonly record struct QuerySyntaxClause(
    QuerySyntax Query,
    Occur Occur,
    QueryClauseState State = QueryClauseState.Parsed);
internal sealed record QueryFieldCompilationContext(
    string Field,
    IAnalyser QueryAnalyser,
    Func<string, string>? MultiTermNormaliser);

internal sealed class QueryParseLimitException(string message, int? offset = null) : Exception(message)
{
    public int? Offset { get; } = offset;
}
