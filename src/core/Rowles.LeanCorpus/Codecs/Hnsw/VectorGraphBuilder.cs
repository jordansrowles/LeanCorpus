using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Single-threaded, deterministic builder for the Float32 cosine vector graph (ADR052).
/// The same vectors, document IDs, parameters and seed always produce the same graph.
/// </summary>
/// <remarks>
/// The algorithm is conventional HNSW so it can be compared with Lucene and hnswlib: seeded
/// level assignment <c>floor(-ln(u) / ln(M))</c>, seeded insertion order, an
/// <c>efConstruction</c> beam per level, the diversity heuristic for neighbour selection, and
/// reciprocal links re-pruned through the same heuristic. Two deliberate additions: a node the
/// heuristic leaves with fewer than <c>M / 2</c> neighbours is backfilled from rejected
/// candidates in distance order, and a final repair pass makes every level-zero node
/// reachable from the entry point. Full reachability is verified afterwards: a graph that repair
/// could not connect is refused rather than published. All orderings use <c>(distance, ordinal)</c>.
/// Distances follow <see cref="VectorGraph.NodeDistance"/>, so extreme magnitudes rank exactly as
/// the exact scorer ranks them.
/// </remarks>
internal sealed class VectorGraphBuilder
{
    /// <summary>Default seed when the writer configuration supplies none, so builds stay reproducible.</summary>
    internal const long DefaultSeed = 0x4C43_5647_5231L;

    private const int MaxAssignedLevel = 15;
    internal const int DefaultRepairPasses = 8;

    private readonly IVectorSource _vectors;
    private readonly int[] _docIds;
    private readonly int _m;
    private readonly int _m0;
    private readonly int _efConstruction;
    private readonly long _seed;
    private readonly bool _normalised;
    private readonly int _repairPasses;
    private readonly float[]? _inverseNorms;
    private readonly int[] _level0;
    private readonly int[] _nodeLevels;
    private readonly int[]?[] _upper;
    private readonly int[] _visited;
    private int _epoch;
    private int _entry = -1;
    private int _maxLevel = -1;
    private readonly VectorGraphHeap _candidates = new(maxHeap: false, capacity: 256);
    private readonly VectorGraphHeap _results = new(maxHeap: true, capacity: 256);
    private readonly List<(float Distance, int Ordinal)> _beam = new(256);
    private readonly List<(float Distance, int Ordinal)> _pruneCandidates = new(256);
    private readonly List<(float Distance, int Ordinal)> _selected = new(256);
    private readonly List<(float Distance, int Ordinal)> _rejected = new(256);

    private VectorGraphBuilder(IVectorSource vectors, int[] docIds, HnswBuildConfig config, long seed, bool normalised, int repairPasses)
    {
        _repairPasses = repairPasses;
        _vectors = vectors;
        _docIds = docIds;
        _m = config.M;
        _m0 = config.EffectiveM0;
        _efConstruction = config.EfConstruction;
        _seed = seed;
        _normalised = normalised;
        int n = docIds.Length;
        _level0 = new int[checked(n * (1 + _m0))];
        _nodeLevels = new int[n];
        _upper = new int[n][];
        _visited = new int[n];
        if (!normalised)
        {
            _inverseNorms = new float[n];
            for (int ordinal = 0; ordinal < n; ordinal++)
                _inverseNorms[ordinal] = VectorGraph.InverseNormOf(vectors.GetVector(docIds[ordinal]));
        }
    }

    /// <summary>
    /// Returns whether a graph of <paramref name="nodeCount"/> nodes fits the builder's single
    /// level-zero array. Writers skip the graph (the field stays exact) rather than fail a flush or merge.
    /// </summary>
    internal static bool CanBuild(int nodeCount, HnswBuildConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return nodeCount >= 0 && (long)nodeCount * (1 + config.EffectiveM0) <= Array.MaxLength;
    }

