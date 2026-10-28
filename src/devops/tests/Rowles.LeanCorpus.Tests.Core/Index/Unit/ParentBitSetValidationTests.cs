using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class ParentBitSetValidationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public ParentBitSetValidationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadFrom_RejectsUndersizedWordArrayAndRemainsUsable(bool canonical)
    {
        string malformedPath = NewPath("undersized");
        WriteBitSetBody(malformedPath, canonical, length: 10, wordCount: 0);

        Assert.Throws<InvalidDataException>(() => ParentBitSet.ReadFrom(malformedPath));

        string validPath = NewPath("valid");
        var expected = new ParentBitSet(10);
        expected.Set(3);
        expected.WriteTo(validPath);

        var restored = ParentBitSet.ReadFrom(validPath);
        Assert.True(restored.IsParent(3));
        Assert.Equal(3, restored.NextParent(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadFrom_RejectsSetBitsBeyondLogicalLength(bool canonical)
    {
        string path = NewPath("unused-high-bit");
        WriteBitSetBody(path, canonical, length: 10, wordCount: 1, 1L << 10);

        Assert.Throws<InvalidDataException>(() => ParentBitSet.ReadFrom(path));
    }

    [Theory]
    [InlineData(false, -1, 0)]
    [InlineData(false, 0, -1)]
    [InlineData(true, -1, 0)]
    [InlineData(true, 0, -1)]
    public void ReadFrom_RejectsNegativeDimensions(bool canonical, int length, int wordCount)
    {
        string path = NewPath("negative-dimensions");
        WriteBitSetBody(path, canonical, length, wordCount);

        Assert.Throws<InvalidDataException>(() => ParentBitSet.ReadFrom(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadFrom_RejectsOversizedWordArray(bool canonical)
    {
        string path = NewPath("oversized");
        WriteBitSetBody(path, canonical, length: 10, wordCount: 2, 0, 0);

        Assert.Throws<InvalidDataException>(() => ParentBitSet.ReadFrom(path));
    }

    [Fact]
    public void ReadFrom_RejectsTruncatedWordArray()
    {
        string path = NewPath("truncated");
        WriteBitSetBody(path, canonical: false, length: 64, wordCount: 1);

        Assert.Throws<InvalidDataException>(() => ParentBitSet.ReadFrom(path));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void Set_RejectsDocumentIdsOutsideLogicalLength(int docId)
    {
        var bitSet = new ParentBitSet(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => bitSet.Set(docId));
    }

    [Fact]
    public void Constructor_RejectsNegativeLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParentBitSet(-1));
    }

    private string NewPath(string name) => Path.Combine(_fixture.Path, $"{name}-{Guid.NewGuid():N}.pbs");

    private static void WriteBitSetBody(
        string path,
        bool canonical,
        int length,
        int wordCount,
        params long[] words)
    {
        using var output = new IndexOutput(path);
        if (canonical)
        {
            using var frame = CodecFileWriter.Begin(output, ParentBitSet.Descriptor);
            frame.Output.WriteInt32(length);
            frame.Output.WriteInt32(wordCount);
            foreach (long word in words)
                frame.Output.WriteInt64(word);
            frame.Complete();
            return;
        }

        output.WriteInt32(length);
        output.WriteInt32(wordCount);
        foreach (long word in words)
            output.WriteInt64(word);
    }
}
