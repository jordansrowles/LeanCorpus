namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>
/// Compile-time-safe fluent builder for constructing query trees.
/// </summary>
public static class QueryBuilder
{
    /// <summary>Creates a <see cref="TermQuery"/>.</summary>
    public static TermQuery Term(string field, string term) => new(field, term);

    /// <summary>Creates a <see cref="PhraseQuery"/>.</summary>
    public static PhraseQuery Phrase(string field, params string[] terms) => new(field, terms);

    /// <summary>Creates a <see cref="PhraseQuery"/> with the given slop.</summary>
    public static PhraseQuery Phrase(string field, int slop, params string[] terms) => new(field, slop, terms);

    /// <summary>Creates a <see cref="PhraseQuery"/> with explicit term positions.</summary>
    public static PhraseQuery Phrase(string field, string[] terms, int[] positions, int slop = 0)
        => new(field, terms, positions, slop);

    /// <summary>Creates a <see cref="PrefixQuery"/>.</summary>
    public static PrefixQuery Prefix(string field, string prefix) => new(field, prefix);

    /// <summary>Creates a <see cref="FuzzyQuery"/>.</summary>
    public static FuzzyQuery Fuzzy(string field, string term, int maxEdits = 2) => new(field, term, maxEdits);

    /// <summary>Creates a <see cref="WildcardQuery"/>.</summary>
    public static WildcardQuery Wildcard(string field, string pattern) => new(field, pattern);

    /// <summary>Creates a <see cref="SynonymQuery"/>.</summary>
    public static SynonymQuery Synonym(string field, params string[] terms) => new(field, terms);

    /// <summary>Creates a <see cref="RangeQuery"/>.</summary>
    public static RangeQuery Range(
        string field,
        double min,
        double max,
        bool includeMin = true,
        bool includeMax = true) => new(field, min, max, includeMin, includeMax);

    /// <summary>Creates an <see cref="Int64RangeQuery"/>.</summary>
    public static Int64RangeQuery Int64Range(
        string field,
        long min,
        long max,
        bool includeMin = true,
        bool includeMax = true) => new(field, min, max, includeMin, includeMax);

    /// <summary>Creates an <see cref="Int32RangeQuery"/>.</summary>
    public static Int32RangeQuery Int32Range(
        string field,
        int min,
        int max,
        bool includeMin = true,
        bool includeMax = true) => new(field, min, max, includeMin, includeMax);

    /// <summary>Creates a <see cref="SingleRangeQuery"/>.</summary>
    public static SingleRangeQuery SingleRange(
        string field,
        float min,
        float max,
        bool includeMin = true,
        bool includeMax = true) => new(field, min, max, includeMin, includeMax);

    /// <summary>Creates an <see cref="Int32PointInSetQuery"/>.</summary>
    public static Int32PointInSetQuery Int32PointInSet(string field, params int[] points)
        => new(field, points);

    /// <summary>Creates a <see cref="SinglePointInSetQuery"/>.</summary>
    public static SinglePointInSetQuery SinglePointInSet(string field, params float[] points)
        => new(field, points);

    /// <summary>Creates a <see cref="BinaryRangeQuery"/>.</summary>
    public static BinaryRangeQuery BinaryRange(
        string field,
        ReadOnlyMemory<byte>? lower,
        ReadOnlyMemory<byte>? upper,
        bool includeLower = true,
        bool includeUpper = true)
        => new(field, lower, upper, includeLower, includeUpper);

    /// <summary>Creates a <see cref="BinaryPointInSetQuery"/>.</summary>
    public static BinaryPointInSetQuery BinaryPointInSet(string field, params byte[][] points)
        => new(field, points);

    /// <summary>Creates an <see cref="InetAddressRangeQuery"/>.</summary>
    public static InetAddressRangeQuery InetAddressRange(
        string field,
        System.Net.IPAddress lower,
        System.Net.IPAddress upper,
        bool includeLower = true,
        bool includeUpper = true)
        => new(field, lower, upper, includeLower, includeUpper);

    /// <summary>Creates an <see cref="InetAddressPointInSetQuery"/>.</summary>
    public static InetAddressPointInSetQuery InetAddressPointInSet(
        string field,
        params System.Net.IPAddress[] addresses)
        => new(field, addresses);

    /// <summary>Creates a <see cref="TermRangeQuery"/>.</summary>
    public static TermRangeQuery TermRange(string field, string? lower, string? upper,
        bool includeLower = true, bool includeUpper = true) => new(field, lower, upper, includeLower, includeUpper);

    /// <summary>Creates a <see cref="RegexpQuery"/>.</summary>
    public static RegexpQuery Regexp(string field, string pattern) => new(field, pattern);

    /// <summary>Creates a <see cref="ConstantScoreQuery"/>.</summary>
    public static ConstantScoreQuery ConstantScore(Query inner, float score = 1.0f) => new(inner, score);

    /// <summary>Creates a <see cref="VectorQuery"/>.</summary>
    public static VectorQuery Vector(string field, float[] queryVector, int topK = 10) => new(field, queryVector, topK);

    /// <summary>Starts building a <see cref="BooleanQuery"/> using a fluent callback.</summary>
    public static BooleanQuery Bool(Action<BooleanQueryBuilder> configure)
    {
        var builder = new BooleanQueryBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>Starts building a <see cref="DisjunctionMaxQuery"/>.</summary>
    public static DisjunctionMaxQuery DisMax(float tieBreakerMultiplier = 0f, params Query[] disjuncts)
    {
        var q = new DisjunctionMaxQuery(tieBreakerMultiplier);
        foreach (var d in disjuncts) q.Add(d);
        return q;
    }
}
