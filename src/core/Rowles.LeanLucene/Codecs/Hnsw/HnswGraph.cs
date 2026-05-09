using System.Runtime.CompilerServices;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Util;
using Rowles.LeanLucene.Codecs.Vectors;
namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// In-memory hierarchical navigable small world graph.
/// Vectors are expected to be L2-normalised so that dot product equals cosine similarity;
/// the graph itself works in distance space (lower is better) with distance defined as
/// negative dot product.
/// </summary>
/// <remarks>
/// <para>The graph supports two lifecycle states. While mutable (the default after construction)
/// only single-threaded use is permitted: <see cref="Insert"/> mutates internal adjacency lists
/// without locks. Once <see cref="Freeze"/> has been called, search becomes thread-safe and
/// lock-free; mutation is no longer permitted.</para>
/// <para>Pruning uses the diversity-preserving heuristic from the original HNSW paper rather
/// than the simple top-M variant, which gives materially better recall on clustered embedding
/// spaces typical of real-world workloads.</para>
/// </remarks>
internal sealed class HnswGraph
{
    private const int NoEntryPoint = -1;

    private readonly IVectorSource _vectors;
    private readonly Random _rng;
    private readonly double _levelMultiplier;

    // Layer 0 is the base; index increases with sparsity.
    private readonly List<Dictionary<int, List<int>>> _mutableLevels;

    // Populated after Freeze: stable adjacency arrays for thread-safe reads.
    private List<Dictionary<int, int[]>>? _frozenLevels;

    /// <summary>Vector dimension; matches <see cref="IVectorSource.Dimension"/>.</summary>
    public int Dimension => _vectors.Dimension;

    /// <summary>Maximum neighbours per node on layers above zero.</summary>
    public int M { get; }

    /// <summary>Maximum neighbours per node on layer zero (typically <c>2 * M</c>).</summary>
    public int M0 { get; }

    /// <summary>Candidate set size used during graph construction.</summary>
    public int EfConstruction { get; }

    /// <summary>Document identifier of the top-layer entry point, or -1 when the graph is empty.</summary>
    public int EntryPoint { get; private set; } = NoEntryPoint;

    /// <summary>Highest layer containing any node, or -1 when empty.</summary>
    public int MaxLevel { get; private set; } = -1;

    /// <summary>Number of distinct documents present in the graph.</summary>
    public int NodeCount { get; private set; }

    /// <summary>Seed used by the random number generator. Persisted to the .hnsw file for reproducibility.</summary>
    public long Seed { get; }

    /// <summary>True once <see cref="Freeze"/> has been called; mutation is prohibited and search is thread-safe.</summary>
    public bool IsReadOnly => _frozenLevels is not null;

    public HnswGraph(IVectorSource vectors, HnswBuildConfig config, long seed)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(config);
        if (config.M < 2) throw new ArgumentOutOfRangeException(nameof(config), "M must be at least 2.");
        if (config.EfConstruction < 1) throw new ArgumentOutOfRangeException(nameof(config), "efConstruction must be positive.");

