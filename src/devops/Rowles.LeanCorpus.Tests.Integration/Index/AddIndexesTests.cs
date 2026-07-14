using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Tests for <see cref="IndexWriter.AddIndexes"/>, which imports all segments
/// from a foreign directory into the current index.
/// </summary>
[Trait("Category", "Index")]
public sealed class AddIndexesTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public AddIndexesTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Imports a single-segment index and verifies all documents are searchable.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Imports Single Segment With Text Fields")]
    public void AddIndexes_ImportsSingleSegment_WithTextFields()
    {
        var srcDir = new MMapDirectory(SubDir("add_single_src"));
        var tgtDir = new MMapDirectory(SubDir("add_single_tgt"));

        // Build source index.
        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 100; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document number {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // Import into target.
        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        // Verify.
        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var all = searcher.Search(new MatchAllDocsQuery(), 200);
        Assert.Equal(100, all.TotalHits);

        var results = searcher.Search(new TermQuery("body", "document"), 10);
        Assert.Equal(100, results.TotalHits);
        foreach (var sd in results.ScoreDocs)
            Assert.True(sd.DocId is >= 0 and < 100);
    }

    /// <summary>
    /// Imports multiple segments from a source index that was committed multiple times.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Imports Multiple Source Segments")]
    public void AddIndexes_ImportsMultipleSourceSegments()
    {
        var srcDir = new MMapDirectory(SubDir("add_multi_seg_src"));
        var tgtDir = new MMapDirectory(SubDir("add_multi_seg_tgt"));

        // Build source with multiple commits to create multiple segments.
        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig { MaxBufferedDocs = 10 }))
        {
            for (int i = 0; i < 50; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"batch1 doc {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();

            for (int i = 0; i < 30; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"batch2 doc {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // Import.
        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var all = searcher.Search(new MatchAllDocsQuery(), 200);
        Assert.Equal(80, all.TotalHits);

        var batch1 = searcher.Search(new TermQuery("body", "batch1"), 100);
        Assert.Equal(50, batch1.TotalHits);

        var batch2 = searcher.Search(new TermQuery("body", "batch2"), 100);
        Assert.Equal(30, batch2.TotalHits);
    }

    /// <summary>
    /// Scores should be preserved across the import — docs with higher term frequency rank higher.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Scoring Is Preserved After Import")]
    public void AddIndexes_ScoringIsPreservedAfterImport()
    {
        var srcDir = new MMapDirectory(SubDir("add_score_src"));
        var tgtDir = new MMapDirectory(SubDir("add_score_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            var d0 = new LeanDocument();
            d0.Add(new TextField("body", "rare common"));
            writer.AddDocument(d0);

            var d1 = new LeanDocument();
            d1.Add(new TextField("body", "rare rare rare common"));
            writer.AddDocument(d1);

            var d2 = new LeanDocument();
            d2.Add(new TextField("body", "rare rare common"));
            writer.AddDocument(d2);

            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var results = searcher.Search(new TermQuery("body", "rare"), 10);
        Assert.Equal(3, results.TotalHits);

        // Doc 1 (freq 3) > Doc 2 (freq 2) > Doc 0 (freq 1).
        for (int i = 1; i < results.ScoreDocs.Length; i++)
            Assert.True(results.ScoreDocs[i - 1].Score >= results.ScoreDocs[i].Score,
                $"Score at {i - 1} ({results.ScoreDocs[i - 1].Score}) should be >= score at {i} ({results.ScoreDocs[i].Score})");
    }

    /// <summary>
    /// Doc values (numeric via NumericField, sorted via StringField) survive the import.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: DocValues Survive Import")]
    public void AddIndexes_DocValuesSurviveImport()
    {
        var srcDir = new MMapDirectory(SubDir("add_dv_src"));
        var tgtDir = new MMapDirectory(SubDir("add_dv_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 20; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"item {i}"));
                doc.Add(new NumericField("price", i * 1.5));
                doc.Add(new StringField("category", i % 3 == 0 ? "alpha" : "beta"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var all = searcher.Search(new MatchAllDocsQuery(), 50);
        Assert.Equal(20, all.TotalHits);

        // Numeric range query proof that doc values and BKD survive.
        var priceResults = searcher.Search(new RangeQuery("price", 0.0, 5.0), 50);
        Assert.Equal(4, priceResults.TotalHits); // prices 0, 1.5, 3.0, 4.5

        // Sorted doc values: aggregate on category.
        var catResults = searcher.Search(new TermQuery("category", "alpha"), 50);
        Assert.Equal(7, catResults.TotalHits); // i % 3 == 0 for i=0,3,6,9,12,15,18 = 7 docs
    }

    /// <summary>
    /// Stored fields survive the import.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Stored Fields Survive Import")]
    public void AddIndexes_StoredFieldsSurviveImport()
    {
        var srcDir = new MMapDirectory(SubDir("add_stored_src"));
        var tgtDir = new MMapDirectory(SubDir("add_stored_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            doc.Add(new StoredField("payload", "secret-value-42"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var results = searcher.Search(new TermQuery("body", "hello"), 10);
        Assert.Equal(1, results.TotalHits);

        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.NotNull(stored);
        Assert.True(stored.TryGetValue("payload", out var values));
        Assert.Equal("secret-value-42", values[0]);
    }

    /// <summary>
    /// Deleted documents in the source are not imported.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Deleted Docs Are Excluded From Import")]
    public void AddIndexes_DeletedDocsExcludedFromImport()
    {
        var srcDir = new MMapDirectory(SubDir("add_del_src"));
        var tgtDir = new MMapDirectory(SubDir("add_del_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 10; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"keep {i}"));
                doc.Add(new StringField("id", $"doc-{i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("id", "doc-3"));
            writer.DeleteDocuments(new TermQuery("id", "doc-7"));
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var results = searcher.Search(new TermQuery("body", "keep"), 20);
        Assert.Equal(8, results.TotalHits);
    }

    /// <summary>
    /// Importing into a non-empty target merges correctly.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Merges Into Non-Empty Target")]
    public void AddIndexes_MergesIntoNonEmptyTarget()
    {
        var srcDir = new MMapDirectory(SubDir("add_merge_src"));
        var tgtDir = new MMapDirectory(SubDir("add_merge_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "source document"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "target document"));
            writer.AddDocument(doc);
            writer.Commit();

            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var all = searcher.Search(new MatchAllDocsQuery(), 10);
        Assert.Equal(2, all.TotalHits);

        var srcResults = searcher.Search(new TermQuery("body", "source"), 10);
        Assert.Equal(1, srcResults.TotalHits);

        var tgtResults = searcher.Search(new TermQuery("body", "target"), 10);
        Assert.Equal(1, tgtResults.TotalHits);
    }

    /// <summary>
    /// Field lengths and norms from the source produce correct scoring in the target.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Field Length Norms Affect Scoring After Import")]
    public void AddIndexes_FieldLengthNormsAffectScoringAfterImport()
    {
        var srcDir = new MMapDirectory(SubDir("add_norms_src"));
        var tgtDir = new MMapDirectory(SubDir("add_norms_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            // Short doc — "rare" has higher weight.
            var shortDoc = new LeanDocument();
            shortDoc.Add(new TextField("body", "rare"));
            writer.AddDocument(shortDoc);

            // Long doc — "rare" has lower weight.
            var longDoc = new LeanDocument();
            longDoc.Add(new TextField("body", $"rare {new string('x', 200)}"));
            writer.AddDocument(longDoc);

            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var results = searcher.Search(new TermQuery("body", "rare"), 10);
        Assert.Equal(2, results.TotalHits);
        // The short doc should score higher than the long doc.
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score,
            "Short document should score higher than long document for the same term.");
    }

    /// <summary>
    /// Two separate source indexes imported sequentially both appear in the target.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Sequential Imports From Two Sources")]
    public void AddIndexes_SequentialImportsFromTwoSources()
    {
        var srcA = new MMapDirectory(SubDir("add_two_a_src"));
        var srcB = new MMapDirectory(SubDir("add_two_b_src"));
        var tgtDir = new MMapDirectory(SubDir("add_two_tgt"));

        using (var writer = new IndexWriter(srcA, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "alpha source"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using (var writer = new IndexWriter(srcB, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "beta source"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcA);
            writer.AddIndexes(srcB);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var all = searcher.Search(new MatchAllDocsQuery(), 10);
        Assert.Equal(2, all.TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "alpha"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "beta"), 10).TotalHits);
    }

    /// <summary>
    /// Verifies that term positions survive the import (phrase queries still work).
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Phrase Queries Work After Import")]
    public void AddIndexes_PhraseQueriesWorkAfterImport()
    {
        var srcDir = new MMapDirectory(SubDir("add_phrase_src"));
        var tgtDir = new MMapDirectory(SubDir("add_phrase_tgt"));

        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "the quick brown fox jumps over the lazy dog"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var phraseResults = searcher.Search(
            new PhraseQuery("body", "quick", "brown", "fox"), 10);
        Assert.Equal(1, phraseResults.TotalHits);
    }

    /// <summary>
    /// Importing 500 documents with doc values and then searching verifies correctness at scale.
    /// </summary>
    [Fact(DisplayName = "AddIndexes: Large Import With DocValues Is Correct")]
    public void AddIndexes_LargeImportWithDocValues_IsCorrect()
    {
        var srcDir = new MMapDirectory(SubDir("add_large_src"));
        var tgtDir = new MMapDirectory(SubDir("add_large_tgt"));

        const int n = 500;
        using (var writer = new IndexWriter(srcDir, new IndexWriterConfig { MaxBufferedDocs = 100 }))
        {
            for (int i = 0; i < n; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"item {i}"));
                doc.Add(new NumericField("val", i));
                doc.Add(new StringField("kind", i % 2 == 0 ? "even" : "odd"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using (var writer = new IndexWriter(tgtDir, new IndexWriterConfig()))
        {
            writer.AddIndexes(srcDir);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(tgtDir, new IndexSearcherConfig());
        var all = searcher.Search(new MatchAllDocsQuery(), n + 10);
        Assert.Equal(n, all.TotalHits);

        // Verify a specific doc is findable via numeric range.
        var spotCheck = searcher.Search(new RangeQuery("val", 42.0, 42.0), 10);
        Assert.Equal(1, spotCheck.TotalHits);

        // Doc values: range query should find docs 0-99.
        var rangeResults = searcher.Search(new RangeQuery("val", 0.0, 99.0), n);
        Assert.Equal(100, rangeResults.TotalHits);
    }
}
