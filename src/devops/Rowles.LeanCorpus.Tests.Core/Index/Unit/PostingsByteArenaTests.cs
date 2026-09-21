using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Index.Indexer.Postings;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class PostingsByteArenaTests
{
    [Fact]
    public void SingleByteStream_RoundTripsAndStopsAtLogicalEnd()
    {
        var pool = new TrackingBytePool();
        using var arena = new PostingsByteArena(pool);
        var cursor = arena.StartStream();
        arena.WriteByte(ref cursor, 0xA7);

        var reader = arena.OpenReader(cursor);
        Assert.False(reader.EndOfStream);
        Assert.Equal(0xA7, reader.ReadByte());
        Assert.True(reader.EndOfStream);
        AssertInvalidRead(ref reader);
    }

    [Fact]
    public void VarUInt_RoundTripsAll32BitLengths()
    {
        uint[] values = [0, 1, 0x7F, 0x80, 0x3FFF, 0x4000, 0x1FFFFF, 0x200000, 0xFFFFFFF, uint.MaxValue];
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();
        foreach (uint value in values)
            arena.WriteVarUInt(ref cursor, value);

        var reader = arena.OpenReader(cursor);
        foreach (uint expected in values)
            Assert.Equal(expected, reader.ReadVarUInt());
        Assert.True(reader.EndOfStream);
    }

    [Fact]
    public void ByteSpans_CrossEverySliceLevelAndMultipleBlocks()
    {
        const int length = (3 * PostingsByteArena.BlockSize) + 8192;
        byte[] expected = Enumerable.Range(0, length).Select(static i => (byte)i).ToArray();
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();
        arena.WriteBytes(ref cursor, expected);

        byte[] actual = new byte[expected.Length];
        var reader = arena.OpenReader(cursor);
        reader.CopyBytes(actual);
        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfStream);
        Assert.True(arena.BlockCount >= 4);
    }

    [Fact]
    public void LargeStream_AndDirtyRentedBlocksDoNotDependOnZeroedMemory()
    {
        var pool = new TrackingBytePool(fill: 0xD3);
        using var arena = new PostingsByteArena(pool);
        byte[] expected = new byte[(2 * 1024 * 1024) + 17];
        Random.Shared.NextBytes(expected);
        var cursor = arena.StartStream();
        arena.WriteBytes(ref cursor, expected);

        byte[] actual = new byte[expected.Length];
        var reader = arena.OpenReader(cursor);
        reader.CopyBytes(actual);
        Assert.Equal(expected, actual);
        Assert.True(pool.AllRented.Count > 1);
    }

    [Fact]
    public void EmptyStream_IsAtLogicalEnd()
    {
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();
        var reader = arena.OpenReader(cursor);
        Assert.True(reader.EndOfStream);
        AssertInvalidCopy(ref reader, 1);
        AssertInvalidVarUInt(ref reader);
    }

    [Fact]
    public void OversizedLogicalCopyAndSkip_AreRejectedBeforeReading()
    {
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();
        arena.WriteBytes(ref cursor, new byte[29]);

        var copyReader = arena.OpenReader(cursor);
        AssertInvalidCopy(ref copyReader, 30);

        var skipReader = arena.OpenReader(cursor);
        AssertInvalidSkip(ref skipReader, 30);
    }

    [Fact]
    public void ExactLogicalLength_SucceedsAcrossSlices()
    {
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();
        byte[] expected = Enumerable.Range(0, (2 * PostingsByteArena.BlockSize) + 17)
            .Select(static i => (byte)i)
            .ToArray();
        arena.WriteBytes(ref cursor, expected);

        var reader = arena.OpenReader(cursor);
        byte[] actual = new byte[expected.Length];
        reader.CopyBytes(actual);

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfStream);
        AssertInvalidSkip(ref reader, 1);
    }

    [Fact]
    public void TruncatedVarUInt_IsRejected()
    {
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();
        arena.WriteByte(ref cursor, 0x80);
        var reader = arena.OpenReader(cursor);
        AssertInvalidVarUInt(ref reader);
    }

    [Fact]
    public void InvalidForwardAddress_IsRejected()
    {
        var pool = new TrackingBytePool();
        using var arena = new PostingsByteArena(pool);
        var cursor = arena.StartStream();
        arena.WriteBytes(ref cursor, new byte[29]);
        BinaryPrimitives.WriteInt32LittleEndian(pool.AllRented[0].AsSpan(), int.MaxValue);

        var reader = arena.OpenReader(cursor);
        AssertInvalidSkip(ref reader, 29);
    }

    [Fact]
    public void ForwardAddressOutsideLogicalBlock_IsRejected()
    {
        var pool = new TrackingBytePool();
        using var arena = new PostingsByteArena(pool);
        var cursor = arena.StartStream();
        arena.WriteBytes(ref cursor, new byte[29]);
        int invalidNext = PostingsByteArena.ComposeAddress(0, PostingsByteArena.BlockSize - 1);
        BinaryPrimitives.WriteInt32LittleEndian(pool.AllRented[0].AsSpan(), invalidNext);

        var reader = arena.OpenReader(cursor);
        AssertInvalidSkip(ref reader, 29);
    }

    [Fact]
    public void AddressOutsideArenaAndAddressOverflow_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PostingsByteArena.ComposeAddress(int.MaxValue, 0));
        Assert.Throws<InvalidDataException>(() => PostingsByteArena.DecodeAddress(0, out _, out _));
    }

    [Fact]
    public void Dispose_IsIdempotentAndReturnsEveryBlockOnce()
    {
        var pool = new TrackingBytePool();
        var arena = new PostingsByteArena(pool);
        var cursor = arena.StartStream();
        arena.WriteBytes(ref cursor, new byte[PostingsByteArena.BlockSize + 1]);
        long allocated = arena.AllocatedBytes;
        Assert.True(allocated > 0);

        arena.Dispose();
        arena.Dispose();

        Assert.Equal(0, arena.AllocatedBytes);
        Assert.Equal(pool.AllRented.Count, pool.ReturnedCount);
        Assert.Equal(0, pool.DoubleReturnCount);
        Assert.Throws<ObjectDisposedException>(() => arena.StartStream());
    }

    private static void AssertInvalidRead(ref PostingsByteArena.Reader reader)
    {
        try
        {
            reader.ReadByte();
            Assert.Fail("Expected a malformed stream exception.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void AssertInvalidVarUInt(ref PostingsByteArena.Reader reader)
    {
        try
        {
            reader.ReadVarUInt();
            Assert.Fail("Expected a malformed VarUInt exception.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void AssertInvalidCopy(ref PostingsByteArena.Reader reader, int length)
    {
        try
        {
            reader.CopyBytes(new byte[length]);
            Assert.Fail("Expected a malformed stream exception.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void AssertInvalidSkip(ref PostingsByteArena.Reader reader, int length)
    {
        try
        {
            reader.SkipBytes(length);
            Assert.Fail("Expected a malformed forwarding exception.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private sealed class TrackingBytePool : ArrayPool<byte>
    {
        private readonly object _gate = new();
        private readonly HashSet<byte[]> _active = [];
        private readonly byte _fill;

        internal TrackingBytePool(byte fill = 0xA5) => _fill = fill;

        internal List<byte[]> AllRented { get; } = [];
        internal int ReturnedCount { get; private set; }
        internal int DoubleReturnCount { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            var array = new byte[Math.Max(PostingsByteArena.BlockSize, minimumLength)];
            Array.Fill(array, _fill);
            lock (_gate)
            {
                AllRented.Add(array);
                _active.Add(array);
            }
            return array;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            lock (_gate)
            {
                if (!_active.Remove(array))
                    DoubleReturnCount++;
                else
                    ReturnedCount++;
            }
            if (clearArray)
                Array.Clear(array);
        }
    }
}
