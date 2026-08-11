using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class NormsCanonicalFrameTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LeanCorpus_NormsFrame", Guid.NewGuid().ToString("N"));

    public NormsCanonicalFrameTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void NormalWriter_EmitsCatalogueCurrentVersion()
    {
        string path = Path.Combine(_directory, "segment.nrm");
        var norms = new Dictionary<string, float[]>
        {
            ["body"] = [0.25f, 0.5f, 1f],
        };

        NormsWriter.Write(path, norms);

        var descriptor = CodecCatalog.Default.GetFile("leancorpus.norms.data");
        using var input = new IndexInput(path);
        using var session = CodecFileReader.Open(input, CodecCatalog.Default);
        Assert.Equal(descriptor.FormatId, session.Metadata.FormatId);
        Assert.Equal(descriptor.CurrentFormatVersion, session.Metadata.FormatVersion);
        Assert.Equal(CodecFileWriter.CurrentFrameVersion, session.Metadata.FrameVersion);
        Assert.Equal(CodecFileChecksumAlgorithm.XxHash64, session.Metadata.ChecksumAlgorithm);
        session.ValidateChecksum();
    }

    [Fact]
    public void CurrentFrame_RoundTripsThroughProductionReader()
    {
        string path = Path.Combine(_directory, "roundtrip.nrm");
        var norms = new Dictionary<string, float[]>
        {
            ["title"] = [0f, 0.5f, 1f],
        };
        var boosts = new Dictionary<string, float[]>
        {
            ["title"] = [1f, 2f, 1f],
        };

        NormsWriter.Write(path, norms, boosts);
        var restored = NormsReader.Read(path);

        Assert.Equal(new byte[] { 0, 128, 255 }, restored.Norms["title"]);
        Assert.Equal(new float[] { 1f, 2f, 1f }, restored.Boosts["title"]);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
            return;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Directory.Delete(_directory, recursive: true);
    }
}
