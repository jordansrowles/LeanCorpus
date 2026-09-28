namespace Rowles.LeanCorpus.Search.Parsing;

using Rowles.LeanCorpus.Analysis.Analysers;

internal abstract record QuerySyntax;
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

internal sealed class QueryParseLimitException(string message) : Exception(message);
