using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Correctness tests for delete + merge interaction:
/// verifies that deleted documents are properly excluded after segment merges.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Merge")]
public sealed class MergeCorrectnessTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MergeCorrectnessTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Verifies the Delete In Multiple Segments: Then Merge All Deleted Docs Gone scenario.
    /// </summary>
    [Fact(DisplayName = "Delete In Multiple Segments: Then Merge All Deleted Docs Gone")]
    public void DeleteInMultipleSegments_ThenMerge_AllDeletedDocsGone()
    {
        // Arrange: create many 1-doc segments to trigger tiered merge (threshold=10),
        // delete specific docs, then add enough commits to trigger merge.
        var dir = new MMapDirectory(SubDir("merge_multi_delete"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 1 };

        using (var writer = new IndexWriter(dir, config))
        {
            // Create 12 single-doc segments, each auto-flushed
            for (int i = 0; i < 12; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new TextField("body", "common content here"));
                writer.AddDocument(doc);
            }
            writer.Commit();
            _output.WriteLine("Committed 12 single-doc segments");

            // Delete two docs
            writer.DeleteDocuments(new TermQuery("id", "doc3"));
            writer.DeleteDocuments(new TermQuery("id", "doc7"));
            writer.Commit();
            _output.WriteLine("Deleted doc3 and doc7, merge should be triggered");

            // Another commit to allow background merge to settle
            Thread.Sleep(500);
            writer.Commit();
        }

        // Assert: search should find 10 docs, not 12
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "common"), 20);

        _output.WriteLine($"After delete+merge: {results.TotalHits} docs match 'common'");
        Assert.Equal(10, results.TotalHits);

        // Verify specific deletions
        var deletedA = searcher.Search(new TermQuery("id", "doc3"), 10);
        var deletedB = searcher.Search(new TermQuery("id", "doc7"), 10);
        _output.WriteLine($"  doc3 hits: {deletedA.TotalHits} (expected 0)");
        _output.WriteLine($"  doc7 hits: {deletedB.TotalHits} (expected 0)");
        Assert.Equal(0, deletedA.TotalHits);
        Assert.Equal(0, deletedB.TotalHits);
    }

    /// <summary>
    /// Verifies the Delete All Docs In One Group: After Merge Only Kept Docs Remain scenario.
    /// </summary>
    [Fact(DisplayName = "Delete All Docs In One Group: After Merge Only Kept Docs Remain")]
    public void DeleteAllDocsInOneGroup_AfterMerge_OnlyKeptDocsRemain()
    {
        var dir = new MMapDirectory(SubDir("merge_all_deleted"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 1 };

        using (var writer = new IndexWriter(dir, config))
        {
            // Create 6 disposable docs and 6 keeper docs (interleaved, 12 segments)
            for (int i = 0; i < 12; i++)
            {
                var doc = new LeanDocument();
                bool disposable = i < 6;
                doc.Add(new TextField("group", disposable ? "disposable" : "keeper"));
                doc.Add(new TextField("body", "content here"));
                writer.AddDocument(doc);
            }
            writer.Commit();
            _output.WriteLine("Committed 12 single-doc segments (6 disposable, 6 keeper)");

            // Delete ALL disposable docs
            writer.DeleteDocuments(new TermQuery("group", "disposable"));
            writer.Commit();
            _output.WriteLine("Deleted all 'disposable' docs, merge should be triggered");

            // Let background merge settle
            Thread.Sleep(500);
            writer.Commit();
        }

        // Assert: only keeper docs survive
        using var searcher = new IndexSearcher(dir);
        var keeperResults = searcher.Search(new TermQuery("group", "keeper"), 10);
        var disposableResults = searcher.Search(new TermQuery("group", "disposable"), 10);

        _output.WriteLine($"After merge: keeper={keeperResults.TotalHits}, disposable={disposableResults.TotalHits}");
        Assert.Equal(6, keeperResults.TotalHits);
        Assert.Equal(0, disposableResults.TotalHits);
    }

    /// <summary>
    /// Verifies the Delete And Commit: Stored Fields Surviving Docs Accessible scenario.
    /// </summary>
    [Fact(DisplayName = "Delete And Commit: Stored Fields Surviving Docs Accessible")]
    public void DeleteAndCommit_StoredFields_SurvivingDocsAccessible()
    {
        var dir = new MMapDirectory(SubDir("merge_storedfields"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 2 };

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 6; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new TextField("body", $"content for doc {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();

            // Delete doc1 and doc4
            writer.DeleteDocuments(new TermQuery("id", "doc1"));
            writer.DeleteDocuments(new TermQuery("id", "doc4"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // Verify surviving doc stored fields are accessible
        var doc0Results = searcher.Search(new TermQuery("id", "doc0"), 10);
        Assert.Equal(1, doc0Results.TotalHits);
        var stored = searcher.GetStoredFields(doc0Results.ScoreDocs[0].DocId);
        _output.WriteLine($"Stored fields for doc0: id={stored["id"][0]}");
        Assert.Equal("doc0", stored["id"][0]);

        // Verify deleted docs are truly gone
        Assert.Equal(0, searcher.Search(new TermQuery("id", "doc1"), 10).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "doc4"), 10).TotalHits);

        // Verify remaining docs
        foreach (var id in new[] { "doc0", "doc2", "doc3", "doc5" })
        {
            var r = searcher.Search(new TermQuery("id", id), 10);
            _output.WriteLine($"  {id} → {r.TotalHits} hits");
            Assert.Equal(1, r.TotalHits);
        }
    }
}
