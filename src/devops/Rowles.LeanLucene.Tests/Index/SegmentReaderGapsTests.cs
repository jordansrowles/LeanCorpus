using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Gap-coverage tests for <see cref="SegmentReader"/> obtained from a real index.
/// Covers: IsLive, HasDeletions, GetNorm, GetFieldLength, TryGetFieldLengths,
/// GetDocIds, GetDocFreq, HasTermVectors, GetTermVectors, GetParentBitSet.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class SegmentReaderGapsTests : IDisposable
{
    private readonly string _dir;

    public SegmentReaderGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_sr_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (MMapDirectory Dir, IndexSearcher Searcher) BuildAndOpen(
        Action<IndexWriter> populate)
    {
        var mmap = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            populate(writer);
            writer.Commit();
        }
        return (mmap, new IndexSearcher(mmap));
    }

    // IsLive / HasDeletions (no deletions)

    [Fact(DisplayName = "SegmentReader: IsLive Returns True When No Deletions")]
    public void IsLive_NoDeletions_ReturnsTrue()
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
            Assert.True(reader.IsLive(0));
            Assert.False(reader.HasDeletions);
        }
    }

    // GetNorm

    [Fact(DisplayName = "SegmentReader: GetNorm Returns NonZero For Indexed Field")]
    public void GetNorm_IndexedField_ReturnsNonZero()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "the quick brown fox"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            float norm = reader.GetNorm(0, "body");
            Assert.True(norm > 0f);
            Assert.True(norm <= 1f);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetNorm Returns Zero For Missing Field")]
    public void GetNorm_MissingField_ReturnsZero()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Equal(0f, reader.GetNorm(0, "title"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetNorm DocId Returns NonZero For First Field")]
    public void GetNorm_DocId_ReturnsNonZeroForFirstField()
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
            float norm = reader.GetNorm(0);
            Assert.True(norm > 0f);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetNorm Out-Of-Range DocId Returns Zero")]
    public void GetNorm_OutOfRange_ReturnsZero()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Equal(0f, reader.GetNorm(9999, "body"));
        }
    }

    // GetFieldLength / TryGetFieldLengths

    [Fact(DisplayName = "SegmentReader: GetFieldLength Returns Positive For Indexed Field")]
    public void GetFieldLength_IndexedField_ReturnsPositive()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "one two three four five"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            int len = reader.GetFieldLength(0, "body");
            Assert.True(len >= 1);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetFieldLength Missing Field Returns One")]
    public void GetFieldLength_MissingField_ReturnsOne()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Equal(1, reader.GetFieldLength(0, "missing"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetFieldLength Out-Of-Range DocId Returns One")]
    public void GetFieldLength_OutOfRange_ReturnsOne()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Equal(1, reader.GetFieldLength(9999, "body"));
        }
    }

    [Fact(DisplayName = "SegmentReader: TryGetFieldLengths Returns True For Indexed Field")]
    public void TryGetFieldLengths_IndexedField_ReturnsTrue()
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
            Assert.True(reader.TryGetFieldLengths("body", out var lengths));
            Assert.NotNull(lengths);
            Assert.True(lengths.Length > 0);
        }
    }

    [Fact(DisplayName = "SegmentReader: TryGetFieldLengths Returns False For Missing Field")]
    public void TryGetFieldLengths_MissingField_ReturnsFalse()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.False(reader.TryGetFieldLengths("missing", out _));
        }
    }

    // GetDocIds / GetDocFreq

    [Fact(DisplayName = "SegmentReader: GetDocIds Returns DocIds For Present Term")]
    public void GetDocIds_PresentTerm_ReturnsDocIds()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "apple banana cherry"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var ids = reader.GetDocIds("body", "banana");
            Assert.NotEmpty(ids);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetDocIds Returns Empty For Missing Term")]
    public void GetDocIds_MissingTerm_ReturnsEmpty()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "apple banana"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var ids = reader.GetDocIds("body", "xyz_not_there");
            Assert.Empty(ids);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetDocFreq Returns Positive For Present Term")]
    public void GetDocFreq_PresentTerm_ReturnsPositive()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "delta echo foxtrot"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            int freq = reader.GetDocFreq("body", "delta");
            Assert.True(freq > 0);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetDocFreq Returns Zero For Missing Term")]
    public void GetDocFreq_MissingTerm_ReturnsZero()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "alpha beta"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Equal(0, reader.GetDocFreq("body", "zzz_missing"));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetDocFreq Uses Term Offset Cache On Repeat Lookup")]
    public void GetDocFreq_RepeatedLookup_UsesTermOffsetCache()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "cacheable term"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            long hitsBefore = reader.TermOffsetCacheHits;

            Assert.Equal(1, reader.GetDocFreq("body", "cacheable"));
            Assert.Equal(1, reader.TermOffsetCacheCount);
            Assert.Equal(1, reader.GetDocFreq("body", "cacheable"));

            Assert.True(reader.TermOffsetCacheHits > hitsBefore);
            Assert.Equal(1, reader.TermOffsetCacheCount);
        }
    }

    [Fact(DisplayName = "SegmentReader: HasDeletions True When Delete File Exists")]
    public void HasDeletions_DeleteFileExists_ReturnsTrueAndMarksDocNotLive()
    {
        var mmap = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var victim = new LeanDocument();
            victim.Add(new StringField("id", "victim"));
            victim.Add(new TextField("body", "delete me"));
            writer.AddDocument(victim);

            var survivor = new LeanDocument();
            survivor.Add(new StringField("id", "survivor"));
            survivor.Add(new TextField("body", "keep me"));
            writer.AddDocument(survivor);

            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "victim"));
            writer.Commit();
        }

        using (mmap)
        using (var searcher = new IndexSearcher(mmap))
        {
            var reader = searcher.GetSegmentReaders()[0];

            Assert.True(reader.HasDeletions);
            Assert.False(reader.IsLive(0));
            Assert.True(reader.IsLive(1));
        }
    }

    // MaxDoc

    [Fact(DisplayName = "SegmentReader: MaxDoc Equals Documents Written")]
    public void MaxDoc_EqualsDocumentsWritten()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"doc {i}"));
                w.AddDocument(doc);
            }
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Equal(5, reader.MaxDoc);
        }
    }

    // HasTermVectors / GetTermVectors

    [Fact(DisplayName = "SegmentReader: HasTermVectors Returns False When Not Stored")]
    public void HasTermVectors_NotStored_ReturnsFalse()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "some text"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.False(reader.HasTermVectors);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetTermVectors Returns Null When Not Stored")]
    public void GetTermVectors_NotStored_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "some text"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetTermVectors(0));
        }
    }

    // GetParentBitSet

    [Fact(DisplayName = "SegmentReader: GetParentBitSet Returns Null When No Parent File")]
    public void GetParentBitSet_NoPbsFile_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "just a doc"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetParentBitSet());
        }
    }

    // Info

    [Fact(DisplayName = "SegmentReader: Info Returns The Correct SegmentInfo")]
    public void Info_ReturnsCorrectSegmentInfo()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "test document"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.NotNull(reader.Info.SegmentId);
            Assert.Equal(1, reader.Info.DocCount);
        }
    }
}
