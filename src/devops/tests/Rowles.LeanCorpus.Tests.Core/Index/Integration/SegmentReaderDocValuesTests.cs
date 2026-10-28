using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Coverage tests for DocValues-related methods on <see cref="SegmentReader"/>:
/// TryGetBinaryDocValues, GetBinaryDocValues, EnsureBinaryDocValues (cache),
/// GetNumericDocValues, GetSortedSetDocValues, GetSortedNumericDocValues.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
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

    [Fact(DisplayName = "SegmentReader: GetBinaryDocValues Returns Independent Deep Copies")]
    public void GetBinaryDocValues_ReturnsIndependentDeepCopies()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new BinaryField("payload", new byte[] { 1, 2, 3 }));
            doc.Add(new BinaryField("payload", new byte[] { 4, 5, 6 }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var first = reader.GetBinaryDocValues("payload");
            var second = reader.GetBinaryDocValues("payload");
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotSame(first, second);

            first![0][0][0] = 99;
            first[0][1] = [88];
            first[0] = [];

            Assert.Equal(new byte[] { 1, 2, 3 }, second![0][0]);
            Assert.Equal(new byte[] { 4, 5, 6 }, second[0][1]);
            Assert.Equal(new byte[] { 1, 2, 3 }, reader.GetBinaryDocValues("payload")![0][0]);
        }
    }

    [Fact(DisplayName = "SegmentReader: Cached DocValues Getters Return Defensive Copies")]
    public void CachedDocValuesGetters_ReturnDefensiveCopies()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("number", 9.99, stored: false));
            doc.Add(new StringField("sorted", "alpha", stored: false));
            doc.Add(new StringField("set", "alpha", stored: false));
            doc.Add(new StringField("set", "beta", stored: false));
            doc.Add(new NumericField("numbers", 7.5, stored: false));
            doc.Add(new NumericField("numbers", 1.5, stored: false));
            doc.Add(new Int64Field("integer", 42, stored: false));
            doc.Add(new Int64Field("integers", 20, stored: false));
            doc.Add(new Int64Field("integers", 10, stored: false));
            doc.Add(new BinaryField("payload", new byte[] { 1, 2, 3 }));
            doc.Add(new TextField("body", "hello world"));
            w.AddDocument(doc);
        });

        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];

            var numeric = reader.GetNumericDocValues("number")!;
            numeric[0] = 123;
            Assert.Equal(9.99, reader.GetNumericDocValues("number")![0]);

            var sorted = reader.GetSortedDocValues("sorted")!;
            sorted[0] = "changed";
            Assert.Equal("alpha", reader.GetSortedDocValues("sorted")![0]);

            var sortedSet = reader.GetSortedSetDocValues("set")!;
            sortedSet[0][0] = "changed";
            sortedSet[0] = [];
            Assert.Equal(["alpha", "beta"], reader.GetSortedSetDocValues("set")![0]);

            var sortedTerms = reader.GetSortedDocValueTerms("sorted")!;
            sortedTerms[0] = "changed";
            Assert.Equal("alpha", reader.GetSortedDocValueTerms("sorted")![0]);

            var sortedSetTerms = reader.GetSortedSetDocValueTerms("set")!;
            sortedSetTerms[0] = "changed";
            Assert.Equal(["alpha", "beta"], reader.GetSortedSetDocValueTerms("set")!);

            var sortedNumeric = reader.GetSortedNumericDocValues("numbers")!;
            sortedNumeric[0][0] = 123;
            Assert.Equal([1.5, 7.5], reader.GetSortedNumericDocValues("numbers")![0]);

            var int64 = reader.GetInt64DocValues("integer")!;
            int64[0] = 123;
            Assert.Equal(42, reader.GetInt64DocValues("integer")![0]);

            var sortedInt64 = reader.GetSortedInt64DocValues("integers")!;
            sortedInt64[0][0] = 123;
            Assert.Equal([10L, 20L], reader.GetSortedInt64DocValues("integers")![0]);

            Assert.True(reader.TryGetFieldLengths("body", out var fieldLengths));
            int originalLength = fieldLengths![0];
            fieldLengths[0] = 0;
            Assert.Equal(originalLength, reader.GetFieldLength(0, "body"));
            Assert.True(reader.TryGetFieldLengths("body", out var nextFieldLengths));
            Assert.Equal(originalLength, nextFieldLengths![0]);
        }
    }

    [Fact(DisplayName = "SegmentReader: Missing sorted values do not add an empty term")]
    public void GetSortedDocValueTerms_MissingDocumentsDoNotCreateEmptyTerm()
    {
        var (dir, searcher) = BuildAndOpen(writer =>
        {
            var present = new LeanDocument();
            present.Add(new StringField("tag", "alpha", stored: false));
            writer.AddDocument(present);
            writer.AddDocument(new LeanDocument());
        });

        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];

            Assert.Equal(["alpha"], reader.GetSortedDocValueTerms("tag")!);
            Assert.Equal(["alpha", ""], reader.GetSortedDocValues("tag")!);
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
