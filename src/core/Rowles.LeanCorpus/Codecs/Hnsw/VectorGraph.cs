using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Immutable, thread-safe Float32 cosine graph over one vector field of one segment (ADR052).
/// Nodes are dense ordinals in ascending document-ID order. Level-zero adjacency has a fixed
/// stride of <c>1 + M0</c> (count, then slots) and is either an owned array or borrowed from the
/// mapped <c>.vgr</c> body. Upper levels, document IDs and inverse norms are materialised.
/// </summary>
/// <remarks>
/// Distances are negative cosine similarity. Query to node: <c>-dot(q, v) * invNorm(v)</c> for a
/// unit query. Node to node: <c>-dot(a, b) * invNorm(a) * invNorm(b)</c>. A normalised field
/// stores no inverse norms and treats each as one. A zero vector has inverse norm zero, so its
/// distance is zero, matching the exact scorer's cosine of zero.
/// </remarks>
internal sealed class VectorGraph : IDisposable
{
    internal const byte ScorerFloat32Cosine = 1;
    internal const byte FlagNormalised = 1;
    internal const byte FlagInverseNorms = 2;
    internal const int MaxLevels = 64;

    private readonly int[] _docIds;
    private readonly int[]? _level0;
    private readonly IndexInput? _body;
    private readonly long _level0Offset;
    private readonly int[][] _upperOrdinals;
    private readonly int[][] _upperAdjacency;
    private readonly float[]? _inverseNorms;
    private readonly VectorGraphScratchPool _scratchPool = new();
    private int _disposed;

    private VectorGraph(
        int dimension, bool normalised, int m, int m0, int efConstruction, long seed,
        int maxLevel, int entryOrdinal, int[] docIds, int[]? level0, IndexInput? body, long level0Offset,
        int[][] upperOrdinals, int[][] upperAdjacency, float[]? inverseNorms)
    {
        Dimension = dimension;
        Normalised = normalised;
        M = m;
        M0 = m0;
        EfConstruction = efConstruction;
        Seed = seed;
        MaxLevel = maxLevel;
        EntryOrdinal = entryOrdinal;
        _docIds = docIds;
        _level0 = level0;
        _body = body;
        _level0Offset = level0Offset;
        _upperOrdinals = upperOrdinals;
        _upperAdjacency = upperAdjacency;
        _inverseNorms = inverseNorms;
    }

    internal static VectorGraph FromArrays(
        int dimension, bool normalised, int m, int m0, int efConstruction, long seed,
        int maxLevel, int entryOrdinal, int[] docIds, int[] level0,
        int[][] upperOrdinals, int[][] upperAdjacency, float[]? inverseNorms)
        => new(dimension, normalised, m, m0, efConstruction, seed, maxLevel, entryOrdinal, docIds,
            level0, body: null, level0Offset: 0, upperOrdinals, upperAdjacency, inverseNorms);

    /// <summary>Creates a graph that borrows level zero from <paramref name="body"/> and owns it.</summary>
    internal static VectorGraph FromMapped(
        int dimension, bool normalised, int m, int m0, int efConstruction, long seed,
        int maxLevel, int entryOrdinal, int[] docIds, IndexInput body, long level0Offset,
        int[][] upperOrdinals, int[][] upperAdjacency, float[]? inverseNorms)
        => new(dimension, normalised, m, m0, efConstruction, seed, maxLevel, entryOrdinal, docIds,
            level0: null, body, level0Offset, upperOrdinals, upperAdjacency, inverseNorms);

    internal int Dimension { get; }
    internal bool Normalised { get; }
    internal int M { get; }
    internal int M0 { get; }
    internal int EfConstruction { get; }
    internal long Seed { get; }
    internal int MaxLevel { get; }
    internal int EntryOrdinal { get; }
    internal int NodeCount => _docIds.Length;
    internal bool HasInverseNorms => _inverseNorms is not null;

    /// <summary>Document IDs by ordinal, strictly ascending.</summary>
    internal ReadOnlySpan<int> DocIds => _docIds;

    internal ReadOnlySpan<float> InverseNorms => _inverseNorms;

    internal int DocId(int ordinal) => _docIds[ordinal];

    /// <summary>Returns the ordinal of <paramref name="docId"/>, or -1 when it is not a node.</summary>
    internal int OrdinalOf(int docId)
    {
        int index = Array.BinarySearch(_docIds, docId);
        return index >= 0 ? index : -1;
    }

