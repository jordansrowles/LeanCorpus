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
/// reachable from the entry point. All orderings use <c>(distance, ordinal)</c>.
/// </remarks>
internal sealed class VectorGraphBuilder
{
    /// <summary>Default seed when the writer configuration supplies none, so builds stay reproducible.</summary>
    internal const long DefaultSeed = 0x4C43_5647_5231L;

    private const int MaxAssignedLevel = 15;
    private const int MaxRepairPasses = 8;

    private readonly IVectorSource _vectors;
    private readonly int[] _docIds;
    private readonly int _m;
    private readonly int _m0;
    private readonly int _efConstruction;
    private readonly long _seed;
    private readonly bool _normalised;
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

    private VectorGraphBuilder(IVectorSource vectors, int[] docIds, HnswBuildConfig config, long seed, bool normalised)
    {
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
                _inverseNorms[ordinal] = InverseNormOf(vectors.GetVector(docIds[ordinal]));
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

    /// <summary>Builds a graph over <paramref name="docIds"/>, which must be strictly ascending.</summary>
    /// <param name="vectors">Stored vectors addressed by document ID.</param>
    /// <param name="docIds">Present document IDs, strictly ascending; they become ordinals 0..n-1.</param>
    /// <param name="config">Degree and beam parameters.</param>
    /// <param name="seed">Seed for insertion order and level assignment.</param>
    /// <param name="normalised">Whether the stored vectors are unit length (no inverse norms persisted).</param>
    /// <param name="cancellationToken">Observed between insertions.</param>
    internal static VectorGraph Build(
        IVectorSource vectors,
        ReadOnlySpan<int> docIds,
        HnswBuildConfig config,
        long seed,
        bool normalised,
        CancellationToken cancellationToken = default)
    {
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

        var builder = new VectorGraphBuilder(vectors, docIds.ToArray(), config, seed, normalised);
        return builder.BuildCore(cancellationToken);
    }

    private VectorGraph BuildCore(CancellationToken cancellationToken)
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

        return Freeze();
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
    /// Pruning can leave a level-zero node with no path from the entry point. Each unreachable
    /// node is linked from its nearest reachable node, in ordinal order. A replaced link can in
    /// rare cases strand another node, so reachability is re-checked for a bounded number of passes.
    /// </summary>
    private void RepairLevelZeroReachability()
    {
        int n = _docIds.Length;
        var reached = new bool[n];
        var stack = new Stack<int>();
        var inDegree = new int[n];
        for (int pass = 0; pass < MaxRepairPasses; pass++)
        {
            Array.Clear(reached);
            MarkReachable(_entry, reached, stack);
            if (Array.IndexOf(reached, false) < 0)
                return;

            Array.Clear(inDegree);
            for (int ordinal = 0; ordinal < n; ordinal++)
            {
                foreach (int neighbour in Neighbours(ordinal, 0))
                    inDegree[neighbour]++;
            }

            for (int ordinal = 0; ordinal < n; ordinal++)
            {
                if (reached[ordinal])
                    continue;

                _beam.Clear();
                _beam.Add((Distance(ordinal, _entry), _entry));
                SearchLayer(ordinal, 0);
                if (LinkFromNearestWithCapacity(ordinal, reached, inDegree)
                    || ReplaceFarthestRedundantLink(ordinal, reached, inDegree))
                {
                    MarkReachable(ordinal, reached, stack);
                }
            }
        }
    }

    private bool LinkFromNearestWithCapacity(int ordinal, bool[] reached, int[] inDegree)
    {
        foreach (var (_, candidate) in _beam)
        {
            if (!reached[candidate])
                continue;
            Span<int> record = Record(candidate, 0);
            if (record[0] >= _m0)
                continue;
            record[1 + record[0]] = ordinal;
            record[0]++;
            inDegree[ordinal]++;
            return true;
        }

        return false;
    }

    private bool ReplaceFarthestRedundantLink(int ordinal, bool[] reached, int[] inDegree)
    {
        foreach (var (_, candidate) in _beam)
        {
            if (!reached[candidate])
                continue;
            Span<int> record = Record(candidate, 0);
            int replaceAt = -1;
            float farthest = float.MinValue;
            for (int i = 0; i < record[0]; i++)
            {
                int neighbour = record[1 + i];
                if (inDegree[neighbour] <= 1)
                    continue;
                float distance = Distance(candidate, neighbour);
                if (distance > farthest)
                {
                    farthest = distance;
                    replaceAt = i;
                }
            }

            if (replaceAt < 0)
                continue;
            inDegree[record[1 + replaceAt]]--;
            record[1 + replaceAt] = ordinal;
            inDegree[ordinal]++;
            return true;
        }

        return false;
    }

    private void MarkReachable(int start, bool[] reached, Stack<int> stack)
    {
        if (reached[start])
            return;
        reached[start] = true;
        stack.Push(start);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            foreach (int neighbour in Neighbours(current, 0))
            {
                if (!reached[neighbour])
                {
                    reached[neighbour] = true;
                    stack.Push(neighbour);
                }
            }
        }
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
    {
        float dot = VectorMath.DotProduct(_vectors.GetVector(_docIds[left]), _vectors.GetVector(_docIds[right]));
        float distance = _inverseNorms is null ? -dot : -dot * _inverseNorms[left] * _inverseNorms[right];
        return VectorGraph.FiniteDistance(distance);
    }

    private void NextEpoch()
    {
        if (_epoch == int.MaxValue)
        {
            Array.Clear(_visited);
            _epoch = 0;
        }
        _epoch++;
    }

    /// <summary>Returns <c>1 / ||v||</c>, or zero for a zero or non-finite vector.</summary>
    internal static float InverseNormOf(ReadOnlySpan<float> vector)
    {
        double squared = 0;
        foreach (float value in vector)
            squared += (double)value * value;
        if (!(squared > 0) || !double.IsFinite(squared))
            return 0f;
        float inverse = (float)(1.0 / Math.Sqrt(squared));
        return float.IsFinite(inverse) ? inverse : 0f;
    }
}
