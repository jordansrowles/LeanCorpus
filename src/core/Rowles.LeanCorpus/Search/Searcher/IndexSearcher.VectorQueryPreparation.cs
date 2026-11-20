using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// The candidates one <see cref="VectorQuery"/> instance selected across a whole reader snapshot
/// (ADR049). Immutable, and held only for one search operation.
/// </summary>
internal sealed class PreparedVectorQuery
{
    private readonly ScoreDoc[] _byDocId;

    /// <summary>Creates a prepared set from selected global documents and their final vector scores.</summary>
    public PreparedVectorQuery(ReadOnlySpan<ScoreDoc> members)
    {
        _byDocId = members.ToArray();
        Array.Sort(_byDocId, static (left, right) => left.DocId.CompareTo(right.DocId));
    }

    /// <summary>Gets the number of selected documents.</summary>
    public int Count => _byDocId.Length;

    /// <summary>Gets the selected documents in ascending document-ID order.</summary>
    public ReadOnlySpan<ScoreDoc> Members => _byDocId;

    /// <summary>Returns the selected documents whose IDs fall in [<paramref name="start"/>, <paramref name="end"/>).</summary>
    public ReadOnlySpan<ScoreDoc> InRange(int start, int end)
    {
        int first = LowerBound(start);
        int last = LowerBound(end);
        return _byDocId.AsSpan(first, last - first);
    }

    /// <summary>Gets a selected document's score.</summary>
    public bool TryGetScore(int docId, out float score)
    {
        int index = LowerBound(docId);
        if (index < _byDocId.Length && _byDocId[index].DocId == docId)
        {
            score = _byDocId[index].Score;
            return true;
        }

        score = 0f;
        return false;
    }

    private int LowerBound(int docId)
    {
        int low = 0;
        int high = _byDocId.Length;
        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (_byDocId[mid].DocId < docId)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }
}

public sealed partial class IndexSearcher
{
    // Vector queries whose preparation is running on this thread. A filter that refers back to its
    // own vector query would otherwise recurse without bound.
    [ThreadStatic] private static HashSet<VectorQuery>? t_preparingVectorQueries;

    // Set while Explain prepares a selected set, so diagnostic preparation records no search metrics.
    [ThreadStatic] private static bool t_suppressVectorSearchMetrics;

    // Prepared sets supplied by a MultiReader so every component observes one global selection.
    private readonly AsyncLocal<IReadOnlyDictionary<VectorQuery, PreparedVectorQuery>?> _seededVectorPlans = new();

    /// <summary>
    /// Returns this operation's selected set for <paramref name="query"/>, preparing it on first use.
    /// The set is shared by every segment, clause and route of the current search, and is never
    /// reused by a later search.
    /// </summary>
    private PreparedVectorQuery GetPreparedVectorQuery(VectorQuery query, Action? checkResources = null)
    {
        if (t_preparingVectorQueries?.Contains(query) == true)
            throw RecursiveVectorPreparation(query);

        var context = _fuzzyQueryExecutionContext.Value;
        checkResources ??= context?.VectorResourceCheck;
        return context is null
            ? BuildPreparedVectorQuery(query, checkResources)
            : context.GetVectorPlan(query, () => BuildPreparedVectorQuery(query, checkResources));
    }

    private PreparedVectorQuery BuildPreparedVectorQuery(VectorQuery query, Action? checkResources)
    {
        var preparing = t_preparingVectorQueries ??= new HashSet<VectorQuery>(ReferenceEqualityComparer.Instance);
        if (!preparing.Add(query))
            throw RecursiveVectorPreparation(query);

        try
        {
            // TopK can exceed the snapshot; the collector allocates its capacity eagerly.
            int capacity = Math.Min(query.TopK, Math.Max(_totalDocCount, 1));
            var collector = new TopNCollector(capacity);
            var filterDocFreqs = new Dictionary<(string Field, string Term), int>();
            foreach (var reader in _readers)
            {
                checkResources?.Invoke();
                // Pin the segment for the whole scan or traversal (ADR011), as ExecuteQuery does.
                using var segmentLease = reader.AcquireQueryLease();
                CollectVectorCandidates(query, reader, filterDocFreqs, ref collector);
            }

            return new PreparedVectorQuery(collector.ToTopDocs().ScoreDocs);
        }
        finally
        {
            preparing.Remove(query);
        }
    }

