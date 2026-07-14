using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Unit.Index;

/// <summary>
/// Unit tests for <see cref="SegmentInfo.ReadFrom"/> error branches.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class SegmentInfoTests : IDisposable
{
    private readonly string _dir;

    public SegmentInfoTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_seg_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    [Fact(DisplayName = "SegmentInfo.ReadFrom: JSON Null Deserialise Throws InvalidDataException")]
    public void ReadFrom_NullJsonDeserialise_ThrowsInvalidDataException()
    {
        var path = Path.Combine(_dir, "null.seg");
        File.WriteAllText(path, "null");

        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(path));
    }

    [Fact(DisplayName = "SegmentInfo.ReadFrom: Valid File Returns SegmentInfo")]
    public void ReadFrom_ValidFile_ReturnsSegmentInfo()
    {
        var info = new SegmentInfo
        {
            SegmentId = "seg_0",
            DocCount = 5,
            LiveDocCount = 5,
        };
        var path = Path.Combine(_dir, "seg_0.seg");
        info.WriteTo(path);

        var loaded = SegmentInfo.ReadFrom(path);
        Assert.Equal("seg_0", loaded.SegmentId);
        Assert.Equal(5, loaded.DocCount);
    }
}
