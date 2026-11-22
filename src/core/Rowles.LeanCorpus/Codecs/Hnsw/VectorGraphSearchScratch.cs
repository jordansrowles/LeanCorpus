namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Scratch for Float32 vector graph traversal (ADR052): epoch-stamped visited marks sized to the
/// graph and two heaps. Borrow with <see cref="Borrow"/> and dispose to return it. Graphs up to
/// <see cref="ThreadCacheMaxNodes"/> nodes share one cached instance per thread, so a thread holds
/// at most a few megabytes. Larger graphs lend scratch from a small pool owned by the graph, which
/// is released with the graph. A nested borrow on the same thread receives a fresh instance.
/// </summary>
internal sealed class VectorGraphSearchScratch : IDisposable
{
    /// <summary>Largest graph whose visited marks a thread may keep after a search (4 MiB).</summary>
    internal const int ThreadCacheMaxNodes = 1 << 20;

    [ThreadStatic]
    private static VectorGraphSearchScratch? t_cached;

    private int[] _visited = [];
    private int _epoch;
    private bool _inUse;
    private VectorGraphScratchPool? _owner;

    /// <summary>Frontier of nodes still to expand, nearest first.</summary>
    internal VectorGraphHeap Candidates { get; } = new(maxHeap: false, capacity: 64);

    /// <summary>Accepted results, farthest first, capped by the caller at <c>ef</c>.</summary>
    internal VectorGraphHeap Results { get; } = new(maxHeap: true, capacity: 64);

    internal static VectorGraphSearchScratch Borrow(int nodeCount, VectorGraphScratchPool? largeGraphPool = null)
    {
        if (nodeCount > ThreadCacheMaxNodes && largeGraphPool is not null)
        {
            var pooled = largeGraphPool.Rent();
            pooled._owner = largeGraphPool;
            pooled._inUse = true;
            pooled.Prepare(nodeCount);
            return pooled;
        }

        var scratch = t_cached;
        if (scratch is null)
        {
            scratch = new VectorGraphSearchScratch();
            if (nodeCount <= ThreadCacheMaxNodes)
                t_cached = scratch;
        }
        else if (scratch._inUse)
        {
            scratch = new VectorGraphSearchScratch();
        }

        scratch._inUse = true;
        scratch.Prepare(nodeCount);
        return scratch;
    }

    /// <summary>Starts a new traversal: all nodes become unvisited and both heaps empty.</summary>
    internal void Prepare(int nodeCount)
    {
        if (_visited.Length < nodeCount)
        {
            _visited = new int[nodeCount];
            _epoch = 0;
        }

        if (_epoch == int.MaxValue)
        {
            Array.Clear(_visited);
            _epoch = 0;
        }

        _epoch++;
        Candidates.Clear();
        Results.Clear();
    }

    /// <summary>Marks <paramref name="ordinal"/> visited and returns whether it was unvisited.</summary>
    internal bool TryVisit(int ordinal)
    {
        if (_visited[ordinal] == _epoch)
            return false;
        _visited[ordinal] = _epoch;
        return true;
    }

    public void Dispose()
    {
        Candidates.Clear();
        Results.Clear();
        _inUse = false;
        if (_owner is { } owner)
        {
            _owner = null;
            owner.Return(this);
        }
        else if (_visited.Length > ThreadCacheMaxNodes)
        {
            // Never keep a large graph's marks on a thread.
            _visited = [];
            _epoch = 0;
        }
    }
}

/// <summary>
/// A bounded pool of traversal scratch owned by one large vector graph. Memory scales with the
/// graph's concurrent searches, not with every thread that ever searched it, and is released when
/// the graph is disposed.
/// </summary>
internal sealed class VectorGraphScratchPool
{
    private readonly System.Collections.Concurrent.ConcurrentBag<VectorGraphSearchScratch> _items = [];
    private readonly int _capacity = Math.Max(2, Environment.ProcessorCount);
    private int _count;

    internal VectorGraphSearchScratch Rent()
    {
        if (_items.TryTake(out var scratch))
        {
            Interlocked.Decrement(ref _count);
            return scratch;
        }
        return new VectorGraphSearchScratch();
    }

    internal void Return(VectorGraphSearchScratch scratch)
    {
        if (Interlocked.Increment(ref _count) <= _capacity)
            _items.Add(scratch);
        else
            Interlocked.Decrement(ref _count);
    }

    internal void Clear()
    {
        while (_items.TryTake(out _))
            Interlocked.Decrement(ref _count);
    }
}
