namespace Rowles.LeanCorpus.Search.Parsing;

using Rowles.LeanCorpus.Search.Queries;

/// <summary>Configures parser-time limits for untrusted query text.</summary>
/// <remarks>
/// The default options bound input size, token creation, query syntax and
/// phrase graph expansion. Existing <see cref="QueryParser"/> constructors
/// retain compatibility-oriented limits for trusted callers.
/// </remarks>
public sealed record QueryParserOptions
{
    /// <summary>Gets the hard maximum syntax depth supported by the recursive parser.</summary>
    public const int MaximumSupportedSyntaxDepth = 65;

    /// <summary>Gets the default bounded parser options.</summary>
    public static QueryParserOptions Default { get; } = new();

    /// <summary>Gets the maximum number of UTF-16 characters accepted in one query.</summary>
    public int MaxInputChars { get; init; } = 65_536;

    /// <summary>Gets the maximum number of syntax tokens accepted in one query.</summary>
    public int MaxTokens { get; init; } = 8_192;

    /// <summary>Gets the maximum nested query-expression depth, including the root expression.</summary>
    public int MaxSyntaxDepth { get; init; } = 64;

    /// <summary>Gets the maximum syntax nodes created while parsing and lowering one query.</summary>
    public int MaxSyntaxNodes { get; init; } = 16_384;

    /// <summary>Gets the maximum query clauses accepted after analysis and lowering.</summary>
    public int MaxQueryClauses { get; init; } = 4_096;

    /// <summary>Gets the maximum total analysed phrase tokens across one query.</summary>
    public int MaxPhraseTokens { get; init; } = 16_384;

    /// <summary>Gets the maximum token-graph edges read across one query.</summary>
    public int MaxGraphEdges { get; init; } = 8_192;

    /// <summary>Gets the maximum phrase-graph traversal steps across one query.</summary>
    public int MaxGraphTraversalSteps { get; init; } = 65_536;

    /// <summary>Gets the maximum complete phrase-graph paths emitted across one query.</summary>
    public int MaxGraphPaths { get; init; } = 256;

    /// <summary>Gets the maximum phrase terms retained across emitted graph paths.</summary>
    public int MaxCompiledPhraseTerms { get; init; } = 65_536;

    /// <summary>Gets the maximum Boolean clauses generated from phrase paths.</summary>
    public int MaxCompiledPhraseClauses { get; init; } = 512;

    /// <summary>Gets the maximum fuzzy edit distance accepted by query syntax, from zero through two.</summary>
    public int MaxFuzzyEdits { get; init; } = 2;

    /// <summary>Gets the maximum phrase slop accepted by query syntax, from zero through <see cref="PhraseQuery.MaximumSlop"/>.</summary>
    public int MaxPhraseSlop { get; init; } = PhraseQuery.MaximumSlop;

    /// <summary>Gets the maximum number of characters in a wildcard pattern.</summary>
    public int MaxWildcardPatternChars { get; init; } = 4_096;

    /// <summary>Gets the maximum number of characters in a regular-expression pattern.</summary>
    public int MaxRegexpPatternChars { get; init; } = 4_096;

    internal static QueryParserOptions Trusted { get; } = new()
    {
        MaxInputChars = int.MaxValue,
        MaxTokens = int.MaxValue,
        MaxSyntaxNodes = int.MaxValue,
        MaxQueryClauses = int.MaxValue,
        MaxWildcardPatternChars = int.MaxValue,
        MaxRegexpPatternChars = int.MaxValue
    };

    internal QueryParserOptions Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxInputChars, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTokens, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxSyntaxDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxSyntaxDepth, MaximumSupportedSyntaxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxSyntaxNodes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxQueryClauses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPhraseTokens, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxGraphEdges, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxGraphTraversalSteps, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxGraphPaths, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCompiledPhraseTerms, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCompiledPhraseClauses, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxFuzzyEdits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxFuzzyEdits, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxPhraseSlop);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxPhraseSlop, PhraseQuery.MaximumSlop);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWildcardPatternChars, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxRegexpPatternChars, 1);
        return this;
    }
}
