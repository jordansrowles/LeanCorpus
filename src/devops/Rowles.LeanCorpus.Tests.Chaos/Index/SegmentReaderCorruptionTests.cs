using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Searcher;
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
    public void SegmentReader_CorruptedPostingsHeader_ThrowsOnOpen(NonNegativeInt byteOffset)
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "sr_corrupt_pos");
        var posFile = Directory.GetFiles(directory.DirectoryPath, "*.pos").Single();
        FlipByte(posFile, byteOffset.Get % 5);

        Assert.ThrowsAny<Exception>((Action)(() => new IndexSearcher(directory)));
    }

    [Property(MaxTest = 8)]
    public void SegmentReader_CorruptedNumericDocValues_ThrowsOnRead(NonNegativeInt byteOffset)
    {
        var path = Path.Combine(_fixture.Path, $"sr_corrupt_dvn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        using var mmap = BuildNumericIndex(path);
        var dvnFile = Directory.GetFiles(path, "*.dvn").Single();
        FlipByte(dvnFile, byteOffset.Get % 5);

        Assert.ThrowsAny<Exception>((Action)(() =>
        {
            using var searcher = new IndexSearcher(mmap);
            var reader = searcher.GetSegmentReaders()[0];
            _ = reader.GetNumericDocValues("price");
        }));
    }

    [Property(MaxTest = 8)]
    public void SegmentReader_CorruptedVectorFile_ThrowsOnRead(NonNegativeInt byteOffset)
    {
        var path = Path.Combine(_fixture.Path, $"sr_corrupt_vec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        using var mmap = BuildVectorIndex(path);
        var vecFile = Directory.GetFiles(path, "*.vec").Single();
        FlipByte(vecFile, byteOffset.Get % 5);

        Assert.ThrowsAny<Exception>((Action)(() =>
        {
            using var searcher = new IndexSearcher(mmap);
            var reader = searcher.GetSegmentReaders()[0];
            _ = reader.GetVector(0);
        }));
    }

    private static MMapDirectory BuildNumericIndex(string path)
    {
        var mmap = new MMapDirectory(path);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new NumericField("price", 9.99));
        writer.AddDocument(doc);
        writer.Commit();
        return mmap;
    }

    private static MMapDirectory BuildVectorIndex(string path)
    {
        var mmap = new MMapDirectory(path);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("title", "test", stored: false));
        doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
        writer.AddDocument(doc);
        writer.Commit();
        return mmap;
    }

    private static void FlipByte(string path, long offset)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset;
        var value = stream.ReadByte();
        Assert.NotEqual(-1, value);
        stream.Position = offset;
        stream.WriteByte((byte)(value ^ 0x5A));
    }
}