        _vectors = vectors;
        M = config.M;
        M0 = config.EffectiveM0;
        EfConstruction = config.EfConstruction;
        Seed = seed;
        _rng = new Random(unchecked((int)seed));
        _levelMultiplier = 1.0 / Math.Log(M);
        _mutableLevels = [new Dictionary<int, List<int>>()];
    }

    /// <summary>
    /// Builds an <see cref="HnswGraph"/> from pre-loaded adjacency. Used by the reader to materialise
    /// a graph from a .hnsw file. The graph is created in the read-only state immediately.
    /// </summary>
    internal static HnswGraph FromFrozen(
        IVectorSource vectors,
        HnswBuildConfig config,
        long seed,
        List<Dictionary<int, int[]>> levels,
        int entryPoint,
        int maxLevel,
        int nodeCount)
    {
        var graph = new HnswGraph(vectors, config, seed)
        {
            EntryPoint = entryPoint,
            MaxLevel = maxLevel,
            NodeCount = nodeCount,
            _frozenLevels = levels,
        };
        return graph;
    }

    /// <summary>Marks the graph as immutable. Required before search can be called concurrently.</summary>
    public void Freeze()
    {
        if (IsReadOnly) return;

        var frozen = new List<Dictionary<int, int[]>>(_mutableLevels.Count);
        foreach (var level in _mutableLevels)
        {
            var dict = new Dictionary<int, int[]>(level.Count);
            foreach (var (docId, neighbours) in level)
                dict[docId] = neighbours.ToArray();
            frozen.Add(dict);
        }
        _frozenLevels = frozen;
        _mutableLevels.Clear();
    }

    /// <summary>
    /// Reverses <see cref="Freeze"/>: copies frozen adjacency back into the mutable structure
    /// so further <see cref="Insert"/> calls are permitted. Used by incremental merge to seed
    /// a new graph from the largest input segment's existing graph.
    /// </summary>
    internal void Thaw()
    {
        if (_frozenLevels is null) return;

        _mutableLevels.Clear();
        foreach (var level in _frozenLevels)
        {
            var dict = new Dictionary<int, List<int>>(level.Count);
            foreach (var (docId, neighbours) in level)
                dict[docId] = [.. neighbours];
            _mutableLevels.Add(dict);
        }
        _frozenLevels = null;
    }

    /// <summary>True if a node with the given id is already present at layer 0.</summary>
    internal bool ContainsNode(int docId)
    {
        if (_frozenLevels is not null)
            return _frozenLevels.Count > 0 && _frozenLevels[0].ContainsKey(docId);
        return _mutableLevels.Count > 0 && _mutableLevels[0].ContainsKey(docId);
    }

    /// <summary>Inserts a node into the graph. The node's vector must already be present in the source.</summary>
    public void Insert(int docId)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("HnswGraph is frozen; insert is not permitted.");

        int newLevel = AssignLevel();
        var query = _vectors.GetVector(docId);

        if (NodeCount == 0)
        {
            EnsureLevels(newLevel);
            for (int l = 0; l <= newLevel; l++)
                _mutableLevels[l][docId] = new List<int>(LevelDegree(l));
            EntryPoint = docId;
            MaxLevel = newLevel;
            NodeCount = 1;
            return;
        }

        EnsureLevels(newLevel);

        // Greedy descent from the entry point through any layers above the new node's level.
        int currentEntry = EntryPoint;
        for (int l = MaxLevel; l > newLevel; l--)
            currentEntry = GreedyDescent(query, currentEntry, l);

        // For layers from min(MaxLevel, newLevel) down to 0, run efConstruction-search and connect.
        var entryPoints = new List<int> { currentEntry };
        for (int l = Math.Min(MaxLevel, newLevel); l >= 0; l--)
        {
            var candidates = SearchLayer(query, entryPoints, EfConstruction, l, allowList: null, out _);
            // candidates is in arbitrary order; convert to a sorted distance ascending list for selection.
            var sorted = SortAscByDistance(candidates);
            int degree = LevelDegree(l);
            var neighbours = SelectNeighboursHeuristic(query, sorted, degree);

            // Bidirectional linking: add neighbours to the new node and vice versa, pruning back-edges as needed.
            _mutableLevels[l][docId] = neighbours.Select(n => n.DocId).ToList();
            foreach (var n in neighbours)
            {
                if (!_mutableLevels[l].TryGetValue(n.DocId, out var nList))
                {
                    nList = new List<int>(degree);
                    _mutableLevels[l][n.DocId] = nList;
                }
                nList.Add(docId);
                if (nList.Count > degree)
                    PruneNeighbours(n.DocId, l, degree);
            }

            // Use the unfiltered candidates as entry points for the next layer down.
            entryPoints = sorted.Select(c => c.DocId).ToList();
        }

        if (newLevel > MaxLevel)
        {
            MaxLevel = newLevel;
            EntryPoint = docId;
        }

        NodeCount++;
    }

    /// <summary>
    /// Searches the graph for the closest documents to a query vector.
    /// Safe for concurrent callers once <see cref="Freeze"/> has been called.
    /// </summary>
    public IReadOnlyList<HnswSearchResult> Search(ReadOnlySpan<float> query, HnswSearchOptions options)
        => SearchCore(query, options, out _);

    /// <summary>
    /// Searches the graph and returns per-call statistics for diagnostics and metrics.
    /// </summary>
    internal IReadOnlyList<HnswSearchResult> Search(
        ReadOnlySpan<float> query,
        HnswSearchOptions options,
        out HnswSearchStats stats)
        => SearchCore(query, options, out stats);

    private IReadOnlyList<HnswSearchResult> SearchCore(
        ReadOnlySpan<float> query,
        HnswSearchOptions options,
        out HnswSearchStats stats)
    {
        ArgumentNullException.ThrowIfNull(options);
        stats = default;
        if (NodeCount == 0 || EntryPoint == NoEntryPoint)
            return Array.Empty<HnswSearchResult>();

        int currentEntry = EntryPoint;
        int layersDescended = 0;
        for (int l = MaxLevel; l > 0; l--)
        {
            currentEntry = GreedyDescent(query, currentEntry, l);
            layersDescended++;
        }

        int ef = Math.Max(1, options.Ef);
        int retriesLeft = options.MaxPostFilterRetries;
        IReadOnlyList<HnswSearchResult> results;
        int totalVisited = 0;

        while (true)
        {
            var raw = SearchLayer(query, [currentEntry], ef, level: 0, options.AllowList, out int visitedThisIteration);
            totalVisited += visitedThisIteration;
            var ranked = SortAscByDistance(raw);
            var filtered = options.PostFilterMask is null
                ? ranked
                : ranked.Where(r => options.PostFilterMask.Contains(r.DocId)).ToList();

            int target = options.TopK > 0 ? options.TopK : filtered.Count;
            if (filtered.Count >= target || retriesLeft <= 0 || options.PostFilterMask is null)
            {
                results = filtered
                    .Take(options.TopK > 0 ? options.TopK : filtered.Count)
                    .Select(c => new HnswSearchResult(c.DocId, -c.Distance))
                    .ToArray();
                break;
            }

            ef *= 2;
            retriesLeft--;
        }

        stats = new HnswSearchStats(totalVisited, layersDescended);
        return results;
    }

    /// <summary>Returns the neighbours of a node at a given layer. Used by the writer.</summary>
    internal IReadOnlyList<int> GetNeighbours(int docId, int level)
    {
        if (_frozenLevels is not null)
        {
            return _frozenLevels[level].TryGetValue(docId, out var arr) ? arr : Array.Empty<int>();
        }
        return _mutableLevels[level].TryGetValue(docId, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();
    }

    /// <summary>Enumerates every document identifier present at a given layer. Used by the writer.</summary>
    internal IEnumerable<int> GetNodesAtLevel(int level)
    {
        if (_frozenLevels is not null)
            return _frozenLevels[level].Keys;
        return _mutableLevels[level].Keys;
    }

    /// <summary>Number of layers including layer zero.</summary>
    internal int LevelCount => _frozenLevels?.Count ?? _mutableLevels.Count;

    private int AssignLevel()
    {
        // -ln(rand) * mL gives an exponentially decaying level distribution.
        double r;
        do { r = _rng.NextDouble(); } while (r <= double.Epsilon);
        return (int)Math.Floor(-Math.Log(r) * _levelMultiplier);
    }

    private void EnsureLevels(int upToLevel)
    {
        while (_mutableLevels.Count <= upToLevel)
            _mutableLevels.Add(new Dictionary<int, List<int>>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int LevelDegree(int level) => level == 0 ? M0 : M;

    private int GreedyDescent(ReadOnlySpan<float> query, int entry, int level)
    {
        int current = entry;
        float currentDist = Distance(query, _vectors.GetVector(current));
        bool improved;
        do
        {
            improved = false;
            foreach (var n in NeighboursAt(current, level))
            {
                float d = Distance(query, _vectors.GetVector(n));
                if (d < currentDist)
                {
                    currentDist = d;
                    current = n;
                    improved = true;
                }
            }
        } while (improved);
        return current;
    }

    private IReadOnlyList<int> NeighboursAt(int docId, int level)
    {
        if (_frozenLevels is not null)
            return _frozenLevels[level].TryGetValue(docId, out var arr) ? arr : Array.Empty<int>();
        return _mutableLevels[level].TryGetValue(docId, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();
    }

    /// <summary>
    /// Returns up to <paramref name="ef"/> candidate (docId, distance) pairs reachable
    /// from the entry points at the given layer. Order of the returned list is unspecified.
    /// </summary>
    private List<(int DocId, float Distance)> SearchLayer(
        ReadOnlySpan<float> query,
        IReadOnlyList<int> entryPoints,
        int ef,
        int level,
        IBitSet? allowList,
        out int nodesVisited)
    {
        using var scratch = HnswSearchScratch.Borrow();
        var visited = scratch.Visited;
        var frontier = scratch.Frontier;
        var results = scratch.Results;

        foreach (var ep in entryPoints)
        {
            if (visited.Add(ep))
            {
                float d = Distance(query, _vectors.GetVector(ep));
                frontier.Enqueue(ep, d);
                if (allowList is null || allowList.Contains(ep))
                {
                    results.Enqueue(ep, d);
                    if (results.Count > ef) results.Dequeue();
                }
            }
        }

        while (frontier.Count > 0)
        {
            if (!frontier.TryDequeue(out int current, out float currentDist))
                break;

            if (results.Count >= ef && results.TryPeek(out _, out float worstInResult) && currentDist > worstInResult)
                break;

            foreach (var neighbour in NeighboursAt(current, level))
            {
                if (!visited.Add(neighbour)) continue;

                float d = Distance(query, _vectors.GetVector(neighbour));
                bool resultsFull = results.Count >= ef;
                bool eligibleForResults = allowList is null || allowList.Contains(neighbour);

                if (!resultsFull || (eligibleForResults && results.TryPeek(out _, out float currentWorst) && d < currentWorst))
                {
                    frontier.Enqueue(neighbour, d);
                    if (eligibleForResults)
                    {
                        results.Enqueue(neighbour, d);
                        if (results.Count > ef) results.Dequeue();
                    }
                }
            }
        }

        var output = new List<(int DocId, float Distance)>(results.Count);
        while (results.TryDequeue(out int id, out float dist))
            output.Add((id, dist));
        nodesVisited = visited.Count;
        return output;
    }

    private static List<(int DocId, float Distance)> SortAscByDistance(List<(int DocId, float Distance)> list)
    {
        list.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return list;
    }

    /// <summary>
    /// Diversity-preserving neighbour selection (Algorithm 4 of the HNSW paper).
    /// Picks up to <paramref name="m"/> elements from the candidate list, accepting
    /// each only when it is closer to the query than to every already-selected element.
    /// </summary>
    private List<(int DocId, float Distance)> SelectNeighboursHeuristic(
        ReadOnlySpan<float> query,
        List<(int DocId, float Distance)> sortedCandidates,
        int m)
    {
        var selected = new List<(int DocId, float Distance)>(m);
        foreach (var c in sortedCandidates)
        {
            if (selected.Count >= m) break;
            bool good = true;
            var candidateVec = _vectors.GetVector(c.DocId);
            foreach (var s in selected)
            {
                float dToSelected = Distance(candidateVec, _vectors.GetVector(s.DocId));
                if (dToSelected < c.Distance)
                {
                    good = false;
                    break;
                }
            }
            if (good) selected.Add(c);
        }
        return selected;
    }

    private void PruneNeighbours(int docId, int level, int degree)
    {
        var list = _mutableLevels[level][docId];
        var nodeVec = _vectors.GetVector(docId);
        var ranked = new List<(int DocId, float Distance)>(list.Count);
        foreach (var n in list)
            ranked.Add((DocId: n, Distance: Distance(nodeVec, _vectors.GetVector(n))));
        ranked.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        var kept = SelectNeighboursHeuristic(nodeVec, ranked, degree);
        list.Clear();
        foreach (var n in kept) list.Add(n.DocId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Distance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        // Vectors are expected to be L2-normalised; dot product then equals cosine similarity.
        // Convert to a distance where smaller is better.
        return -SimdVectorOps.DotProduct(a, b);
    }
}
