using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Search.Searcher;

public sealed partial class IndexSearcher
{
    /// <summary>
    /// Rejects a query tree that contains a clause which cannot execute where it is placed
    /// (ADR049). This runs at operation admission, before the query cache, empty-index returns
    /// and any clause short-circuit, so an unsupported clause never becomes an empty result.
    /// </summary>
    /// <remarks>
    /// <see cref="MoreLikeThisQuery"/> and <see cref="RrfQuery"/> coordinate whole-index searches and
    /// are valid only as the root of a search, or as a direct child of a root RRF query. A custom
    /// query must rewrite to built-in queries or supply a weight (ADR013).
    /// </remarks>
    internal void ValidateQueryComposition(Query query)
    {
        // The most common shapes contain only built-in leaves; skip the walk on the hot paths.
        if (query is TermQuery || (query is BooleanQuery bq && IsAllTermQueryBoolean(bq)))
            return;
        RewriteQuery(query).Visit(new CompositionVisitor(this, new CompositionState(), nested: false));
    }

    private static NotSupportedException UnsupportedComposition(Query query) => query switch
    {
        MoreLikeThisQuery or RrfQuery => new NotSupportedException(
            $"{query.GetType().Name} coordinates a whole-index search and cannot be used as a clause, " +
            "filter or child query. Run it as a top-level query instead."),
        _ => new NotSupportedException(
            $"{query.GetType().Name} cannot be executed: a custom query must rewrite to built-in queries " +
            "or supply a weight."),
    };

    private sealed class CompositionState
    {
        public HashSet<Query> VisitedRoots { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<Query> VisitedNested { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private sealed class CompositionVisitor(IndexSearcher searcher, CompositionState state, bool nested) : QueryVisitor
    {
        public override QueryVisitor GetSubVisitor(Occur occur, Query parent)
        {
            if (parent is RrfQuery)
            {
                // RRF children are independent top-level searches, so they may themselves be
                // whole-index queries. RRF itself must be the root.
                if (nested)
                    throw UnsupportedComposition(parent);
                return new CompositionVisitor(searcher, state, nested: false);
            }

            return nested ? this : new CompositionVisitor(searcher, state, nested: true);
        }

        public override void VisitLeaf(Query query)
        {
            var visited = nested ? state.VisitedNested : state.VisitedRoots;
            if (!visited.Add(query))
                return;

            if (query is MoreLikeThisQuery)
            {
                if (nested)
                    throw UnsupportedComposition(query);
                return;
            }

            var rewritten = RewriteQuery(query);
            if (!ReferenceEquals(rewritten, query))
            {
                rewritten.Visit(this);
                return;
            }

            if (query.GetType().Assembly == typeof(Query).Assembly)
                return;

            if (query.CreateWeight(searcher) is not { } weight)
                throw UnsupportedComposition(query);
            weight.Approximation.Visit(nested ? this : new CompositionVisitor(searcher, state, nested: true));
        }
    }
}
