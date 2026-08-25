using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class BkdCanonicalFrameTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public BkdCanonicalFrameTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "BKD: double and Int64 writers emit canonical checksummed frames")]
    public void WritersEmitCanonicalChecksummedFrames()
    {
        string doublePath = Path.Combine(_fixture.Path, $"bkd-{Guid.NewGuid():N}.bkd");
        string int64Path = Path.Combine(_fixture.Path, $"bkd-{Guid.NewGuid():N}.bkdl");
        BKDWriter.Write(doublePath, new() { ["price"] = [(1.5, 1), (2.5, 2)] });
        Int64BKDWriter.Write(int64Path, new() { ["count"] = [(long.MinValue, 1), (long.MaxValue, 2)] });

        AssertCanonical(doublePath, BkdCodecFiles.Double);
        AssertCanonical(int64Path, BkdCodecFiles.Int64);
        using var doubles = BKDReader.Open(doublePath);
        Assert.Equal([1, 2], doubles.RangeQuery("price", 1, 3).Select(static point => point.DocId));
        using var integers = Int64BKDReader.Open(int64Path);
        Assert.Equal([1, 2], integers.RangeQuery("count", long.MinValue, long.MaxValue).Select(static point => point.DocId));
    }

    [Fact(DisplayName = "BKD: readers retain support for legacy envelopes")]
    public void ReadersAcceptLegacyEnvelopes()
    {
        string doublePath = Path.Combine(_fixture.Path, $"legacy-{Guid.NewGuid():N}.bkd");
        string int64Path = Path.Combine(_fixture.Path, $"legacy-{Guid.NewGuid():N}.bkdl");
        BKDWriter.Write(doublePath, new() { ["price"] = [(10.0, 7)] });
        Int64BKDWriter.Write(int64Path, new() { ["count"] = [(42L, 8)] });
        RewriteAsLegacyEnvelope(doublePath, BkdCodecFiles.Double);
        RewriteAsLegacyEnvelope(int64Path, BkdCodecFiles.Int64);

        using var doubles = BKDReader.Open(doublePath);
        Assert.Equal(7, Assert.Single(doubles.RangeQuery("price", 10, 10)).DocId);
        using var integers = Int64BKDReader.Open(int64Path);
        Assert.Equal(8, Assert.Single(integers.RangeQuery("count", 42, 42)).DocId);
    }

    private static void AssertCanonical(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        Assert.Equal(descriptor.CurrentFormatVersion, frame.Metadata.FormatVersion);
        Assert.Equal(CodecFileChecksumAlgorithm.XxHash64, frame.Metadata.ChecksumAlgorithm);
        frame.ValidateChecksum();
    }

    private static void RewriteAsLegacyEnvelope(string path, CodecFileDescriptor descriptor)
    {
        byte[] body;
        using (var input = new IndexInput(path))
        using (var frame = CodecFileReader.Open(input, descriptor))
            body = frame.ReadBody();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(1);
        ulong encoded = checked((ulong)body.Length << 1);
        while (encoded >= 0x80)
        {
            stream.WriteByte((byte)(encoded | 0x80));
            encoded >>= 7;
        }
        stream.WriteByte((byte)encoded);
        stream.Write(body);
    }
}
