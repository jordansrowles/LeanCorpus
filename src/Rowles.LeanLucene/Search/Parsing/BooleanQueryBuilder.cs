namespace Rowles.LeanLucene.Search.Parsing;

/// <summary>
/// Fluent builder for <see cref="BooleanQuery"/> clauses.
/// Delegates to <see cref="BooleanQuery.Builder"/> internally.
/// </summary>
public sealed class BooleanQueryBuilder
{
    private readonly BooleanQuery.Builder _builder = new();

    /// <summary>Adds a MUST clause.</summary>
    public BooleanQueryBuilder Must(Query query)
    {
        _builder.Add(query, Occur.Must);
        return this;
    }

    /// <summary>Adds a SHOULD clause.</summary>
    public BooleanQueryBuilder Should(Query query)
    {
        _builder.Add(query, Occur.Should);
        return this;
    }

    /// <summary>Adds a MUST_NOT clause.</summary>
    public BooleanQueryBuilder MustNot(Query query)
    {
        _builder.Add(query, Occur.MustNot);
        return this;
    }

    internal BooleanQuery Build() => _builder.Build();
}
