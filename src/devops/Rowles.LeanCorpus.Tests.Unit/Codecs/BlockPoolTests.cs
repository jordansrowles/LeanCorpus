using Rowles.LeanCorpus.Codecs.Postings;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

public sealed class BlockPoolTests
{
    [Fact(DisplayName = "ByteBlockPool: Allocates contiguous regions within a block")]
    public void ByteBlockPool_AllocatesContiguousRegionsWithinBlock()
    {
        var pool = new ByteBlockPool();
        try
        {
            var first = pool.Allocate(3);
            var second = pool.Allocate(5);

            Assert.Equal((0, 0), first);
            Assert.Equal((0, 3), second);
            Assert.Equal(1, pool.BlockCount);
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "ByteBlockPool: Starts a new block at the boundary")]
    public void ByteBlockPool_StartsNewBlockAtBoundary()
    {
        var pool = new ByteBlockPool();
        try
        {
            var first = pool.Allocate(ByteBlockPool.BlockSize);
            var second = pool.Allocate(1);

            Assert.Equal((0, 0), first);
            Assert.Equal((1, 0), second);
            Assert.Equal(2, pool.BlockCount);
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "ByteBlockPool: Rejects allocations larger than a block")]
    public void ByteBlockPool_RejectsAllocationLargerThanBlock()
    {
        var pool = new ByteBlockPool();
        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => pool.Allocate(ByteBlockPool.BlockSize + 1));

            Assert.Equal("length", exception.ParamName);
            Assert.Equal(0, pool.BlockCount);
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "ByteBlockPool: Span writes update the underlying block")]
    public void ByteBlockPool_SpanWritesUpdateUnderlyingBlock()
    {
        var pool = new ByteBlockPool();
        try
        {
            var allocation = pool.Allocate(4);
            SetValues(pool.GetSpan(allocation.Block, allocation.Offset, 4), 1, 2, 3, 4);

            Assert.Equal(
                new byte[] { 1, 2, 3, 4 },
                pool.GetBlock(allocation.Block).AsSpan(allocation.Offset, 4).ToArray());
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "ByteBlockPool: Reset clears blocks and restarts allocation")]
    public void ByteBlockPool_ResetClearsBlocksAndRestartsAllocation()
    {
        var pool = new ByteBlockPool();
        try
        {
            pool.Allocate(ByteBlockPool.BlockSize);
            pool.Allocate(1);
            Assert.Equal(2, pool.BlockCount);

            pool.Reset();

            Assert.Equal(0, pool.BlockCount);
            Assert.Equal((0, 0), pool.Allocate(2));
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "IntBlockPool: Allocates contiguous regions within a block")]
    public void IntBlockPool_AllocatesContiguousRegionsWithinBlock()
    {
        var pool = new IntBlockPool();
        try
        {
            var first = pool.Allocate(3);
            var second = pool.Allocate(5);

            Assert.Equal((0, 0), first);
            Assert.Equal((0, 3), second);
            Assert.Equal(1, pool.BlockCount);
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "IntBlockPool: Starts a new block at the boundary")]
    public void IntBlockPool_StartsNewBlockAtBoundary()
    {
        var pool = new IntBlockPool();
        try
        {
            var first = pool.Allocate(IntBlockPool.BlockSize);
            var second = pool.Allocate(1);

            Assert.Equal((0, 0), first);
            Assert.Equal((1, 0), second);
            Assert.Equal(2, pool.BlockCount);
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "IntBlockPool: Rejects allocations larger than a block")]
    public void IntBlockPool_RejectsAllocationLargerThanBlock()
    {
        var pool = new IntBlockPool();
        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => pool.Allocate(IntBlockPool.BlockSize + 1));

            Assert.Equal("count", exception.ParamName);
            Assert.Equal(0, pool.BlockCount);
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "IntBlockPool: Span writes update the underlying block")]
    public void IntBlockPool_SpanWritesUpdateUnderlyingBlock()
    {
        var pool = new IntBlockPool();
        try
        {
            var allocation = pool.Allocate(4);
            SetValues(pool.GetSpan(allocation.Block, allocation.Offset, 4), 1, 2, 3, 4);

            Assert.Equal(
                new[] { 1, 2, 3, 4 },
                pool.GetBlock(allocation.Block).AsSpan(allocation.Offset, 4).ToArray());
        }
        finally
        {
            pool.Reset();
        }
    }

    [Fact(DisplayName = "IntBlockPool: Reset clears blocks and restarts allocation")]
    public void IntBlockPool_ResetClearsBlocksAndRestartsAllocation()
    {
        var pool = new IntBlockPool();
        try
        {
            pool.Allocate(IntBlockPool.BlockSize);
            pool.Allocate(1);
            Assert.Equal(2, pool.BlockCount);

            pool.Reset();

            Assert.Equal(0, pool.BlockCount);
            Assert.Equal((0, 0), pool.Allocate(2));
        }
        finally
        {
            pool.Reset();
        }
    }

    private static void SetValues(Span<byte> span, byte first, byte second, byte third, byte fourth)
    {
        span[0] = first;
        span[1] = second;
        span[2] = third;
        span[3] = fourth;
    }

    private static void SetValues(Span<int> span, int first, int second, int third, int fourth)
    {
        span[0] = first;
        span[1] = second;
        span[2] = third;
        span[3] = fourth;
    }
}
