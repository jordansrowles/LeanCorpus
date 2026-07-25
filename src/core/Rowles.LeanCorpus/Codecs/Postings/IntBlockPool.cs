using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// A segment-lifetime int allocator that replaces per-term array rentals.
/// Each indexing buffer owns one pool; during flush, <see cref="Reset"/>
/// returns all blocks in bulk.
/// </summary>
internal sealed class IntBlockPool
{
    public const int BlockSize = 8192;

    private readonly List<int[]> _blocks = [];
    private int _currentBlockOffset;

    public int BlockCount => _blocks.Count;

    /// <summary>
    /// Allocates a contiguous region of <paramref name="count"/> ints and returns
    /// the (block index, offset within block).
    /// </summary>
    public (int Block, int Offset) Allocate(int count)
    {
        if (count > BlockSize)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                $"Requested allocation {count} exceeds block size {BlockSize}");

        if (_blocks.Count == 0 || _currentBlockOffset + count > BlockSize)
        {
            _blocks.Add(ArrayPool<int>.Shared.Rent(BlockSize));
            _currentBlockOffset = 0;
        }

        int block = _blocks.Count - 1;
        int offset = _currentBlockOffset;
        _currentBlockOffset += count;
        return (block, offset);
    }

    /// <summary>Returns the underlying array for the given block.</summary>
    public int[] GetBlock(int block) => _blocks[block];

    /// <summary>Returns a span for the allocated region.</summary>
    public Span<int> GetSpan(int block, int offset, int count)
        => _blocks[block].AsSpan(offset, count);

    /// <summary>Returns all blocks to the pool and clears state.</summary>
    public void Reset()
    {
        foreach (var block in _blocks)
            ArrayPool<int>.Shared.Return(block, clearArray: false);
        _blocks.Clear();
        _currentBlockOffset = 0;
    }
}
