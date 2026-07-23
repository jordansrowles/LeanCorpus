using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

[Trait("Category", "Search")]
public sealed class LazySegmentReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "ll_lazy_" + Guid.NewGuid().ToString("N"));

    [Fact(DisplayName = "Index Searcher: 1500 Segments Open Without Loading Heavy Reader States")]
    public void Constructor_ManyMinimalSegments_LoadsNoHeavyReaderStates()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        }))
        {
            for (int i = 0; i < 1_500; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "shared", stored: false));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            MaxCachedSegmentReaders = 32,
        });

        Assert.Equal(1_500, searcher.GetSegmentReaders().Count);
        Assert.Equal(0, searcher.CachedSegmentReaderCount);
        Assert.Equal(0, searcher.LoadedSegmentReaderCount);
    }

    [Fact(DisplayName = "Index Searcher: Rejects Segment Reader Cache Capacity Below One")]
    public void Constructor_InvalidCacheCapacity_Throws()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        var config = new IndexSearcherConfig { MaxCachedSegmentReaders = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexSearcher(directory, config));
    }

    [Fact(DisplayName = "Index Searcher: Eviction Preserves Query Results Scores Fields DocValues Vectors And Deletions")]
    public void Eviction_AcrossQueryFamilies_PreservesResultsAndValues()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxBufferedDocs = 3,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        }))
        {
            for (int i = 0; i < 9; i++)
            {
                var document = new LeanDocument();
                string body = (i % 3) switch
                {
                    0 => "alpha beta",
                    1 => "alpha gamma",
                    _ => "delta",
                };
                document.Add(new TextField("body", body, stored: true));
                document.Add(new StringField("category", "group", stored: true));
                document.Add(new NumericField("price", i, stored: true));
                document.Add(new VectorField("vector", new ReadOnlyMemory<float>([1f, i / 10f])));
                writer.AddDocument(document);
            }
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("body", "delta"));
            writer.Commit();
        }

        using var bounded = new IndexSearcher(directory, new IndexSearcherConfig
        {
            MaxCachedSegmentReaders = 1,
            ParallelSearch = false,
        });
        using var retained = new IndexSearcher(directory, new IndexSearcherConfig
        {
            MaxCachedSegmentReaders = 16,
            ParallelSearch = false,
        });

        Query[] queries =
        [
            new TermQuery("body", "alpha"),
            new PhraseQuery("body", "alpha", "beta"),
            new WildcardQuery("body", "*pha*"),
            new FuzzyQuery("body", "alpa"),
            new RegexpQuery("body", "alp.*"),
            new BooleanQuery.Builder()
                .Add(new TermQuery("category", "group"), Occur.Must)
                .Add(new WildcardQuery("body", "*alpha*"), Occur.Must)
                .Build(),
            new RangeQuery("price", 2, 7),
            new VectorQuery("vector", [1f, 0f], topK: 5),
            new TermQuery("body", "delta"),
        ];

        foreach (var query in queries)
            AssertTopDocsEqual(retained.Search(query, 20), bounded.Search(query, 20));

        for (int docId = 0; docId < 9; docId++)
            Assert.Equal(retained.GetStoredFields(docId), bounded.GetStoredFields(docId));

        var retainedReaders = retained.GetSegmentReaders();
        var boundedReaders = bounded.GetSegmentReaders();
        for (int segment = 0; segment < retainedReaders.Count; segment++)
        {
            for (int docId = 0; docId < retainedReaders[segment].MaxDoc; docId++)
            {
                bool expectedFound = retainedReaders[segment].TryGetNumericValue("price", docId, out double expected);
                bool actualFound = boundedReaders[segment].TryGetNumericValue("price", docId, out double actual);
                Assert.Equal(expectedFound, actualFound);
                Assert.Equal(expected, actual);
            }
        }

        Assert.True(bounded.LoadedSegmentReaderCount > bounded.GetSegmentReaders().Count);
        Assert.InRange(bounded.CachedSegmentReaderCount, 0, 1);
    }

    private static void AssertTopDocsEqual(TopDocs expected, TopDocs actual)
    {
        Assert.Equal(expected.TotalHits, actual.TotalHits);
        Assert.Equal(expected.ScoreDocs.Length, actual.ScoreDocs.Length);
        for (int i = 0; i < expected.ScoreDocs.Length; i++)
        {
            Assert.Equal(expected.ScoreDocs[i].DocId, actual.ScoreDocs[i].DocId);
            Assert.Equal(expected.ScoreDocs[i].Score, actual.ScoreDocs[i].Score);
        }
    }

    [Fact(DisplayName = "Index Searcher: Old Snapshot Survives Merge From Separate Directory Instance")]
    public void OldSnapshot_SeparateWriterCompacts_RemainsCorrectUntilReleased()
    {
        Directory.CreateDirectory(_path);
        using (var indexingDirectory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(indexingDirectory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        }))
        {
            for (int i = 0; i < 8; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "stable", stored: true));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var readerDirectory = new MMapDirectory(_path);
        var oldSearcher = new IndexSearcher(readerDirectory, new IndexSearcherConfig
        {
            MaxCachedSegmentReaders = 2,
        });
        var oldSegmentIds = oldSearcher.GetSegmentReaders()
            .Select(static reader => reader.Info.SegmentId)
            .ToArray();
        var expected = oldSearcher.Search(new TermQuery("body", "stable"), 20);

        using (var writerDirectory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(writerDirectory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            DurableCommits = false,
        }))
        {
            writer.Compact();
        }

        Assert.Equal(8, oldSearcher.Search(new TermQuery("body", "stable"), 20).TotalHits);
        Assert.Contains(oldSegmentIds, id => File.Exists(Path.Combine(_path, id + ".seg")));
        using (var refreshed = new IndexSearcher(new MMapDirectory(_path)))
            AssertTopDocsEqual(expected, refreshed.Search(new TermQuery("body", "stable"), 20));

        oldSearcher.Dispose();
        Assert.All(oldSegmentIds, id => Assert.False(File.Exists(Path.Combine(_path, id + ".seg"))));
    }

    public void Dispose()
    {
        try { Directory.Delete(_path, recursive: true); }
        catch { }
    }
}
