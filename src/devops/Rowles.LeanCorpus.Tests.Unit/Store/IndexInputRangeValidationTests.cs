using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Store;

[Trait("Category", "Store")]
[Trait("Category", "UnitTest")]
public sealed class IndexInputRangeValidationTests : IDisposable
{
    private readonly string _dir;

    public IndexInputRangeValidationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_iirv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    [Fact(DisplayName = "IndexInput: Seek Rejects Positions Outside Mapped Range")]
    public void Seek_RejectsPositionsOutsideMappedRange()
    {
        using var input = OpenInput([1, 2, 3, 4]);

        Assert.Throws<ArgumentOutOfRangeException>(() => input.Seek(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.Seek(input.Length + 1));
        Assert.Equal(0, input.Position);
    }

    [Fact(DisplayName = "IndexInput: Ref Readers Reject Negative Cursors")]
    public void RefReaders_RejectNegativeCursors()
    {
        using var input = OpenInput([1, 2, 3, 4, 5, 6, 7, 8]);
        var ints = new int[1];
        var singles = new float[1];

        long position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadByte(ref position));
        Assert.Equal(-1, position);

        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadBoolean(ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadSpan(1, ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadInt32(ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadInt32Array(ints, 1, ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadInt64(ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadSingle(ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadSingleArray(singles, 1, ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadLengthPrefixedString(ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadVarInt(ref position));
        position = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadVarIntFast(ref position));
    }

    [Fact(DisplayName = "IndexInput: Negative Counts Are Rejected Before Pointer Arithmetic")]
    public void Readers_RejectNegativeCounts()
    {
        using var input = OpenInput([1, 2, 3, 4]);
        var ints = new int[1];
        var singles = new float[1];
        long position = -1;

        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadBytes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadSpan(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadInt32Array(ints, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadSingleArray(singles, -1, ref position));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadUtf8String(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.CompareUtf8BytesAndAdvance(-1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.CompareCharsAndAdvance(-1, []));
    }

    [Fact(DisplayName = "IndexInput: Huge Counts Fail Before Allocation or Pointer Arithmetic")]
    public void Readers_RejectHugeCounts()
    {
        using var input = OpenInput([1, 2, 3, 4]);
        var ints = new int[1];
        var singles = new float[1];
        long position = 0;

        Assert.Throws<EndOfStreamException>(() => input.ReadBytes(int.MaxValue));
        Assert.Throws<EndOfStreamException>(() => input.ReadSpan(int.MaxValue));
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32Array(ints, int.MaxValue));
        Assert.Throws<EndOfStreamException>(() => input.ReadSingleArray(singles, int.MaxValue, ref position));
        Assert.Equal(0, input.Position);
        Assert.Equal(0, position);
    }

    [Fact(DisplayName = "IndexInput: Overflowed Ref Cursors Fail Before Pointer Arithmetic")]
    public void RefReaders_RejectOverflowedCursors()
    {
        using var input = OpenInput([1, 2, 3, 4, 5, 6, 7, 8]);
        var ints = new int[1];
        var singles = new float[1];

        long position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadByte(ref position));
        Assert.Equal(long.MaxValue, position);

        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadSpan(1, ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32(ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32Array(ints, 1, ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadInt64(ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadSingle(ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadSingleArray(singles, 1, ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadVarInt(ref position));
        position = long.MaxValue;
        Assert.Throws<EndOfStreamException>(() => input.ReadVarIntFast(ref position));
    }

    [Fact(DisplayName = "IndexInput: Readers Throw ObjectDisposedException After Dispose")]
    public void Readers_AfterDispose_ThrowObjectDisposedException()
    {
        var input = OpenInput([1, 2, 3]);
        input.Dispose();

        Assert.Throws<ObjectDisposedException>(() => input.Seek(0));
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => input.ReadBytes(1));
        Assert.Throws<ObjectDisposedException>(() => input.Prefetch());
    }

    private IndexInput OpenInput(byte[] bytes)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllBytes(path, bytes);
        return new IndexInput(path);
    }
}
