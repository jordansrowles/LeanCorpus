using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Search;

/// <summary>
/// Chaos coverage for the added query families.
/// </summary>
[Trait("Category", "Chaos")]
[Trait("Category", "Search")]
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
            FlipByte(bkdFile, Math.Min(16, bkdLen - 1));

            using var searcher = new IndexSearcher(directory);
            var results = searcher.Search(new PointInSetQuery("price", 10.0, 30.0), 10);
            Assert.Equal(2, results.TotalHits);
        }
        finally
        {
            ReleaseFileHandles();
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
                    var results = searcher.Search(new FieldExistsQuery("note"), 10);
                    _ = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
                });
            }
        }
        finally
        {
            ReleaseFileHandles();
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
                    }), 10);
                });
            }
        }
        finally
        {
            ReleaseFileHandles();
            DeleteDirectoryBestEffort(path);
        }
    }

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

    private static void ReleaseFileHandles()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string CreateIsolatedPath(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "LeanCorpusQueryChaos", $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt >= 4)
                    break;
                Thread.Sleep(100);
                ReleaseFileHandles();
            }
            catch (IOException)
            {
                if (attempt >= 4)
                    break;
                Thread.Sleep(100);
                ReleaseFileHandles();
            }
        }

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}
