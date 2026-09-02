using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Tests.Core.Codecs.CodecKit;
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class CanonicalAuxiliaryFrameTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LeanCorpus_AuxiliaryFrames", Guid.NewGuid().ToString("N"));

    public CanonicalAuxiliaryFrameTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void TermDictionaryWriter_EmitsCanonicalCurrentFrame()
    {
        string path = Path.Combine(_directory, "segment.dic");
        TermDictionaryWriter.Write(path, ["body\0term"], new Dictionary<string, long> { ["body\0term"] = 42 });

        AssertCurrentFrame(path, "leancorpus.term-dictionary.data");
        using var reader = TermDictionaryReader.Open(path);
        Assert.True(reader.TryGetPostingsOffset("body\0term", out long offset));
        Assert.Equal(42, offset);
    }

    [Fact]
    public void PostingsWriter_EmitsCanonicalCurrentFrame()
    {
        string path = Path.Combine(_directory, "segment.pos");

        PostingsWriter.Write(path, "body\0term", [1, 4, 9]);

        AssertCurrentFrame(path, "leancorpus.postings.data");
    }

    [Fact]
    public void FieldLengthWriter_EmitsCanonicalCurrentFrame()
    {
        string path = Path.Combine(_directory, "segment.fln");
        FieldLengthWriter.Write(path, new Dictionary<string, int[]> { ["body"] = [1, 2, 3] });

        AssertCurrentFrame(path, "leancorpus.field-lengths.data");
        var restored = FieldLengthReader.TryRead(path);
        Assert.Equal(new[] { 1, 2, 3 }, restored!["body"]);
    }

    [Fact]
    public void FieldLengthReader_RejectsCanonicalChecksumMismatch()
    {
        string path = Path.Combine(_directory, "corrupt.fln");
        FieldLengthWriter.Write(path, new Dictionary<string, int[]> { ["body"] = [1, 2, 3] });
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var exception = Assert.Throws<CodecFileException>(() => FieldLengthReader.TryRead(path));

        Assert.Equal(CodecFileErrorCode.ChecksumMismatch, exception.ErrorCode);
    }

    [Fact]
    public void LiveDocsWriter_EmitsCanonicalCurrentFrame()
    {
        string path = Path.Combine(_directory, "segment.del");
        var liveDocs = new LiveDocs(8);
        liveDocs.Delete(2);
        liveDocs.SoftDelete(5, 1234);

        LiveDocs.Serialise(path, liveDocs);

        AssertCurrentFrame(path, "leancorpus.deletes.live-docs");
        var restored = LiveDocs.Deserialise(path, 8);
        Assert.False(restored.IsLive(2));
        Assert.False(restored.IsLive(5));
        Assert.Equal(1234, restored.SoftDeleteTimestamps![5]);
    }

    [Fact]
    public void LiveDocsReader_AcceptsHeaderlessLegacyBody()
    {
        string path = Path.Combine(_directory, "legacy.del");
        var deleted = new RoaringBitmap();
        deleted.Add(3);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            deleted.Serialise(writer);
            writer.Write(0);
        }

        var restored = LiveDocs.Deserialise(path, 6);
        Assert.False(restored.IsLive(3));
        Assert.Equal(5, restored.LiveCount);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
            return;
        TestDirectoryFixture.TryDeleteDirectory(_directory);
    }

    private static void AssertCurrentFrame(string path, string formatId)
    {
        var descriptor = CodecCatalog.Default.GetFile(formatId);
        using var input = new IndexInput(path);
        using var session = CodecFileReader.Open(input, descriptor);
        Assert.Equal(descriptor.CurrentFormatVersion, session.Metadata.FormatVersion);
        Assert.Equal(CodecFileChecksumAlgorithm.XxHash64, session.Metadata.ChecksumAlgorithm);
        session.ValidateChecksum();
    }
}
