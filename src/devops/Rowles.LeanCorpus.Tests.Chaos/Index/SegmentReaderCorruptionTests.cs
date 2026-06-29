using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Chaos.Index;

[Trait("Category", "Chaos")]
[Trait("Category", "Index")]
public sealed class SegmentReaderCorruptionTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public SegmentReaderCorruptionTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 8)]
    public void SegmentReader_CorruptedNumericDocValues_ThrowsOnRead(NonNegativeInt byteOffset)
    {
        var path = Path.Combine(_fixture.Path, $"sr_corrupt_dvn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new NumericField("price", 1.0));
            doc1.Add(new StringField("id", "a"));
            writer.AddDocument(doc1);
            var doc2 = new LeanDocument();
            doc2.Add(new NumericField("price", 2.0));
            doc2.Add(new StringField("id", "b"));
            writer.AddDocument(doc2);
            writer.Commit();
        }
        var dvnFile = Directory.GetFiles(path, "*.dvn").Single();
        FlipByte(dvnFile, 0);
        Assert.ThrowsAny<Exception>((Action)(() =>
        {
            using var searcher = new IndexSearcher(directory);
            var reader = searcher.GetSegmentReaders()[0];
            _ = reader.GetNumericDocValues("price");
        }));
    }

    [Property(MaxTest = 8)]
    public void SegmentReader_CorruptedVectorFile_ThrowsOnRead(NonNegativeInt byteOffset)
    {
        var path = Path.Combine(_fixture.Path, $"sr_corrupt_vec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new VectorField("vec", new ReadOnlyMemory<float>([1.0f, 2.0f, 3.0f]), 3));
            doc1.Add(new StringField("id", "a"));
            writer.AddDocument(doc1);
            var doc2 = new LeanDocument();
            doc2.Add(new VectorField("vec", new ReadOnlyMemory<float>([4.0f, 5.0f, 6.0f]), 3));
            doc2.Add(new StringField("id", "b"));
            writer.AddDocument(doc2);
            writer.Commit();
        }
        var vecFile = Directory.GetFiles(path, "*.vec").Single();
        FlipByte(vecFile, 0);
        Assert.ThrowsAny<Exception>((Action)(() =>
        {
            using var searcher = new IndexSearcher(directory);
            var reader = searcher.GetSegmentReaders()[0];
            _ = reader.GetVector(0);
        }));
    }


    [Property(MaxTest = 8)]
    public void SegmentReader_CorruptedNorms_ThrowsOnRead(NonNegativeInt byteOffset)
    {
        var path = Path.Combine(_fixture.Path, $"sr_corrupt_nrm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            doc.Add(new StringField("id", "a"));
            writer.AddDocument(doc);
            writer.Commit();
        }
        var nrmFile = Directory.GetFiles(path, "*.nrm").Single();
        FlipByte(nrmFile, 0);
        Assert.ThrowsAny<Exception>((Action)(() =>
        {
            using var searcher = new IndexSearcher(directory);
            var reader = searcher.GetSegmentReaders()[0];
            _ = reader.GetNorm(0, "body");
        }));
    }

    private static void FlipByte(string path, long offset)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset;
        int original = stream.ReadByte();
        stream.Position = offset;
        stream.WriteByte((byte)~original);
    }

    private static void CorruptWindow(string path, long offset, int length)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset;
        var buf = new byte[length];
        int read = stream.Read(buf, 0, length);
        for (int i = 0; i < read; i++)
            buf[i] = (byte)~buf[i];
        stream.Position = offset;
        stream.Write(buf, 0, read);
    }
}
