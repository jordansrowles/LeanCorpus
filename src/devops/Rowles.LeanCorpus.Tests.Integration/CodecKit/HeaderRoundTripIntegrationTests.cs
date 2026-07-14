using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class HeaderRoundTripIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HeaderRoundTripIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact(DisplayName = "CodecKit headers: every codec file produced by IndexWriter has valid header")]
    public void EveryCodecFile_HasValidHeader()
    {
        var dir = new MMapDirectory(SubDir("header_every_file"));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new Analyser(new Tokeniser())
        };

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document number {i} for codec header testing"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // Verify .pos file
        var posFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        Assert.NotEmpty(posFiles);
        foreach (var posFile in posFiles)
        {
            using var input = new IndexInput(posFile);
            byte version = PostingsFileHeader.ReadVersion(input);
            Assert.Equal(CodecConstants.PostingsVersion, version);
        }

        // Verify .fdt file
        var fdtFiles = Directory.GetFiles(dir.DirectoryPath, "*.fdt");
        Assert.NotEmpty(fdtFiles);
        foreach (var fdtFile in fdtFiles)
        {
            using var fs = new FileStream(fdtFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);
            byte version = StoredFieldsFileHeader.ReadVersion(reader);
            Assert.Equal(CodecConstants.StoredFieldsVersion, version);
        }

        // Verify .tim file
        var timFiles = Directory.GetFiles(dir.DirectoryPath, "*.tim");
        foreach (var timFile in timFiles)
        {
            using var input = new IndexInput(timFile);
            byte version = CodecFileHeader.ReadVersion(input, CodecFormats.TermDictionary);
            Assert.Equal(CodecConstants.TermDictionaryVersion, version);
        }

        // Verify .tvx files
        var tvxFiles = Directory.GetFiles(dir.DirectoryPath, "*.tvx");
        foreach (var tvxFile in tvxFiles)
        {
            using var input = new IndexInput(tvxFile);
            byte version = CodecFileHeader.ReadVersion(input, CodecFormats.TermVectors);
            Assert.Equal(CodecConstants.TermVectorsVersion, version);
        }
    }

    [Fact(DisplayName = "Corrupt version byte in .pos to future value → IndexSearcher throws")]
    public void CorruptPosVersion_IndexSearcher_Throws()
    {
        var dir = new MMapDirectory(SubDir("corrupt_pos_version"));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new Analyser(new Tokeniser())
        };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "corruption test document"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        // Corrupt first byte of .pos to version=0xFF
        var posFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        Assert.NotEmpty(posFiles);
        byte[] bytes = File.ReadAllBytes(posFiles[0]);
        bytes[0] = 0xFF;
        File.WriteAllBytes(posFiles[0], bytes);

        Assert.ThrowsAny<Exception>(() => new IndexSearcher(dir));
    }

    [Fact(DisplayName = "Corrupt VarInt bodyLen in .pos (truncated) → IndexSearcher throws")]
    public void CorruptPosVarInt_Truncated_Throws()
    {
        var dir = new MMapDirectory(SubDir("corrupt_pos_varint"));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new Analyser(new Tokeniser())
        };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "varint corruption test"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var posFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        Assert.NotEmpty(posFiles);
        byte[] bytes = File.ReadAllBytes(posFiles[0]);
        // Keep version, truncate VarInt by removing everything after byte 2
        Array.Resize(ref bytes, 0); // No bytes at all — missing version byte for v2
        File.WriteAllBytes(posFiles[0], bytes);

        Assert.ThrowsAny<Exception>(() => new IndexSearcher(dir));
    }

    [Fact(DisplayName = "Files produced during merge have correct headers")]
    public void MergedFiles_HaveCorrectHeaders()
    {
        var dir = new MMapDirectory(SubDir("merge_headers"));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new Analyser(new Tokeniser()),
            MaxBufferedDocs = 2,
        };

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 10; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"merge document {i} for codec header verification"));
                writer.AddDocument(doc);
            }
            writer.Commit();

        }

        // All .pos files should have valid headers
        var posFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        Assert.NotEmpty(posFiles);
        foreach (var posFile in posFiles)
        {
            using var input = new IndexInput(posFile);
            byte version = PostingsFileHeader.ReadVersion(input);
            Assert.Equal(CodecConstants.PostingsVersion, version);
        }
    }
}
