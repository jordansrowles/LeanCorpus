using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Coverage tests for vector-related methods on <see cref="SegmentReader"/>:
/// GetVector(int), GetVector(string, int), EnsureVectorReaderNoLock (cache and missing-path branches).
/// </summary>
[Trait("Category", "Index")]
public sealed class SegmentReaderVectorTests: IDisposable
{
    private readonly string _dir;

    public SegmentReaderVectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_sr_vec_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    private (MMapDirectory Dir, IndexSearcher Searcher) BuildAndOpen(Action<IndexWriter> populate)
    {
        var mmap = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            populate(writer);
            writer.Commit();
        }
        return (mmap, new IndexSearcher(mmap));
    }

    // GetVector(int docId) — field-less legacy overload

    [Fact(DisplayName = "SegmentReader: GetVector DocId With Vector Field Returns Vector")]
    public void GetVector_DocId_WithVectorField_ReturnsVector()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var vec = reader.GetVector(0);
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetVector DocId No Vector Field Returns Null")]
    public void GetVector_DocId_NoVectorField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetVector(0));
        }
    }

    // GetVector(string fieldName, int docId)

    [Fact(DisplayName = "SegmentReader: GetVector FieldName DocId Returns Vector")]
    public void GetVector_FieldName_DocId_ReturnsVector()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var vec = reader.GetVector("embed", 0);
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetVector FieldName Missing Field Returns Null")]
    public void GetVector_FieldName_MissingField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetVector("nosuchfield", 0));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetVector Empty FieldName Single Vector Field Falls Back To First")]
    public void GetVector_EmptyFieldName_SingleVectorField_FallsBackToFirst()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 4f, 5f, 6f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var vec = reader.GetVector(string.Empty, 0);
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
        }
    }

    // EnsureVectorReaderNoLock — cache and missing-path branches

    [Fact(DisplayName = "SegmentReader: EnsureVectorReaderNoLock Cached Reader Returns Same Instance")]
    public void EnsureVectorReaderNoLock_CachedReader_ReturnsSameInstance()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var first = reader.GetVector("embed", 0);
            var second = reader.GetVector("embed", 0);
            // Both calls should succeed and return equal vectors (reader is cached).
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first, second);
        }
    }

    [Fact(DisplayName = "SegmentReader: EnsureVectorReaderNoLock Missing Path Returns Null")]
    public void EnsureVectorReaderNoLock_MissingPath_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            // "other" was never registered in _vectorPaths, so EnsureVectorReaderNoLock returns null.
            Assert.Null(reader.GetVector("other", 0));
        }
    }
}