    internal ReadOnlySpan<int> LevelOrdinals(int level)
    {
        if (level <= 0 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        return _upperOrdinals[level - 1];
    }

    /// <summary>Returns the neighbour ordinals of <paramref name="ordinal"/> on <paramref name="level"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<int> Neighbours(int ordinal, int level)
    {
        if (level == 0)
        {
            ReadOnlySpan<int> record = Level0Record(ordinal);
            return record.Slice(1, record[0]);
        }

        int index = Array.BinarySearch(_upperOrdinals[level - 1], ordinal);
        if (index < 0)
            return [];
        int stride = 1 + M;
        ReadOnlySpan<int> upper = _upperAdjacency[level - 1].AsSpan(index * stride, stride);
        return upper.Slice(1, upper[0]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal float InverseNorm(int ordinal) => _inverseNorms is null ? 1f : _inverseNorms[ordinal];

    private ReadOnlySpan<int> Level0Record(int ordinal)
    {
        int stride = 1 + M0;
        if (_level0 is not null)
            return _level0.AsSpan(ordinal * stride, stride);

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        long position = _level0Offset + (long)ordinal * stride * sizeof(int);
        return MemoryMarshal.Cast<byte, int>(_body!.BorrowSpan(stride * sizeof(int), ref position));
    }

    /// <summary>Copies level zero into an owned array, in the persisted record layout.</summary>
    internal int[] CopyLevel0()
    {
        if (_level0 is not null)
            return (int[])_level0.Clone();
        var copy = new int[checked(NodeCount * (1 + M0))];
        for (int ordinal = 0; ordinal < NodeCount; ordinal++)
            Level0Record(ordinal).CopyTo(copy.AsSpan(ordinal * (1 + M0)));
        return copy;
    }

    internal ReadOnlySpan<int> UpperAdjacency(int level) => _upperAdjacency[level - 1];

    /// <summary>Gets the owned level-zero array, or an empty span when level zero is mapped.</summary>
    internal ReadOnlySpan<int> OwnedLevel0 => _level0;

    /// <summary>Gets one level-zero record (count, then <c>M0</c> slots) in the persisted layout.</summary>
    internal ReadOnlySpan<int> Level0RecordAt(int ordinal) => Level0Record(ordinal);

    /// <summary>
    /// Searches for the nearest eligible nodes to a unit-length <paramref name="query"/>.
    /// Ineligible nodes still route the traversal; only eligible nodes become results. Traversal
    /// stops early, reporting <see cref="VectorGraphSearchStats.BudgetExceeded"/>, when the query
    /// distance evaluations would exceed <paramref name="evaluationBudget"/>.
    /// </summary>
    /// <param name="query">Unit-length query vector of the graph's dimension.</param>
    /// <param name="vectors">The segment field's stored vectors, addressed by document ID.</param>
    /// <param name="k">Maximum results to return.</param>
    /// <param name="ef">Level-zero beam width; raised to at least <paramref name="k"/>.</param>
    /// <param name="eligible">Optional document-ID predicate, or null for every node.</param>
    /// <param name="evaluationBudget">Maximum distance evaluations.</param>
    /// <param name="results">Receives up to <paramref name="k"/> hits, nearest first.</param>
    internal VectorGraphSearchStats Search(
        ReadOnlySpan<float> query,
        IVectorSource vectors,
        int k,
        int ef,
        IBitSet? eligible,
        long evaluationBudget,
        List<VectorGraphHit> results)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        if (NodeCount == 0 || k <= 0)
            return default;
        if (query.Length != Dimension)
            throw new ArgumentException($"Query dimension {query.Length} != graph dimension {Dimension}.", nameof(query));

        ef = Math.Max(ef, k);
        int evaluations = 0;
        int visited = 0;
        int layers = 0;
        bool exceeded = false;

        using var scratch = VectorGraphSearchScratch.Borrow(NodeCount, _scratchPool);
        int entry = EntryOrdinal;
        float entryDistance = QueryDistance(query, vectors, entry);
        evaluations++;

        for (int level = MaxLevel; level >= 1; level--)
        {
            layers++;
            bool improved = true;
            while (improved)
            {
                improved = false;
                foreach (int neighbour in Neighbours(entry, level))
                {
                    float distance = QueryDistance(query, vectors, neighbour);
                    evaluations++;
                    if (VectorGraphHeap.Less(distance, neighbour, entryDistance, entry))
                    {
                        entry = neighbour;
                        entryDistance = distance;
                        improved = true;
                    }
                }
            }
        }

        var candidates = scratch.Candidates;
        var best = scratch.Results;
        scratch.TryVisit(entry);
        visited++;
        candidates.Push(entryDistance, entry);
        if (eligible is null || eligible.Contains(_docIds[entry]))
            best.Push(entryDistance, entry);

        while (candidates.Count > 0)
        {
            candidates.Pop(out float candidateDistance, out int candidate);
            if (best.Count >= ef && VectorGraphHeap.Less(best.TopDistance, best.TopOrdinal, candidateDistance, candidate))
                break;

            foreach (int neighbour in Neighbours(candidate, 0))
            {
                if (!scratch.TryVisit(neighbour))
                    continue;
                if (evaluations >= evaluationBudget)
                {
                    exceeded = true;
                    break;
                }

                float distance = QueryDistance(query, vectors, neighbour);
                evaluations++;
                visited++;
                if (best.Count < ef || VectorGraphHeap.Less(distance, neighbour, best.TopDistance, best.TopOrdinal))
                {
                    candidates.Push(distance, neighbour);
                    if (eligible is null || eligible.Contains(_docIds[neighbour]))
                    {
                        best.Push(distance, neighbour);
                        if (best.Count > ef)
                            best.Pop();
                    }
                }
            }

            if (exceeded)
                break;
        }

        // The max-heap yields the farthest first; keep the nearest k in ascending order.
        while (best.Count > k)
            best.Pop();
        int count = best.Count;
        for (int i = 0; i < count; i++)
            results.Add(default);
        for (int i = count - 1; i >= 0; i--)
        {
            best.Pop(out float distance, out int ordinal);
            results[i] = new VectorGraphHit(_docIds[ordinal], distance);
        }

        return new VectorGraphSearchStats(visited, evaluations, layers, exceeded);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float QueryDistance(ReadOnlySpan<float> query, IVectorSource vectors, int ordinal)
        => FiniteDistance(-VectorMath.DotProduct(query, vectors.GetVector(_docIds[ordinal])) * InverseNorm(ordinal));

    /// <summary>Maps a non-finite distance to the farthest value so ordering stays total.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float FiniteDistance(float distance)
        => float.IsFinite(distance) ? distance : float.MaxValue;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _scratchPool.Clear();
            _body?.Dispose();
        }
    }
}
