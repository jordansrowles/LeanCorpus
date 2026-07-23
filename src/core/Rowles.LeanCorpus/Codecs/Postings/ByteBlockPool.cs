using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// A segment-lifetime byte allocator that replaces per-term array rentals.
/// Each indexing buffer owns one pool; during flush, <see cref="Reset"/>
/// returns all blocks in bulk.
/// </summary>
internal sealed class ByteBlockPool
{
    public const int BlockSize = 32768;

    private readonly List<byte[]> _blocks = [];
    private int _currentBlockOffset;

    public int BlockCount => _blocks.Count;

    /// <summary>
    /// Allocates a contiguous region of <paramref name="length"/> bytes and returns
    /// the (block index, offset within block). May span no more than one block.
    /// </summary>
    public (int Block, int Offset) Allocate(int length)
    {
        if (length > BlockSize)
            throw new ArgumentOutOfRangeException(nameof(length), length,
                $"Requested allocation {length} exceeds block size {BlockSize}");

        if (_blocks.Count == 0 || _currentBlockOffset + length > BlockSize)
        {
            _blocks.Add(ArrayPool<byte>.Shared.Rent(BlockSize));
            _currentBlockOffset = 0;
        }

        int block = _blocks.Count - 1;
        int offset = _currentBlockOffset;
        _currentBlockOffset += length;
        return (block, offset);
    }

    /// <summary>
    /// Returns the underlying array for the given block.
    /// </summary>
    public byte[] GetBlock(int block) => _blocks[block];

    /// <summary>
    /// Returns a span for the allocated region.
    /// </summary>
    public Span<byte> GetSpan(int block, int offset, int length)
        => _blocks[block].AsSpan(offset, length);

    /// <summary>Returns all blocks to the pool and clears state.</summary>
    public void Reset()
    {
        foreach (var block in _blocks)
            ArrayPool<byte>.Shared.Return(block, clearArray: false);
        _blocks.Clear();
        _currentBlockOffset = 0;
    }
}
