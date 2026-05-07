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

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Search.
/// </summary>
[Trait("Category", "Search")]
public sealed class SearchTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SearchTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Verifies the Term Query: Exact Match Returns Correct Doc IDs scenario.
    /// </summary>
    [Fact(DisplayName = "Term Query: Exact Match Returns Correct Doc IDs")]
    public void TermQuery_ExactMatch_ReturnsCorrectDocIds()
    {
        var dir = new MMapDirectory(SubDir("term_exact"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", i == 2 || i == 4 ? "lucene rocks" : "other text"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "lucene"), 10);

        Assert.Equal(2, results.TotalHits);
        var docIds = results.ScoreDocs.Select(sd => sd.DocId).OrderBy(id => id).ToArray();
        Assert.Equal(new[] { 2, 4 }, docIds);
    }

    /// <summary>
    /// Verifies the Term Query: No Match Returns Empty Results scenario.
    /// </summary>
    [Fact(DisplayName = "Term Query: No Match Returns Empty Results")]
    public void TermQuery_NoMatch_ReturnsEmptyResults()
    {
        var dir = new MMapDirectory(SubDir("term_nomatch"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "some existing content"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "nonexistent"), 10);
        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Boolean Query: Must And Requires Both Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Query: Must And Requires Both Terms")]
    public void BooleanQuery_MustAnd_RequiresBothTerms()
    {
        var dir = new MMapDirectory(SubDir("bool_must"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "fast search", "fast indexing", "slow search", "fast search engine" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "fast"), Occur.Must);
        query.Add(new TermQuery("body", "search"), Occur.Must);
        var results = searcher.Search(query, 10);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Boolean Query: Must Pattern Clause Matches Analysed Term scenario.
    /// </summary>
    /// <param name="queryType">The queryType value for the test case.</param>
    /// <param name="pattern">The pattern value for the test case.</param>
    [Theory(DisplayName = "Boolean Query: Must Pattern Clause Matches Analysed Term")]
    [InlineData("prefix", "wor")]
    [InlineData("wildcard", "wor*")]
    [InlineData("wildcard", "*or*")]
    public void BooleanQuery_MustPatternClause_MatchesAnalysedTerm(string queryType, string pattern)
    {
        var pathPattern = pattern.Replace("*", "star", StringComparison.Ordinal);
        var dir = new MMapDirectory(SubDir($"bool_must_{queryType}_{pathPattern}"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("title", "hello world"));
        doc.Add(new StringField("id", "1"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        Query clause = queryType == "prefix"
            ? new PrefixQuery("title", pattern)
            : new WildcardQuery("title", pattern);
        var query = new BooleanQuery();
        query.Add(clause, Occur.Must);
        var results = searcher.Search(query, 10);

        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Stored Parameter: False Indexes Without Storing Field Values scenario.
    /// </summary>
    [Fact(DisplayName = "Stored Parameter: False Indexes Without Storing Field Values")]
    public void StoredParameter_False_IndexesWithoutStoringFieldValues()
    {
        var dir = new MMapDirectory(SubDir("stored_false"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("title", "hello world", stored: false));
        doc.Add(new StringField("id", "1", stored: false));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("title", "world"), 10);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);

        Assert.Equal(1, results.TotalHits);
        Assert.False(stored.ContainsKey("title"));
        Assert.False(stored.ContainsKey("id"));
    }

    /// <summary>
    /// Verifies the Stored Field: Stores Value Without Indexing scenario.
    /// </summary>
    [Fact(DisplayName = "Stored Field: Stores Value Without Indexing")]
    public void StoredField_StoresValueWithoutIndexing()
    {
        var dir = new MMapDirectory(SubDir("stored_only"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "findable text", stored: false));
        doc.Add(new StoredField("title", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "findable"), 10);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        var storedOnlyResults = searcher.Search(new TermQuery("title", "hello world"), 10);

        Assert.Equal(1, results.TotalHits);
        Assert.Equal("hello world", stored["title"][0]);
        Assert.Equal(0, storedOnlyResults.TotalHits);
    }

    /// <summary>
    /// Verifies the Boolean Query: Should Or Returns Union scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Query: Should Or Returns Union")]
    public void BooleanQuery_ShouldOr_ReturnsUnion()
    {
        var dir = new MMapDirectory(SubDir("bool_should"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "alpha one", "beta two", "alpha beta", "beta three" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Should);
        query.Add(new TermQuery("body", "beta"), Occur.Should);
        var results = searcher.Search(query, 10);

        Assert.Equal(4, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Boolean Query: Must Not Excludes Documents scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Query: Must Not Excludes Documents")]
    public void BooleanQuery_MustNot_ExcludesDocuments()
    {
        var dir = new MMapDirectory(SubDir("bool_mustnot"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "search fast", "search slow", "search quick" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "search"), Occur.Must);
        query.Add(new TermQuery("body", "slow"), Occur.MustNot);
        var results = searcher.Search(query, 10);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Scorer: BM25 Higher Term Frequency Ranks Higher scenario.
    /// </summary>
    [Fact(DisplayName = "Scorer: BM25 Higher Term Frequency Ranks Higher")]
    public void Scorer_Bm25_HigherTermFrequencyRanksHigher()
    {
        var dir = new MMapDirectory(SubDir("bm25_tf"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var docA = new LeanDocument();
        docA.Add(new TextField("body", "performance is good"));
        writer.AddDocument(docA);

        var docB = new LeanDocument();
        docB.Add(new TextField("body", "performance performance performance performance performance"));
        writer.AddDocument(docB);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "performance"), 10);

        Assert.Equal(2, results.TotalHits);
        // Doc B (index 1) should rank higher due to higher term frequency
        Assert.Equal(1, results.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Index Searcher: Parallel Segments All Segments Contribute Results scenario.
    /// </summary>
    [Fact(DisplayName = "Index Searcher: Parallel Segments All Segments Contribute Results")]
    public void IndexSearcher_ParallelSegments_AllSegmentsContributeResults()
    {
        var dir = new MMapDirectory(SubDir("parallel_seg"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 1 };
        using var writer = new IndexWriter(dir, config);

        var uniqueTerms = new[] { "alpha", "bravo", "charlie", "delta" };
        foreach (var term in uniqueTerms)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", term));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        foreach (var term in uniqueTerms)
        {
            var results = searcher.Search(new TermQuery("body", term), 10);
            Assert.True(results.TotalHits >= 1,
                $"Expected at least 1 result for term '{term}', got {results.TotalHits}");
        }
    }

    /// <summary>
    /// Verifies the Segment Reader: Missing Norms File Throws Invalid Data Exception On Open scenario.
    /// </summary>
    [Fact(DisplayName = "Segment Reader: Missing Norms File Throws Invalid Data Exception On Open")]
    public void SegmentReader_MissingNormsFile_ThrowsInvalidDataExceptionOnOpen()
    {
        var dirPath = SubDir("segment_integrity");
        var dir = new MMapDirectory(dirPath);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "integrity check"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        File.Delete(System.IO.Path.Combine(dirPath, "seg_0.nrm"));
        var ex = Record.Exception(() =>
        {
            using var _ = new IndexSearcher(dir);
        });
        Assert.True(ex is InvalidDataException or FileNotFoundException);
    }

    /// <summary>
    /// Verifies the Range Query: Numeric Field Returns In Range Documents scenario.
    /// </summary>
    [Fact(DisplayName = "Range Query: Numeric Field Returns In Range Documents")]
    [Trait("Category", "Advanced")]
    public void RangeQuery_NumericField_ReturnsInRangeDocuments()
    {
        var dir = new MMapDirectory(SubDir("range_numeric"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var prices = new[] { 10, 25, 50, 75, 100 };
        foreach (var price in prices)
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("price", price));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RangeQuery("price", 20, 60), 10);
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Phrase Query: Ordered Terms Matches Exact Phrase scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: Ordered Terms Matches Exact Phrase")]
    [Trait("Category", "Advanced")]
    public void PhraseQuery_OrderedTerms_MatchesExactPhrase()
    {
        var dir = new MMapDirectory(SubDir("phrase_exact"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "the quick brown fox"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "brown quick fox"));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", "quick", "brown"), 10);
        Assert.Equal(1, results.TotalHits);
        Assert.Equal(0, results.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Index Stats: Cross Segment Scores Are Comparable scenario.
    /// </summary>
    [Fact(DisplayName = "Index Stats: Cross Segment Scores Are Comparable")]
    [Trait("Category", "Advanced")]
    public void IndexStats_CrossSegmentScores_AreComparable()
    {
        var dir = new MMapDirectory(SubDir("cross_seg_scores"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 2 };
        using var writer = new IndexWriter(dir, config);

        var docA = new LeanDocument();
        docA.Add(new TextField("body", "relevance relevance padding"));
        writer.AddDocument(docA);

        var docB = new LeanDocument();
        docB.Add(new TextField("body", "filler content material"));
        writer.AddDocument(docB);

        var docC = new LeanDocument();
        docC.Add(new TextField("body", "relevance topic alphabet"));
        writer.AddDocument(docC);

        var docD = new LeanDocument();
        docD.Add(new TextField("body", "extra filler text"));
        writer.AddDocument(docD);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "relevance"), 10);

        Assert.True(results.TotalHits >= 2);
        Assert.Equal(0, results.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Vector Query: Cosine Similarity Top K Returned scenario.
    /// </summary>
    [Fact(DisplayName = "Vector Query: Cosine Similarity Top K Returned")]
    [Trait("Category", "Phase2")]
    public void VectorQuery_CosineSimilarity_TopKReturned()
    {
        var dir = new MMapDirectory(SubDir("vector_cosine"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Three docs with different 4-d vectors
        float[][] vectors =
        [
            [1f, 0f, 0f, 0f],   // doc 0 — axis X
            [0f, 1f, 0f, 0f],   // doc 1 — axis Y
            [0.9f, 0.1f, 0f, 0f] // doc 2 — mostly X
        ];

        for (int i = 0; i < vectors.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"doc {i}"));
            doc.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vectors[i])));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery("embedding", [1f, 0f, 0f, 0f], topK: 2);
        var results = searcher.Search(query, 2);

        Assert.True(results.TotalHits >= 2);
        // Doc 0 is exact match (cos=1.0), doc 2 is close; doc 1 is orthogonal
        var topDocIds = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        Assert.Contains(0, topDocIds);
        Assert.Contains(2, topDocIds);
    }

    /// <summary>
    /// Verifies the Vector Query: Cosine Similarity Scores Descending scenario.
    /// </summary>
    [Fact(DisplayName = "Vector Query: Cosine Similarity Scores Descending")]
    [Trait("Category", "Phase2")]
    public void VectorQuery_CosineSimilarity_ScoresDescending()
    {
        var dir = new MMapDirectory(SubDir("vector_scores"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc1 = new LeanDocument();
        doc1.Add(new VectorField("vec", new ReadOnlyMemory<float>([0f, 1f, 0f])));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new VectorField("vec", new ReadOnlyMemory<float>([1f, 0f, 0f])));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new VectorQuery("vec", [1f, 0f, 0f], topK: 10), 10);

        Assert.Equal(2, results.TotalHits);
        Assert.True(results.ScoreDocs[0].Score >= results.ScoreDocs[1].Score);
        Assert.Equal(1, results.ScoreDocs[0].DocId); // exact match
    }

    /// <summary>
    /// Verifies the Cosine Similarity: Identical Vectors Returns One scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Identical Vectors Returns One")]
    [Trait("Category", "Phase2")]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] v = [1f, 2f, 3f, 4f];
        float sim = VectorQuery.CosineSimilarity(v, v);
        Assert.InRange(sim, 0.999f, 1.001f);
    }

    /// <summary>
    /// Verifies the Cosine Similarity: Orthogonal Vectors Returns Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Orthogonal Vectors Returns Zero")]
    [Trait("Category", "Phase2")]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];
        float sim = VectorQuery.CosineSimilarity(a, b);
        Assert.InRange(sim, -0.001f, 0.001f);
    }

    /// <summary>
    /// Verifies the Cosine Similarity: Empty Vectors Returns Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Empty Vectors Returns Zero")]
    [Trait("Category", "Phase2")]
    public void CosineSimilarity_EmptyVectors_ReturnsZero()
    {
        Assert.Equal(0f, VectorQuery.CosineSimilarity([], []));
    }

    /// <summary>
    /// Verifies the Index Stats: Computes Per Field Average Length scenario.
    /// </summary>
    [Fact(DisplayName = "Index Stats: Computes Per Field Average Length")]
    [Trait("Category", "Phase2")]
    public void IndexStats_ComputesPerFieldAverageLength()
    {
        var dir = new MMapDirectory(SubDir("stats_avglen"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Doc with 3 tokens
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "one two three"));
        writer.AddDocument(doc1);

        // Doc with 1 token
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "single"));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var stats = searcher.Stats;

        Assert.Equal(2, stats.TotalDocCount);
        Assert.Equal(2, stats.LiveDocCount);
        float avgLen = stats.GetAvgFieldLength("body");
        // Average of ~3 and ~1 should be ~2
        Assert.InRange(avgLen, 1.0f, 4.0f);
    }

    /// <summary>
    /// Verifies the BM25: Norms Integration Shorter Doc Ranks Higher For Same Term Freq scenario.
    /// </summary>
    [Fact(DisplayName = "BM25: Norms Integration Shorter Doc Ranks Higher For Same Term Freq")]
    [Trait("Category", "Phase2")]
    public void Bm25_NormsIntegration_ShorterDocRanksHigherForSameTermFreq()
    {
        var dir = new MMapDirectory(SubDir("bm25_norms"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Short doc: "search" appears once in 1 token
        var shortDoc = new LeanDocument();
        shortDoc.Add(new TextField("body", "search"));
        writer.AddDocument(shortDoc);

        // Long doc: "search" appears once among many tokens
        var longDoc = new LeanDocument();
        longDoc.Add(new TextField("body", "search is one of many words in this very long document body text"));
        writer.AddDocument(longDoc);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "search"), 10);

        Assert.Equal(2, results.TotalHits);
        // Shorter doc should rank higher (BM25 length normalisation)
        Assert.Equal(0, results.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Numeric Field: Range Query Via Numeric Index scenario.
    /// </summary>
    [Fact(DisplayName = "Numeric Field: Range Query Via Numeric Index")]
    [Trait("Category", "Phase2")]
    public void NumericField_RangeQuery_ViaNumericIndex()
    {
        var dir = new MMapDirectory(SubDir("numeric_idx"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 10; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("score", i * 10));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RangeQuery("score", 25, 55), 10);

        // Values: 30, 40, 50 => 3 docs
        Assert.Equal(3, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Atomic Commit: Write Then Rename Produces Commit File scenario.
    /// </summary>
    [Fact(DisplayName = "Atomic Commit: Write Then Rename Produces Commit File")]
    [Trait("Category", "Phase2")]
    public void AtomicCommit_WriteThenRename_ProducesCommitFile()
    {
        var dirPath = SubDir("atomic_commit");
        var dir = new MMapDirectory(dirPath);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "commit test"));
        writer.AddDocument(doc);
        writer.Commit();

        // segments_1 should exist, no .tmp leftover
        Assert.True(File.Exists(System.IO.Path.Combine(dirPath, "segments_1")));
        Assert.False(File.Exists(System.IO.Path.Combine(dirPath, "segments_1.tmp")));
    }

    /// <summary>
    /// Verifies the Top Docs Collector: Top N Does Not Sort Full Result Set scenario.
    /// </summary>
    [Fact(DisplayName = "Top Docs Collector: Top N Does Not Sort Full Result Set")]
    [Trait("Category", "Phase2")]
    public void TopDocsCollector_TopN_DoesNotSortFullResultSet()
    {
        var dir = new MMapDirectory(SubDir("topn_collect"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Index 20 docs all containing "common"
        for (int i = 0; i < 20; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "common"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "common"), 5);

        Assert.Equal(20, results.TotalHits);
        Assert.Equal(5, results.ScoreDocs.Length);
    }

    /// <summary>
    /// Verifies the Segment Merger: Below Threshold No Merge scenario.
    /// </summary>
    [Fact(DisplayName = "Segment Merger: Below Threshold No Merge")]
    [Trait("Category", "Phase2")]
    public void SegmentMerger_BelowThreshold_NoMerge()
    {
        var dir = new MMapDirectory(SubDir("merger_nomerge"));
        var merger = new SegmentMerger(dir, mergeThreshold: 20);

        var segments = new List<SegmentInfo>();
        for (int i = 0; i < 5; i++)
            segments.Add(new SegmentInfo { SegmentId = $"seg_{i}", DocCount = 1, LiveDocCount = 1, FieldNames = ["body"] });

        int ordinal = 100;
        var result = merger.MaybeMerge(segments, ref ordinal);

        Assert.Equal(5, result.Count);
    }

    /// <summary>
    /// Verifies the Phrase Query: Multi Segment Matches Across Segments scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: Multi Segment Matches Across Segments")]
    public void PhraseQuery_MultiSegment_MatchesAcrossSegments()
    {
        var dir = new MMapDirectory(SubDir("phrase_multiseg"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 3 };
        using var writer = new IndexWriter(dir, config);

        // Segment 1: Index first batch of documents
        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("content", "the quick brown fox jumps over the lazy dog"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Segment 2: Index second batch of documents
        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("content", "the quick brown fox runs in the field"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("content", "quick", "brown", "fox"), 10);

        // Should match all 6 documents across both segments
        Assert.Equal(6, results.TotalHits);
    }
}
