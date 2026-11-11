using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Chaos coverage for the added query families.
/// </summary>
[Category(TestCategory.Chaos)]
[Area(TestArea.Search)]
public sealed class QueryFamilyChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    public QueryFamilyChaosTests(ChaosDirectoryFixture fixture)
    {
    }

    [Fact(DisplayName = "PointInSetQuery Chaos: corrupt BKD still falls back to numeric index")]
    public void PointInSetQuery_CorruptBkd_StillFallsBackToNumericIndex()
    {
        var path = CreateIsolatedPath("query_chaos_bkd");
        try
        {
            using var directory = BuildNumericIndex(path);
            var bkdFile = Directory.GetFiles(path, "*.bkd").Single();
            var bkdLen = (int)new FileInfo(bkdFile).Length;
            Assert.True(bkdLen > 16);
            FlipByte(bkdFile, bkdLen - 17);

            using var searcher = new IndexSearcher(directory);
            var results = searcher.Search(new PointInSetQuery("price", 10.0, 30.0), 10, TestContext.Current.CancellationToken);
            Assert.Equal(2, results.TotalHits);
        }
        finally
        {
            DeleteDirectoryBestEffort(path);
        }
    }

    [Fact(DisplayName = "FieldExistsQuery Chaos: corrupt stored fields fail explicitly")]
    public void FieldExistsQuery_CorruptStoredFields_FailsExplicitly()
    {
        var path = CreateIsolatedPath("query_chaos_fdt");
        try
        {
            using (var directory = BuildStoredOnlyIndex(path))
            {
                var storedFile = Directory.GetFiles(path, "*.fdt").Single();
                TruncateFile(storedFile, 1);

                Assert.ThrowsAny<Exception>(() =>
                {
                    using var searcher = new IndexSearcher(directory);
                    var results = searcher.Search(new FieldExistsQuery("note"), 10, TestContext.Current.CancellationToken);
                    _ = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
                });
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(path);
        }
    }

    [Fact(DisplayName = "MultiPhraseQuery Chaos: corrupt postings fail explicitly")]
    public void MultiPhraseQuery_CorruptPostings_FailsExplicitly()
    {
        var path = CreateIsolatedPath("query_chaos_pos");
        try
        {
            using (var directory = BuildPositionalIndex(path))
            {
                var posFile = Directory.GetFiles(path, "*.pos").Single();
                FlipByte(posFile, 0);

                Assert.ThrowsAny<Exception>(() =>
                {
                    using var searcher = new IndexSearcher(directory);
                    _ = searcher.Search(new MultiPhraseQuery("body", new[]
                    {
                        new[] { "quick", "fast" },
                        new[] { "brown" }
                    }), 10, TestContext.Current.CancellationToken);
                });
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(path);
        }
    }

    [Fact(DisplayName = "PhraseQuery Chaos: results and scores are invariant across segments and merge")]
    public void PhraseQuery_ResultsAndScores_AreInvariantAcrossSegmentsAndMerge()
    {
        var path = CreateIsolatedPath("phrase_topology");
        try
        {
            using var directory = new MMapDirectory(path);
            var documents = new (string Id, string Body)[]
            {
                ("doc-0", "aa bb cc aa bb"),
                ("doc-1", "aa xx bb cc"),
                ("doc-2", "aa bb xx cc"),
                ("doc-3", "aa yy bb zz cc"),
                ("doc-4", "cc bb aa"),
                ("doc-5", "aa bb"),
                ("doc-6", "xx yy zz")
            };

            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                MergePolicy = NoMergePolicy.Instance
            }))
            {
                foreach (var (id, body) in documents)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", id));
                    document.Add(new TextField("body", body));
                    writer.AddDocument(document);
                    writer.Commit();
                }

                Assert.Equal(documents.Length, writer.GetNrtSegments().Count);
            }

            PhraseQuery[] queries =
            [
                new PhraseQuery("body", "aa", "bb"),
                new PhraseQuery("body", 2, "aa", "bb"),
                new PhraseQuery("body", 2, "aa", "bb", "cc")
            ];
            PhraseSearchSnapshot[] segmentedResults = queries.Select(query => SearchPhrase(directory, query)).ToArray();

            using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
            {
                Assert.True(writer.ForceMerge(1) > 0);
                writer.Commit();
                Assert.Single(writer.GetNrtSegments());
            }

            PhraseSearchSnapshot[] mergedResults = queries.Select(query => SearchPhrase(directory, query)).ToArray();
            for (int i = 0; i < queries.Length; i++)
            {
                Assert.Equal(segmentedResults[i].TotalHits, mergedResults[i].TotalHits);
                Assert.Equal(segmentedResults[i].Hits, mergedResults[i].Hits);
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(path);
        }
    }

    private static PhraseSearchSnapshot SearchPhrase(MMapDirectory directory, PhraseQuery query)
    {
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(query, 100, TestContext.Current.CancellationToken);
        var hits = results.ScoreDocs
            .Select(scoreDoc => new PhraseHit(
                searcher.GetStoredFields(scoreDoc.DocId)["id"][0],
                scoreDoc.Score))
            .OrderBy(static hit => hit.Id, StringComparer.Ordinal)
            .ToArray();

        return new PhraseSearchSnapshot(results.TotalHits, hits);
    }

    private sealed record PhraseSearchSnapshot(int TotalHits, PhraseHit[] Hits);

    private sealed record PhraseHit(string Id, float Score);

    private static MMapDirectory BuildNumericIndex(string path)
    {
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        foreach (double price in new[] { 10.0, 20.0, 30.0 })
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("price", price));
            writer.AddDocument(doc);
        }

        writer.Commit();
        return directory;
    }

    private static MMapDirectory BuildStoredOnlyIndex(string path)
    {
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new StringField("id", "stored"));
        doc.Add(new StoredField("note", "present"));
        writer.AddDocument(doc);
        writer.Commit();
        return directory;
    }

    private static MMapDirectory BuildPositionalIndex(string path)
    {
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        foreach (string body in new[] { "quick brown fox", "fast brown fox" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", body));
            writer.AddDocument(doc);
        }

        writer.Commit();
        return directory;
    }

    private static void FlipByte(string path, long offset)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset;
        int value = stream.ReadByte();
        Assert.NotEqual(-1, value);
        stream.Position = offset;
        stream.WriteByte((byte)(value ^ 0x5A));
    }

    private static void TruncateFile(string path, long length)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.SetLength(length);
    }

    private static string CreateIsolatedPath(string name)
    {
        var path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("tests"), "LeanCorpusQueryChaos", $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        TestDirectoryFixture.TryDeleteDirectory(path);
    }
}