    private static InvalidOperationException RecursiveVectorPreparation(VectorQuery query)
        => new($"VectorQuery on field '{query.Field}' cannot be prepared: its filter refers back to the same query.");

    /// <summary>Prepares every vector query reachable from <paramref name="query"/> before segment execution.</summary>
    private void PrepareVectorQueries(Query query, Action? checkResources)
    {
        var context = _fuzzyQueryExecutionContext.Value;
        if (context is null)
            return;

        var previous = context.VectorResourceCheck;
        context.VectorResourceCheck = checkResources;
        try
        {
            query.Visit(new VectorQueryVisitor(vector => GetPreparedVectorQuery(vector, checkResources)));
        }
        finally
        {
            context.VectorResourceCheck = previous;
        }
    }

    /// <summary>
    /// Returns the vector queries reachable from <paramref name="query"/>, each after every vector
    /// query reachable from its own filter, so nested selections are prepared first.
    /// </summary>
    internal static List<VectorQuery> CollectVectorQueries(Query query)
    {
        var found = new List<VectorQuery>();
        RewriteQuery(query).Visit(new VectorQueryVisitor(found.Add));
        return found;
    }

    /// <summary>Emits the selected members that belong to one segment.</summary>
    private void ExecuteVectorQuery(
        VectorQuery query,
        SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs,
        ref TopNCollector collector)
    {
        var prepared = GetPreparedVectorQuery(query);
        int docBase = reader.DocBase;
        foreach (var member in prepared.InRange(docBase, docBase + reader.MaxDoc))
            collector.Collect(member.DocId, member.Score);
    }

    /// <summary>
    /// Selects this snapshot's top candidates for a <c>MultiReader</c>, which merges them across
    /// components before seeding each component with its share of the global selection.
    /// </summary>
    internal PreparedVectorQuery SelectVectorCandidates(VectorQuery query)
    {
        // The MultiReader validated the whole query before seeding.
        using var scope = PushFuzzyQueryExecutionContext();
        return GetPreparedVectorQuery(query);
    }

    /// <summary>
    /// Makes later searches in the current asynchronous flow use the supplied selected sets.
    /// Disposal restores the previous seed.
    /// </summary>
    internal IDisposable UsePreparedVectorPlans(IReadOnlyDictionary<VectorQuery, PreparedVectorQuery> plans)
    {
        var previous = _seededVectorPlans.Value;
        _seededVectorPlans.Value = plans;
        return new SeedScope(this, previous);
    }

    private sealed class SeedScope(
        IndexSearcher searcher,
        IReadOnlyDictionary<VectorQuery, PreparedVectorQuery>? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                searcher._seededVectorPlans.Value = previous;
        }
    }

    /// <summary>
    /// Visits vector queries in post-order: every vector query in a filter before the vector query
    /// that owns it, and each query once even when it also appears elsewhere in the tree.
    /// </summary>
    private sealed class VectorQueryVisitor(Action<VectorQuery> onVector) : QueryVisitor
    {
        private readonly HashSet<Query> _visited = new(ReferenceEqualityComparer.Instance);

        public override void VisitLeaf(Query query)
        {
            if (!_visited.Add(query))
                return;
            if (query is VectorQuery vector)
            {
                // VectorQuery.Visit visits the filter after this call; those leaves are then skipped.
                vector.Filter?.Visit(this);
                onVector(vector);
                return;
            }

            var rewritten = RewriteQuery(query);
            if (!ReferenceEquals(rewritten, query))
                rewritten.Visit(this);
        }
    }
}
