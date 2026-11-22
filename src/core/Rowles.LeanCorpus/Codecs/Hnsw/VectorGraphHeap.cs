using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Array-backed binary heap of <c>(distance, ordinal)</c> pairs for the Float32 vector graph
/// (ADR052). Pairs are ordered by distance and then by ordinal, so equal distances always
/// resolve the same way. A min-heap yields the nearest pair first; a max-heap the farthest.
/// </summary>
internal sealed class VectorGraphHeap
{
    private readonly bool _maxHeap;
    private float[] _distances;
    private int[] _ordinals;
    private int _count;

    internal VectorGraphHeap(bool maxHeap, int capacity = 16)
    {
        _maxHeap = maxHeap;
        capacity = Math.Max(capacity, 4);
        _distances = new float[capacity];
        _ordinals = new int[capacity];
    }

    internal int Count => _count;

    internal float TopDistance => _distances[0];

    internal int TopOrdinal => _ordinals[0];

    internal void Clear() => _count = 0;

    /// <summary>Returns whether <c>(da, oa)</c> orders strictly before <c>(db, ob)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Less(float da, int oa, float db, int ob)
        => da < db || (da == db && oa < ob);

    internal void Push(float distance, int ordinal)
    {
        if (_count == _distances.Length)
        {
            int grown = checked(_distances.Length * 2);
            Array.Resize(ref _distances, grown);
            Array.Resize(ref _ordinals, grown);
        }

        int index = _count++;
        while (index > 0)
        {
            int parent = (index - 1) >> 1;
            if (!Before(distance, ordinal, _distances[parent], _ordinals[parent]))
                break;
            _distances[index] = _distances[parent];
            _ordinals[index] = _ordinals[parent];
            index = parent;
        }

        _distances[index] = distance;
        _ordinals[index] = ordinal;
    }

    internal void Pop(out float distance, out int ordinal)
    {
        if (_count == 0)
            throw new InvalidOperationException("The heap is empty.");

        distance = _distances[0];
        ordinal = _ordinals[0];
        int last = --_count;
        if (last == 0)
            return;

        float movingDistance = _distances[last];
        int movingOrdinal = _ordinals[last];
        int index = 0;
        while (true)
        {
            int child = (index << 1) + 1;
            if (child >= last)
                break;
            int right = child + 1;
            if (right < last && Before(_distances[right], _ordinals[right], _distances[child], _ordinals[child]))
                child = right;
            if (!Before(_distances[child], _ordinals[child], movingDistance, movingOrdinal))
                break;
            _distances[index] = _distances[child];
            _ordinals[index] = _ordinals[child];
            index = child;
        }

        _distances[index] = movingDistance;
        _ordinals[index] = movingOrdinal;
    }

    /// <summary>Removes the top element without returning it.</summary>
    internal void Pop() => Pop(out _, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Before(float da, int oa, float db, int ob)
        => _maxHeap ? Less(db, ob, da, oa) : Less(da, oa, db, ob);
}
