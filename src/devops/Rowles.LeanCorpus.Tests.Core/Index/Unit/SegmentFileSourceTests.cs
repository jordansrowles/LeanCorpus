using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index.Segment;
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentFileSourceTests : IDisposable
{
    private readonly string _path;

    public SegmentFileSourceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "leancorpus_file_source_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_path);
    }

    [Fact]
    public void LooseAndCompoundSources_ExposeEquivalentLogicalFiles()
    {
        const string segmentId = "seg_test";
        File.WriteAllBytes(Path.Combine(_path, segmentId + ".seg"), [1]);
        File.WriteAllBytes(Path.Combine(_path, segmentId + ".dic"), [2, 3]);
        File.WriteAllBytes(Path.Combine(_path, segmentId + ".pos"), [4, 5, 6]);

        using var directory = new MMapDirectory(_path);
        string[] looseNames;
        using (var loose = new LooseSegmentFileSource(directory, segmentId))
        {
            looseNames = loose.EnumerateFiles().ToArray();
            Assert.Equal(2, loose.GetFileLength(segmentId + ".dic"));
        }

        Assert.True(CompoundFileWriter.Pack(_path, segmentId));
        using var compound = new CompoundSegmentFileSource(directory, segmentId);

        Assert.Equal(looseNames, compound.EnumerateFiles());
        Assert.Equal(3, compound.GetFileLength(segmentId + ".pos"));
        using var input = compound.OpenInput(segmentId + ".pos");
        Assert.Equal(new byte[] { 4, 5, 6 }, input.ReadBytes(3));
    }

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);
}
