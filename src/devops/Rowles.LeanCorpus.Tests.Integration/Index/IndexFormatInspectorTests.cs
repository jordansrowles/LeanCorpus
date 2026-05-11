using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexFormatInspectorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexFormatInspectorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Inspect_ValidIndex_ReportsCurrentCodecVersions()
    {
        using var directory = CreateIndex("format_valid");

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.Equal(directory.DirectoryPath, inventory.DirectoryPath);
        Assert.Equal(1, inventory.CommitGeneration);
        Assert.Single(inventory.SegmentIds);
        var segment = Assert.Single(inventory.Segments);
        Assert.Equal(1, segment.DocCount);
        Assert.Empty(segment.MissingFiles);
        Assert.False(inventory.HasUnsupportedFutureFormat);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".dic" &&
            file.Version == CodecConstants.TermDictionaryVersion &&
            file.CurrentVersion == CodecConstants.TermDictionaryVersion &&
            file.IsCurrent);
    }

    [Fact]
    public void Inspect_FutureCodecVersion_ReportsUnsupportedFutureFormat()
    {
        using var directory = CreateIndex("format_future");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        using (var stream = File.Open(dictionaryPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = sizeof(int);
            stream.WriteByte((byte)(CodecConstants.TermDictionaryVersion + 1));
        }

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.True(inventory.HasUnsupportedFutureFormat);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.UnsupportedFutureCodecVersion);
        var segment = Assert.Single(inventory.Segments);
        Assert.Contains(segment.Files, file => file.Extension == ".dic" && !file.IsSupported);
    }

    [Fact]
    public void Inspect_EmptyDirectory_ReportsNoCommit()
    {
        var path = Path.Combine(_fixture.Path, "format_empty");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.Null(inventory.CommitGeneration);
        Assert.Empty(inventory.Segments);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.NoCommitFile);
    }

    [Fact]
    public void Inspect_OrphanCodecFile_ReportsOrphanInventory()
    {
        using var directory = CreateIndex("format_orphan");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        var orphanPath = Path.Combine(directory.DirectoryPath, "orphan.dic");
        File.Copy(dictionaryPath, orphanPath);

        var inventory = IndexFormatInspector.Inspect(directory);

        var orphan = Assert.Single(inventory.OrphanFiles, file => file.FileName == "orphan.dic");
        Assert.Equal(".dic", orphan.Extension);
        Assert.Equal(CodecConstants.TermDictionaryVersion, orphan.Version);
        Assert.Null(orphan.SegmentId);
    }

    private MMapDirectory CreateIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new TextField("body", "hello world"));
        document.Add(new StringField("id", "1"));
        writer.AddDocument(document);
        writer.Commit();
        return directory;
    }
}
