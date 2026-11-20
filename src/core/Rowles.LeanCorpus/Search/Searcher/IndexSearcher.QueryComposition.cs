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
        RewriteQuery(query).Visit(new CompositionVisitor(this, new CompositionState(), nested: false, path: null));
    }

    private static InvalidOperationException CyclicQuery(Query query) => new(
        $"The query tree contains a cycle: {query.GetType().Name} is reachable from its own clauses, " +
        "filter or child query. Build query trees without self-references.");

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

    /// <summary>The composite queries enclosing the current visit, innermost first.</summary>
    private sealed record ActivePath(Query Query, ActivePath? Parent)
    {
        public bool Contains(Query query)
        {
            for (var node = this; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node.Query, query))
                    return true;
            }
            return false;
        }
    }

    private sealed class CompositionVisitor(IndexSearcher searcher, CompositionState state, bool nested, ActivePath? path)
        : QueryVisitor
    {
        public override QueryVisitor GetSubVisitor(Occur occur, Query parent)
        {
            // Every composite descends through this call, so a composite already on the active path
            // means a mutable query (or a custom one) contains itself. Every other walker, and query
            // hashing, would recurse without bound, so reject it here, at admission.
            if (path?.Contains(parent) == true)
                throw CyclicQuery(parent);
            var childPath = new ActivePath(parent, path);

            if (parent is RrfQuery)
            {
                // RRF children are independent top-level searches, so they may themselves be
                // whole-index queries. RRF itself must be the root.
                if (nested)
                    throw UnsupportedComposition(parent);
                return new CompositionVisitor(searcher, state, nested: false, childPath);
            }

            return new CompositionVisitor(searcher, state, nested: true, childPath);
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
            weight.Approximation.Visit(nested ? this : new CompositionVisitor(searcher, state, nested: true, path));
        }
    }
}
