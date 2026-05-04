namespace Rowles.LeanLucene.Search.Parsing;

/// <summary>
/// Extension methods for fluent query composition on <see cref="Query"/>.
/// </summary>
public static class QueryExtensions
{
    /// <summary>Sets the boost on the query and returns it for chaining.</summary>
    public static T WithBoost<T>(this T query, float boost) where T : Query
    {
        query.Boost = boost;
        return query;
    }

    /// <summary>Combines two queries with a MUST boolean clause.</summary>
    public static BooleanQuery And(this Query left, Query right)
        => new BooleanQuery.Builder().Add(left, Occur.Must).Add(right, Occur.Must).Build();

    /// <summary>Combines two queries with a SHOULD boolean clause.</summary>
    public static BooleanQuery Or(this Query left, Query right)
        => new BooleanQuery.Builder().Add(left, Occur.Should).Add(right, Occur.Should).Build();

    /// <summary>Excludes results matching the given query.</summary>
    public static BooleanQuery Not(this Query left, Query excluded)
        => new BooleanQuery.Builder().Add(left, Occur.Must).Add(excluded, Occur.MustNot).Build();
}
