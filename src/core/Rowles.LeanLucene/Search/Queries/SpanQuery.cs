namespace Rowles.LeanLucene.Search.Queries;

/// <summary>Base type for position-aware queries that produce spans.</summary>
public abstract class SpanQuery : Query
{
    /// <summary>The field these spans are evaluated against. All sub-queries of a span query must agree on this field.</summary>
    public abstract override string Field { get; }
}