    /// <summary>
    /// Builds a graph over <paramref name="docIds"/>, or throws when repair cannot make every node
    /// reachable. Writers use <see cref="TryBuild"/> and search such a field exactly instead.
    /// </summary>
    internal static VectorGraph Build(
        IVectorSource vectors,
        ReadOnlySpan<int> docIds,
        HnswBuildConfig config,
        long seed,
        bool normalised,
        CancellationToken cancellationToken = default)
        => TryBuild(vectors, docIds, config, seed, normalised, cancellationToken)
            ?? throw new InvalidOperationException("The vector graph could not be made fully reachable.");

    /// <summary>
    /// Builds a graph over <paramref name="docIds"/>, which must be strictly ascending. Returns null
    /// when the repair pass leaves a level-zero node unreachable from the entry point.
    /// </summary>
    /// <param name="vectors">Stored vectors addressed by document ID.</param>
    /// <param name="docIds">Present document IDs, strictly ascending; they become ordinals 0..n-1.</param>
    /// <param name="config">Degree and beam parameters.</param>
    /// <param name="seed">Seed for insertion order and level assignment.</param>
    /// <param name="normalised">Whether the stored vectors are unit length (no inverse norms persisted).</param>
    /// <param name="cancellationToken">Observed between insertions.</param>
    /// <param name="repairPasses">Reachability repair passes; tests lower it to exercise refusal.</param>
    internal static VectorGraph? TryBuild(
        IVectorSource vectors,
        ReadOnlySpan<int> docIds,
        HnswBuildConfig config,
        long seed,
        bool normalised,
        CancellationToken cancellationToken = default,
        int repairPasses = DefaultRepairPasses)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(repairPasses);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(config);
        if (!CanBuild(docIds.Length, config))
            throw new ArgumentOutOfRangeException(nameof(docIds), docIds.Length,
                "The vector graph's level-zero adjacency would exceed the maximum array length; check CanBuild first.");
        for (int i = 1; i < docIds.Length; i++)
        {
            if (docIds[i] <= docIds[i - 1])
                throw new ArgumentException("Graph document IDs must be strictly ascending.", nameof(docIds));
        }

