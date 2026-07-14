using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Coverage tests for postings-related methods on <see cref="SegmentReader"/>:
/// GetPostingsEnumWithPositions, GetPositions, GetTermFrequency.
/// </summary>
[Trait("Category", "Index")]
public sealed class SegmentReaderPostingsTests: IDisposable
{
    private readonly string _dir;

    public SegmentReaderPostingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_sr_post_" + Guid.NewGuid().ToString("N")[..8]);
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

    // GetPostingsEnumWithPositions

    [Fact(DisplayName = "SegmentReader: GetPostingsEnumWithPositions Missing Term Returns Empty Enum")]
    public void GetPostingsEnumWithPositions_MissingTerm_ReturnsEmptyEnum()
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
            using var pe = reader.GetPostingsEnumWithPositions("body\x00nosuchterm");
            Assert.Equal(0, pe.DocFreq);
            Assert.False(pe.MoveNext());
        }
    }

    [Fact(DisplayName = "SegmentReader: GetPostingsEnumWithPositions Present Term Cursor Advances")]
    public void GetPostingsEnumWithPositions_PresentTerm_CursorAdvances()
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
            using var pe = reader.GetPostingsEnumWithPositions("body\x00hello");
            Assert.True(pe.MoveNext());
            Assert.Equal(0, pe.DocId);
        }
    }

    // GetPositions(string field, string term, int docId)

    [Fact(DisplayName = "SegmentReader: GetPositions Field Term DocId Returns Positions")]
    public void GetPositions_PresentTerm_DocId_ReturnsPositions()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var positions = reader.GetPositions("body", "hello", 0);
            Assert.NotNull(positions);
            Assert.NotEmpty(positions);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetPositions Field Term Missing Returns Null")]
    public void GetPositions_MissingTerm_ReturnsNull()
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
            Assert.Null(reader.GetPositions("body", "nosuchterm", 0));
        }
    }

    // GetPositions(string qualifiedTerm, int docId) — internal

    [Fact(DisplayName = "SegmentReader: GetPositions Qualified Term Present Returns Positions")]
    public void GetPositions_QualifiedTerm_PresentTerm_ReturnsPositions()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var positions = reader.GetPositions("body\x00hello", 0);
            Assert.False(positions.IsEmpty);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetPositions Qualified Term Missing Returns Empty Span")]
    public void GetPositions_QualifiedTerm_MissingTerm_ReturnsEmptySpan()
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
            Assert.True(reader.GetPositions("body\x00nosuchterm", 0).IsEmpty);
        }
    }

    // GetTermFrequency(string field, string term, int docId)

    [Fact(DisplayName = "SegmentReader: GetTermFrequency Field Term DocId Returns Frequency")]
    public void GetTermFrequency_PresentTerm_DocId_ReturnsFrequency()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.True(reader.GetTermFrequency("body", "hello", 0) >= 1);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetTermFrequency Field Term Missing Returns Zero")]
    public void GetTermFrequency_MissingTerm_ReturnsZero()
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
            Assert.Equal(0, reader.GetTermFrequency("body", "nosuchterm", 0));
        }
    }

    // GetTermFrequency(string qualifiedTerm, int docId) — internal

    [Fact(DisplayName = "SegmentReader: GetTermFrequency Qualified Term Present Returns Frequency")]
    public void GetTermFrequency_QualifiedTerm_PresentTerm_ReturnsFrequency()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.True(reader.GetTermFrequency("body\x00hello", 0) >= 1);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetTermFrequency Qualified Term Missing Returns Zero")]
    public void GetTermFrequency_QualifiedTerm_MissingTerm_ReturnsZero()
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
            Assert.Equal(0, reader.GetTermFrequency("body\x00nosuchterm", 0));
        }
    }
}
