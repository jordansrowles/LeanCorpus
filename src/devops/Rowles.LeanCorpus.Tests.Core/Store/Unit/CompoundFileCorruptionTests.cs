using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Store;
[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class CompoundFileCorruptionTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "LeanCorpus_CompoundFileCorruptionTests",
        Guid.NewGuid().ToString("N"));

    public CompoundFileCorruptionTests() => Directory.CreateDirectory(_path);

    [Fact]
    public void Open_AdjacentMembers_ExposeExactBoundedSlices()
    {
        const string fileName = "seg_test.cfs";
        WriteCompound(
            fileName,
            [0x10, 0x11, 0x20, 0x21, 0x22],
            ("seg_test.dic", 0, 2),
            ("seg_test.pos", 2, 3));

        using var directory = new MMapDirectory(_path);
        using var compound = CompoundFileReader.Open(directory, fileName);

        Assert.Equal(["seg_test.dic", "seg_test.pos"], compound.FileNames);
        using var first = compound.OpenInput(directory, "seg_test.dic");
        using var second = compound.OpenInput(directory, "seg_test.pos");
        Assert.Equal([0x10, 0x11], first.ReadBytes(2));
        Assert.Throws<EndOfStreamException>(() => first.ReadByte());
        Assert.Equal([0x20, 0x21, 0x22], second.ReadBytes(3));
        Assert.Throws<EndOfStreamException>(() => second.ReadByte());
    }

    [Fact]
    public void Open_InvalidMagic_Throws()
    {
        WriteCompound("bad-magic.cfs", [0x01], ("member", 0, 1));
        CorruptInt32("bad-magic.cfs", 0, 0);

        AssertOpenFails("bad-magic.cfs", "invalid magic");
    }

    [Fact]
    public void Open_FutureVersion_Throws()
    {
        WriteCompound("future.cfs", [0x01], ("member", 0, 1));
        CorruptInt32("future.cfs", sizeof(int), CompoundFileWriter.Version + 1);

        AssertOpenFails("future.cfs", "unsupported version");
    }

    [Fact]
    public void Open_DuplicateMember_Throws()
    {
        WriteCompound(
            "duplicate.cfs",
            [0x01, 0x02],
            ("same", 0, 1),
            ("same", 1, 1));

        AssertOpenFails("duplicate.cfs", "duplicate member");
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 0)]
    [InlineData(0, -1)]
    public void Open_OutOfRangeMember_Throws(int relativeOffset, int length)
    {
        WriteCompound("out-of-range.cfs", [0x01], ("member", relativeOffset, length));

        AssertOpenFails("out-of-range.cfs", "out-of-range member");
    }

    [Fact]
    public void Open_OverlappingMembers_Throws()
    {
        WriteCompound(
            "overlap.cfs",
            [0x01, 0x02, 0x03, 0x04],
            ("first", 0, 3),
            ("second", 2, 2));

        AssertOpenFails("overlap.cfs", "overlapping members");
    }

    [Fact]
    public void Open_TruncatedDirectory_Throws()
    {
        File.WriteAllBytes(
            Path.Combine(_path, "truncated.cfs"),
            [0x4c, 0x43, 0x46, 0x53, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00]);

        using var directory = new MMapDirectory(_path);
        Assert.Throws<EndOfStreamException>(() => CompoundFileReader.Open(directory, "truncated.cfs"));
    }

    [Fact]
    public void MissingMember_ThrowsWithoutExposingContainerBytes()
    {
        WriteCompound("missing.cfs", [0x01], ("present", 0, 1));
        using var directory = new MMapDirectory(_path);
        using var compound = CompoundFileReader.Open(directory, "missing.cfs");

        Assert.False(compound.HasFile("absent"));
        Assert.Throws<FileNotFoundException>(() => compound.GetFileLength("absent"));
        Assert.Throws<FileNotFoundException>(() => compound.OpenInput(directory, "absent"));
    }

    [Fact]
    public void CorruptCanonicalMember_ChecksumFailsWithinMemberBoundary()
    {
        const string memberFormatId = "test.compound-member";
        string loosePath = Path.Combine(_path, "member.bin");
        using (var output = new IndexOutput(loosePath, durable: false))
        using (var writeFrame = CodecFileWriter.Begin(output, memberFormatId, formatVersion: 1))
        {
            writeFrame.Output.WriteBytes([0x10, 0x20, 0x30]);
            writeFrame.Complete();
        }

        byte[] corruptMember = File.ReadAllBytes(loosePath);
        corruptMember[CodecFileWriter.FixedHeaderLength + memberFormatId.Length + 1] ^= 0xff;
        WriteCompound(
            "member-corrupt.cfs",
            [.. corruptMember, 0xaa, 0xbb],
            ("seg_test.dic", 0, corruptMember.Length),
            ("seg_test.pos", corruptMember.Length, 2));

        using var directory = new MMapDirectory(_path);
        using var compound = CompoundFileReader.Open(directory, "member-corrupt.cfs");
        using var member = compound.OpenInput(directory, "seg_test.dic");
        using var frame = CodecFileReader.Open(member, expectedFormatId: memberFormatId);
        var exception = Assert.Throws<CodecFileException>(frame.ValidateChecksum);
        Assert.Equal(CodecFileErrorCode.ChecksumMismatch, exception.ErrorCode);

        using var next = compound.OpenInput(directory, "seg_test.pos");
        Assert.Equal([0xaa, 0xbb], next.ReadBytes(2));
    }

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    private void WriteCompound(
        string fileName,
        byte[] payload,
        params (string Name, int RelativeOffset, int Length)[] entries)
    {
        int directoryLength = entries.Sum(static entry =>
            SevenBitEncodedLength(Encoding.UTF8.GetByteCount(entry.Name)) +
            Encoding.UTF8.GetByteCount(entry.Name) +
            sizeof(long) * 2);
        long dataStart = sizeof(int) * 3L + directoryLength;

        using var stream = File.Create(Path.Combine(_path, fileName));
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(CompoundFileWriter.Magic);
        writer.Write(CompoundFileWriter.Version);
        writer.Write(entries.Length);
        foreach (var entry in entries)
        {
            writer.Write(entry.Name);
            writer.Write(dataStart + entry.RelativeOffset);
            writer.Write((long)entry.Length);
        }
        writer.Write(payload);
    }

    private void CorruptInt32(string fileName, int offset, int value)
    {
        string path = Path.Combine(_path, fileName);
        byte[] bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        File.WriteAllBytes(path, bytes);
    }

    private void AssertOpenFails(string fileName, string expectedMessage)
    {
        using var directory = new MMapDirectory(_path);
        var exception = Assert.Throws<InvalidDataException>(() => CompoundFileReader.Open(directory, fileName));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static int SevenBitEncodedLength(int value)
    {
        int length = 1;
        while ((value >>= 7) != 0)
            length++;
        return length;
    }
}