        var builder = new VectorGraphBuilder(vectors, docIds.ToArray(), config, seed, normalised, repairPasses);
        return builder.BuildCore(cancellationToken);
    }

    private VectorGraph? BuildCore(CancellationToken cancellationToken)
    {
        int n = _docIds.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;

        // Seeded so that a given seed always reproduces the same graph; not used for security.
        var random = new Random(unchecked((int)(_seed ^ (_seed >> 32) ^ 0x5A5A5A5AL))); // NOSONAR S2245: deterministic graph construction
        for (int i = n - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        double levelMultiplier = 1.0 / Math.Log(_m);
        for (int i = 0; i < n; i++)
        {
            double uniform = 1.0 - random.NextDouble();
            int level = (int)Math.Floor(-Math.Log(uniform) * levelMultiplier);
            _nodeLevels[order[i]] = Math.Clamp(level, 0, MaxAssignedLevel);
        }

        foreach (int ordinal in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Insert(ordinal);
        }

        if (n > 0)
            RepairLevelZeroReachability();

        // Postcondition: every node is reachable from the entry point, or no graph is published.
        var graph = Freeze();
        if (graph.CountReachableFromEntry() == n)
            return graph;
        graph.Dispose();
        return null;
    }

    private void Insert(int ordinal)
    {
        int level = _nodeLevels[ordinal];
        if (level > 0)
            _upper[ordinal] = new int[level * (1 + _m)];

        if (_entry < 0)
        {
            _entry = ordinal;
            _maxLevel = level;
            return;
        }

        int entry = _entry;
        float entryDistance = Distance(ordinal, entry);
        for (int current = _maxLevel; current > level; current--)
            GreedyDescend(ordinal, current, ref entry, ref entryDistance);

        _beam.Clear();
        _beam.Add((entryDistance, entry));
        for (int current = Math.Min(level, _maxLevel); current >= 0; current--)
        {
            SearchLayer(ordinal, current);
            int degree = current == 0 ? _m0 : _m;
            SelectNeighbours(ordinal, _beam, degree);
            SetNeighbours(ordinal, current, _selected);
            // AddLink reuses the selection buffers, so iterate over the record just written.
            ReadOnlySpan<int> chosen = Neighbours(ordinal, current);
            for (int i = 0; i < chosen.Length; i++)
            {
                int neighbour = chosen[i];
                AddLink(neighbour, ordinal, Distance(ordinal, neighbour), current, degree);
            }
        }

        if (level > _maxLevel)
        {
            _maxLevel = level;
            _entry = ordinal;
        }
    }

    private void GreedyDescend(int baseOrdinal, int level, ref int entry, ref float entryDistance)
    {
        bool improved = true;
        while (improved)
        {
            improved = false;
            foreach (int neighbour in Neighbours(entry, level))
            {
                float distance = Distance(baseOrdinal, neighbour);
                if (VectorGraphHeap.Less(distance, neighbour, entryDistance, entry))
                {
                    entry = neighbour;
                    entryDistance = distance;
                    improved = true;
                }
            }
        }
    }

    /// <summary>Beam search on one level seeded from <see cref="_beam"/>; replaces it with the result, nearest first.</summary>
    private void SearchLayer(int baseOrdinal, int level)
    {
        NextEpoch();
        _candidates.Clear();
        _results.Clear();
        _visited[baseOrdinal] = _epoch;
        foreach (var (distance, seed) in _beam)
        {
            if (_visited[seed] == _epoch)
                continue;
            _visited[seed] = _epoch;
            _candidates.Push(distance, seed);
            _results.Push(distance, seed);
            if (_results.Count > _efConstruction)
                _results.Pop();
        }

        while (_candidates.Count > 0)
        {
            _candidates.Pop(out float candidateDistance, out int candidate);
            if (_results.Count >= _efConstruction
                && VectorGraphHeap.Less(_results.TopDistance, _results.TopOrdinal, candidateDistance, candidate))
                break;

            foreach (int neighbour in Neighbours(candidate, level))
            {
                if (_visited[neighbour] == _epoch)
                    continue;
                _visited[neighbour] = _epoch;
                float distance = Distance(baseOrdinal, neighbour);
                if (_results.Count < _efConstruction
                    || VectorGraphHeap.Less(distance, neighbour, _results.TopDistance, _results.TopOrdinal))
                {
                    _candidates.Push(distance, neighbour);
                    _results.Push(distance, neighbour);
                    if (_results.Count > _efConstruction)
                        _results.Pop();
                }
            }
        }

        _beam.Clear();
        while (_results.Count > 0)
        {
            _results.Pop(out float distance, out int ordinal);
            _beam.Add((distance, ordinal));
        }
        _beam.Reverse();
    }

    /// <summary>
    /// Applies the diversity heuristic to <paramref name="sortedCandidates"/> (nearest first,
    /// distances to <paramref name="baseOrdinal"/>) and leaves the choice in <see cref="_selected"/>.
    /// </summary>
    private void SelectNeighbours(int baseOrdinal, List<(float Distance, int Ordinal)> sortedCandidates, int degree)
    {
        _selected.Clear();
        _rejected.Clear();
        foreach (var candidate in sortedCandidates)
        {
            if (candidate.Ordinal == baseOrdinal)
                continue;
            if (_selected.Count >= degree)
                break;

            bool diverse = true;
            foreach (var chosen in _selected)
            {
                if (Distance(candidate.Ordinal, chosen.Ordinal) < candidate.Distance)
                {
                    diverse = false;
                    break;
                }
            }

            if (diverse)
                _selected.Add(candidate);
            else
                _rejected.Add(candidate);
        }

        int minimum = Math.Min(degree, Math.Max(1, _m / 2));
        for (int i = 0; _selected.Count < minimum && i < _rejected.Count; i++)
            _selected.Add(_rejected[i]);
    }

    private void SetNeighbours(int ordinal, int level, List<(float Distance, int Ordinal)> neighbours)
    {
        Span<int> record = Record(ordinal, level);
        record[0] = neighbours.Count;
        for (int i = 0; i < neighbours.Count; i++)
            record[1 + i] = neighbours[i].Ordinal;
    }

    private void AddLink(int ordinal, int newNeighbour, float distance, int level, int degree)
    {
        Span<int> record = Record(ordinal, level);
        int count = record[0];
        for (int i = 0; i < count; i++)
        {
            if (record[1 + i] == newNeighbour)
                return;
        }

        if (count < degree)
        {
            record[1 + count] = newNeighbour;
            record[0] = count + 1;
            return;
        }

        _pruneCandidates.Clear();
        for (int i = 0; i < count; i++)
        {
            int existing = record[1 + i];
            _pruneCandidates.Add((Distance(ordinal, existing), existing));
        }
        _pruneCandidates.Add((distance, newNeighbour));
        _pruneCandidates.Sort(static (left, right) =>
            VectorGraphHeap.Less(left.Distance, left.Ordinal, right.Distance, right.Ordinal) ? -1
            : VectorGraphHeap.Less(right.Distance, right.Ordinal, left.Distance, left.Ordinal) ? 1 : 0);
        SelectNeighbours(ordinal, _pruneCandidates, degree);
        SetNeighbours(ordinal, level, _selected);
    }

    /// <summary>
    /// Pruning can leave a level-zero node with no path from the entry point. Reached nodes form a
    /// spanning tree from the entry point, and tree edges are never removed, so repair cannot strand
    /// a node it has already reached. Each unreachable node, in ordinal order, is linked from its
    /// nearest reached node that has a free slot, otherwise from the nearest reached node by
    /// replacing that node's farthest non-tree link. When the beam offers neither, any reached node
    /// does: once every reached node is full they hold at least <c>2 * reached</c> links, more than
    /// the <c>reached - 1</c> tree edges. One pass therefore connects the graph; the caller still
    /// verifies the postcondition.
    /// </summary>
    private void RepairLevelZeroReachability()
    {
        int n = _docIds.Length;
        var parent = new int[n];
        var stack = new Stack<int>();
        for (int pass = 0; pass < _repairPasses; pass++)
        {
            Array.Fill(parent, -1);
            parent[_entry] = _entry;
            if (Grow(_entry, parent, stack) == n)
                return;

            for (int ordinal = 0; ordinal < n; ordinal++)
            {
                if (parent[ordinal] >= 0)
                    continue;

                _beam.Clear();
                _beam.Add((Distance(ordinal, _entry), _entry));
                SearchLayer(ordinal, 0);
                int from = LinkFromReached(ordinal, parent);
                if (from < 0)
                    continue;
                parent[ordinal] = from;
                Grow(ordinal, parent, stack);
            }
        }
    }

    /// <summary>Links <paramref name="ordinal"/> from a reached node and returns that node, or -1.</summary>
    private int LinkFromReached(int ordinal, int[] parent)
    {
        foreach (var (_, candidate) in _beam)
        {
            if (parent[candidate] >= 0 && TryAddSlot(candidate, ordinal))
                return candidate;
        }
        foreach (var (_, candidate) in _beam)
        {
            if (parent[candidate] >= 0 && TryReplaceNonTreeLink(candidate, ordinal, parent))
                return candidate;
        }
        for (int candidate = 0; candidate < parent.Length; candidate++)
        {
            if (parent[candidate] >= 0 && (TryAddSlot(candidate, ordinal) || TryReplaceNonTreeLink(candidate, ordinal, parent)))
                return candidate;
        }
        return -1;
    }

    private bool TryAddSlot(int from, int ordinal)
    {
        Span<int> record = Record(from, 0);
        if (record[0] >= _m0)
            return false;
        record[1 + record[0]] = ordinal;
        record[0]++;
        return true;
    }

    /// <summary>Replaces the farthest link of <paramref name="from"/> that is not its child's tree edge.</summary>
    private bool TryReplaceNonTreeLink(int from, int ordinal, int[] parent)
    {
        Span<int> record = Record(from, 0);
        int replaceAt = -1;
        float farthest = float.MinValue;
        for (int i = 0; i < record[0]; i++)
        {
            int neighbour = record[1 + i];
            if (parent[neighbour] == from)
                continue;
            float distance = Distance(from, neighbour);
            if (replaceAt < 0 || VectorGraphHeap.Less(farthest, record[1 + replaceAt], distance, neighbour))
            {
                farthest = distance;
                replaceAt = i;
            }
        }

        if (replaceAt < 0)
            return false;
        record[1 + replaceAt] = ordinal;
        return true;
    }

    /// <summary>Extends the spanning tree from <paramref name="start"/>; returns the nodes newly reached plus one.</summary>
    private int Grow(int start, int[] parent, Stack<int> stack)
    {
        int reached = 1;
        stack.Push(start);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            foreach (int neighbour in Neighbours(current, 0))
            {
                if (parent[neighbour] >= 0)
                    continue;
                parent[neighbour] = current;
                reached++;
                stack.Push(neighbour);
            }
        }
        return reached;
    }

    private VectorGraph Freeze()
    {
        int n = _docIds.Length;
        int maxLevel = n == 0 ? -1 : _maxLevel;
        int levels = Math.Max(0, maxLevel);
        var upperOrdinals = new int[levels][];
        var upperAdjacency = new int[levels][];
        int stride = 1 + _m;
        for (int level = 1; level <= maxLevel; level++)
        {
            var members = new List<int>();
            for (int ordinal = 0; ordinal < n; ordinal++)
            {
                if (_nodeLevels[ordinal] >= level)
                    members.Add(ordinal);
            }

            var adjacency = new int[checked(members.Count * stride)];
            for (int i = 0; i < members.Count; i++)
                Record(members[i], level).CopyTo(adjacency.AsSpan(i * stride, stride));
            upperOrdinals[level - 1] = members.ToArray();
            upperAdjacency[level - 1] = adjacency;
        }

        // Unused slots are -1 in the persisted layout.
        int stride0 = 1 + _m0;
        for (int ordinal = 0; ordinal < n; ordinal++)
        {
            Span<int> record = _level0.AsSpan(ordinal * stride0, stride0);
            record[(1 + record[0])..].Fill(-1);
        }
        foreach (var adjacency in upperAdjacency)
        {
            for (int offset = 0; offset < adjacency.Length; offset += stride)
            {
                Span<int> record = adjacency.AsSpan(offset, stride);
                record[(1 + record[0])..].Fill(-1);
            }
        }

        return VectorGraph.FromArrays(
            _vectors.Dimension, _normalised, _m, _m0, _efConstruction, _seed,
            maxLevel, n == 0 ? -1 : _entry, _docIds, _level0, upperOrdinals, upperAdjacency, _inverseNorms);
    }

    private Span<int> Record(int ordinal, int level)
    {
        if (level == 0)
            return _level0.AsSpan(ordinal * (1 + _m0), 1 + _m0);
        return _upper[ordinal].AsSpan((level - 1) * (1 + _m), 1 + _m);
    }

    private ReadOnlySpan<int> Neighbours(int ordinal, int level)
    {
        Span<int> record = Record(ordinal, level);
        return record.Slice(1, record[0]);
    }

    private float Distance(int left, int right)
        => VectorGraph.NodeDistance(
            _vectors.GetVector(_docIds[left]), _inverseNorms is null ? 1f : _inverseNorms[left],
            _vectors.GetVector(_docIds[right]), _inverseNorms is null ? 1f : _inverseNorms[right]);

    private void NextEpoch()
    {
        if (_epoch == int.MaxValue)
        {
            Array.Clear(_visited);
            _epoch = 0;
        }
        _epoch++;
    }
}
