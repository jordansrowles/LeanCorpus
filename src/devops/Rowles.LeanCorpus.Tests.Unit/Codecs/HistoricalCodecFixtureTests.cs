using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Literal fixtures captured from the documented pre-canonical layouts. These bytes are deliberately
/// not produced by current codec writers, so the compatibility tests cannot drift with writer changes.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "Compatibility")]
public sealed class HistoricalCodecFixtureTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "LeanCorpus_HistoricalCodecFixtureTests",
        Guid.NewGuid().ToString("N"));

    public HistoricalCodecFixtureTests() => Directory.CreateDirectory(_path);

    [Fact]
    public void V1EnvelopeNumericFixture_ReadsSemanticValue()
    {
        string path = WriteFixture("legacy-v1.dvn", HistoricalCodecFixtures.NumericEnvelopeV1);

        var (values, presence) = NumericDocValuesReader.Read(path);

        Assert.Equal([42d], values["count"]);
        Assert.Null(presence["count"]);
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.doc-values.numeric");
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.OpenSupported(input, descriptor);
        Assert.False(frame.IsCanonical);
        Assert.Equal(1, frame.FormatVersion);
        using var legacyInput = new IndexInput(path);
        using var legacy = LegacyCodecFileReader.Open(legacyInput, descriptor);
        Assert.Equal(LegacyCodecFrameKind.Envelope, legacy.Metadata.FrameKind);
    }

    [Fact]
    public void V2TrailerNumericFixture_ReadsSemanticValue()
    {
        string path = WriteFixture("legacy-v2.dvn", HistoricalCodecFixtures.NumericTrailerV2);

        var (values, presence) = NumericDocValuesReader.Read(path);

        Assert.Equal([42d], values["count"]);
        Assert.Null(presence["count"]);
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.doc-values.numeric");
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.OpenSupported(input, descriptor);
        Assert.False(frame.IsCanonical);
        Assert.Equal(2, frame.FormatVersion);
        using var legacyInput = new IndexInput(path);
        using var legacy = LegacyCodecFileReader.Open(legacyInput, descriptor);
        Assert.Equal(LegacyCodecFrameKind.Trailer, legacy.Metadata.FrameKind);
    }

    [Fact]
    public void V2CustomHeaderPostingsFixture_ReadsDocumentIds()
    {
        string path = WriteFixture("legacy-v2.pos", HistoricalCodecFixtures.PostingsCustomHeaderV2);

        Assert.Equal([3, 7], PostingsReader.ReadDocIds(path, "ignored"));
        using var input = new IndexInput(path);
        Assert.Equal(2, PostingsEnum.ValidateFileHeader(input));
    }

    [Fact]
    public void V1HeaderlessLiveDocsFixture_ReadsDeletedDocument()
    {
        string path = WriteFixture("legacy-v1.del", HistoricalCodecFixtures.LiveDocsHeaderlessV1);

        var liveDocs = LiveDocs.Deserialise(path, maxDoc: 3);

        Assert.Equal(2, liveDocs.LiveCount);
        Assert.True(liveDocs.IsLive(0));
        Assert.False(liveDocs.IsLive(1));
        Assert.True(liveDocs.IsLive(2));
    }

    public void Dispose()
    {
        if (Directory.Exists(_path))
            Directory.Delete(_path, recursive: true);
    }

    private string WriteFixture(string name, ReadOnlySpan<byte> bytes)
    {
        string path = Path.Combine(_path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
