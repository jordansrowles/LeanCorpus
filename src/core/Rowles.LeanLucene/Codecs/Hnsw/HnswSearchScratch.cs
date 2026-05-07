namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Reusable scratch buffers for one HNSW <c>SearchLayer</c> traversal. Borrow via
/// <see cref="Borrow"/>, dispose to return. A single instance per thread is held in
/// <see cref="System.ThreadStaticAttribute"/> storage so steady-state queries avoid
/// repeated <see cref="HashSet{T}"/> and <see cref="PriorityQueue{TElement, TPriority}"/>
/// allocations. Concurrent searches on the same thread (e.g. nested calls) fall back
/// to a fresh allocation; only the outermost borrow uses the cached instance.
/// </summary>
internal sealed class HnswSearchScratch : IDisposable
{
    [ThreadStatic]
    private static HnswSearchScratch? t_cached;

    private static readonly Comparer<float> ResultsComparer =
        Comparer<float>.Create((a, b) => b.CompareTo(a));

    private bool _inUse;

    /// <summary>Visited node set, cleared on borrow.</summary>
    public HashSet<int> Visited { get; } = new();

    /// <summary>Min-heap of frontier candidates by distance ascending.</summary>
    public PriorityQueue<int, float> Frontier { get; } = new();

    /// <summary>Max-heap of result candidates by distance descending, capped at ef.</summary>
    public PriorityQueue<int, float> Results { get; } = new(ResultsComparer);

    /// <summary>
    /// Borrow the per-thread scratch. If it is already in use (a nested search on the
    /// same thread), a fresh instance is returned that bypasses the cache slot.
    /// </summary>
    public static HnswSearchScratch Borrow()
    {
        var existing = t_cached;
        if (existing is null)
        {
            var fresh = new HnswSearchScratch { _inUse = true };
            t_cached = fresh;
            return fresh;
        }

        if (existing._inUse)
            return new HnswSearchScratch { _inUse = true };

        existing.Reset();
        existing._inUse = true;
        return existing;
    }

    private void Reset()
    {
        Visited.Clear();
        Frontier.Clear();
        Results.Clear();
    }

    public void Dispose()
    {
        Reset();
        _inUse = false;
    }
}
