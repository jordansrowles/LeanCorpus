using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Coverage tests for DocValues-related methods on <see cref="SegmentReader"/>:
/// TryGetBinaryDocValues, GetBinaryDocValues, EnsureBinaryDocValues (cache),
/// GetNumericDocValues, GetSortedSetDocValues, GetSortedNumericDocValues.
/// </summary>
[Trait("Category", "Index")]
public sealed class SegmentReaderDocValuesTests: IDisposable
{
    private readonly string _dir;

    public SegmentReaderDocValuesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_sr_dv_" + Guid.NewGuid().ToString("N")[..8]);
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

    // TryGetBinaryDocValues / GetBinaryDocValues / EnsureBinaryDocValues

    [Fact(DisplayName = "SegmentReader: TryGetBinaryDocValues With Binary Field Returns Values")]
    public void TryGetBinaryDocValues_WithBinaryField_ReturnsValues()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new BinaryField("body", new byte[] { 1, 2, 3 }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var found = reader.TryGetBinaryDocValues("body", 0, out var values);
            Assert.True(found);
            Assert.Equal(new byte[] { 1, 2, 3 }, values[0]);
        }
    }

    [Fact(DisplayName = "SegmentReader: Stored Text Field Does Not Create Binary DocValues")]
    public void StoredTextField_DoesNotCreateBinaryDocValues()
    {
        var path = Path.Combine(_dir, nameof(StoredTextField_DoesNotCreateBinaryDocValues));
        Directory.CreateDirectory(path);
        var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using (mmap) using (var searcher = new IndexSearcher(mmap))
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.False(reader.TryGetBinaryDocValues("body", 0, out _));
            Assert.Null(reader.GetBinaryDocValues("body"));
        }
    }

    [Fact(DisplayName = "SegmentReader: TryGetBinaryDocValues Missing Field Returns False")]
    public void TryGetBinaryDocValues_MissingField_ReturnsFalse()
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
            Assert.False(reader.TryGetBinaryDocValues("nosuchfield", 0, out _));
        }
    }

    [Fact(DisplayName = "SegmentReader: TryGetBinaryDocValues Out Of Range DocId Returns False")]
    public void TryGetBinaryDocValues_OutOfRangeDocId_ReturnsFalse()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new BinaryField("body", new byte[] { 1, 2, 3 }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.False(reader.TryGetBinaryDocValues("body", 999, out _));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetBinaryDocValues With Binary Field Returns Non Null")]
    public void GetBinaryDocValues_WithBinaryField_ReturnsNonNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new BinaryField("body", new byte[] { 1, 2, 3 }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.NotNull(reader.GetBinaryDocValues("body"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetBinaryDocValues Missing Field Returns Null")]
    public void GetBinaryDocValues_MissingField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new BinaryField("body", new byte[] { 1, 2, 3 }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetBinaryDocValues("nosuchfield"));
        }
    }

    [Fact(DisplayName = "SegmentReader: EnsureBinaryDocValues Second Call Returns Same Reference")]
    public void EnsureBinaryDocValues_SecondCall_ReturnsSameReference()
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
            var first = reader.GetBinaryDocValues("body");
            var second = reader.GetBinaryDocValues("body");
            Assert.Same(first, second);
        }
    }

    // GetNumericDocValues

    [Fact(DisplayName = "SegmentReader: GetNumericDocValues With Numeric Field Returns Non Null")]
    public void GetNumericDocValues_WithNumericField_ReturnsNonNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("price", 9.99));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.NotNull(reader.GetNumericDocValues("price"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetNumericDocValues Missing Field Returns Null")]
    public void GetNumericDocValues_MissingField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("price", 9.99));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetNumericDocValues("nosuchfield"));
        }
    }

    // GetSortedSetDocValues

    [Fact(DisplayName = "SegmentReader: GetSortedSetDocValues With String Field Returns Non Null")]
    public void GetSortedSetDocValues_WithStringField_ReturnsNonNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("tag", "alpha"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.NotNull(reader.GetSortedSetDocValues("tag"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetSortedSetDocValues Missing Field Returns Null")]
    public void GetSortedSetDocValues_MissingField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("tag", "alpha"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetSortedSetDocValues("nosuchfield"));
        }
    }

    // GetSortedNumericDocValues

    [Fact(DisplayName = "SegmentReader: GetSortedNumericDocValues With Numeric Field Returns Non Null")]
    public void GetSortedNumericDocValues_WithNumericField_ReturnsNonNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("score", 7.5));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.NotNull(reader.GetSortedNumericDocValues("score"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetSortedNumericDocValues Missing Field Returns Null")]
    public void GetSortedNumericDocValues_MissingField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("score", 7.5));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetSortedNumericDocValues("nosuchfield"));
        }
    }
}
