namespace Rowles.LeanCorpus.Search;

/// <summary>Visits a query tree without changing it.</summary>
public abstract class QueryVisitor
{
    /// <summary>Returns the visitor to use for a child query.</summary>
    public virtual QueryVisitor GetSubVisitor(Occur occur, Query parent) => this;

    /// <summary>Visits a query that has no query children.</summary>
    public virtual void VisitLeaf(Query query)
    {
    }

    /// <summary>Visits one exact term consumed by a query.</summary>
    public virtual void ConsumeTerm(Query query, string field, string term)
        => VisitLeaf(query);

    /// <summary>Visits exact terms consumed by a query.</summary>
    public virtual void ConsumeTerms(Query query, string field, IReadOnlyList<string> terms)
        => VisitLeaf(query);
}
