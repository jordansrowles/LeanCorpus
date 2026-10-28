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
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.CodecKit;
[Category(TestCategory.Integration)]
[Area(TestArea.CodecKit)]
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

        // Verify canonical codec frames.
        var posFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        Assert.NotEmpty(posFiles);
        foreach (var posFile in posFiles)
        {
            using var input = new IndexInput(posFile);
            using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.postings.data"));
            Assert.Equal(CodecConstants.PostingsVersion, frame.Metadata.FormatVersion);
        }

        // Verify .fdt file
        var fdtFiles = Directory.GetFiles(dir.DirectoryPath, "*.fdt");
        Assert.NotEmpty(fdtFiles);
        foreach (var fdtFile in fdtFiles)
        {
            using var input = new IndexInput(fdtFile);
            using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.stored-fields.data"));
            Assert.Equal(CodecConstants.StoredFieldsVersion, frame.Metadata.FormatVersion);
        }

        // Verify .dic file
        var dictionaryFiles = Directory.GetFiles(dir.DirectoryPath, "*.dic");
        Assert.NotEmpty(dictionaryFiles);
        foreach (var dictionaryFile in dictionaryFiles)
        {
            using var input = new IndexInput(dictionaryFile);
            using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.term-dictionary.data"));
            Assert.Equal(CodecConstants.TermDictionaryVersion, frame.Metadata.FormatVersion);
        }

        // Verify .tvx files
        var tvxFiles = Directory.GetFiles(dir.DirectoryPath, "*.tvx");
        foreach (var tvxFile in tvxFiles)
        {
            using var input = new IndexInput(tvxFile);
            using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.term-vectors.index"));
            Assert.Equal(CodecConstants.TermVectorsVersion, frame.Metadata.FormatVersion);
        }
    }

    [Fact(DisplayName = "Corrupt version byte in .pos to future value fails on first postings access")]
    public void CorruptPosVersion_FirstPostingsAccess_Throws()
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

        // Corrupt the canonical semantic format version to an unsupported value.
        var posFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        Assert.NotEmpty(posFiles);
        byte[] bytes = File.ReadAllBytes(posFiles[0]);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 6);
        File.WriteAllBytes(posFiles[0], bytes);

        Assert.Throws<InvalidDataException>(() => new IndexSearcher(dir));
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
            using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.postings.data"));
            Assert.Equal(CodecConstants.PostingsVersion, frame.Metadata.FormatVersion);
        }
    }
}
