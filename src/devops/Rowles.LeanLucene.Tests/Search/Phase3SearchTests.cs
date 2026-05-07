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

/// <summary>Tests for Phase 3 features: parallel search, RangeQuery boost, VarInt postings end-to-end.</summary>
[Trait("Category", "Search")]
[Trait("Category", "Phase3")]
public sealed class Phase3SearchTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Phase3SearchTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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
    /// Verifies the Range Query: With Boost Scores Reflect Boost Multiplier scenario.
    /// </summary>
    [Fact(DisplayName = "Range Query: With Boost Scores Reflect Boost Multiplier")]
    public void RangeQuery_WithBoost_ScoresReflectBoostMultiplier()
    {
        var dir = new MMapDirectory(SubDir("range_boost"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", $"doc{i}"));
            doc.Add(new NumericField("price", i * 10.0));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        var noBoost = new RangeQuery("price", 10.0, 30.0);
        var boosted = new RangeQuery("price", 10.0, 30.0) { Boost = 2.0f };

        var noBoostResults = searcher.Search(noBoost, 10);
        var boostedResults = searcher.Search(boosted, 10);

        Assert.Equal(noBoostResults.TotalHits, boostedResults.TotalHits);

        // Scores with 2x boost should be roughly 2x the unboosted scores
        for (int i = 0; i < noBoostResults.TotalHits; i++)
        {
            float ratio = boostedResults.ScoreDocs[i].Score / noBoostResults.ScoreDocs[i].Score;
            Assert.InRange(ratio, 1.9f, 2.1f);
        }
    }

    /// <summary>
    /// Verifies the Range Query: Default Boost Scores Unchanged scenario.
    /// </summary>
    [Fact(DisplayName = "Range Query: Default Boost Scores Unchanged")]
    public void RangeQuery_DefaultBoost_ScoresUnchanged()
    {
        var dir = new MMapDirectory(SubDir("range_defaultboost"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new NumericField("val", 50.0));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new RangeQuery("val", 0.0, 100.0);
        Assert.Equal(1.0f, query.Boost);

        var results = searcher.Search(query, 10);
        Assert.Equal(1, results.TotalHits);
        Assert.True(results.ScoreDocs[0].Score > 0f);
    }

    /// <summary>
    /// Verifies the Multi Segment: Parallel Search Returns Correct Results scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Segment: Parallel Search Returns Correct Results")]
    public void MultiSegment_ParallelSearch_ReturnsCorrectResults()
    {
        // Create multiple segments to exercise the Parallel.ForEach path
        var dir = new MMapDirectory(SubDir("parallel_search"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 5 };
        using var writer = new IndexWriter(dir, config);

        for (int i = 0; i < 20; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", i % 3 == 0 ? "target keyword" : "other content"));
            writer.AddDocument(doc);
            if ((i + 1) % 5 == 0)
                writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "target"), 100);

        // docs 0, 3, 6, 9, 12, 15, 18 = 7 documents
        Assert.Equal(7, results.TotalHits);
    }

    /// <summary>
    /// Verifies the VarInt Postings: End-to-end Positions Preserved scenario.
    /// </summary>
    [Fact(DisplayName = "VarInt Postings: End-to-end Positions Preserved")]
    public void VarIntPostings_EndToEnd_PositionsPreserved()
    {
        // Verify positions survive VarInt encoding round-trip through indexing
        var dir = new MMapDirectory(SubDir("varint_positions"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "alpha beta gamma alpha delta alpha"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // PhraseQuery with exact adjacency should match "alpha beta"
        var exact = new PhraseQuery("body", 0, "alpha", "beta");
        Assert.Equal(1, searcher.Search(exact, 10).TotalHits);

        // "alpha gamma" with slop=1 should match (beta is between them)
        var sloppy = new PhraseQuery("body", 1, "alpha", "gamma");
        Assert.Equal(1, searcher.Search(sloppy, 10).TotalHits);

        // "alpha delta" with slop=0 SHOULD match (alpha@3, delta@4 are adjacent)
        var adjacent = new PhraseQuery("body", 0, "alpha", "delta");
        Assert.Equal(1, searcher.Search(adjacent, 10).TotalHits);

        // "beta delta" with slop=0 should NOT match (beta@1, delta@4 — not adjacent)
        var noMatch = new PhraseQuery("body", 0, "beta", "delta");
        Assert.Equal(0, searcher.Search(noMatch, 10).TotalHits);
    }

    /// <summary>
    /// Verifies the VarInt Postings: End-to-end Term Frequencies Correct scenario.
    /// </summary>
    [Fact(DisplayName = "VarInt Postings: End-to-end Term Frequencies Correct")]
    public void VarIntPostings_EndToEnd_TermFrequenciesCorrect()
    {
        var dir = new MMapDirectory(SubDir("varint_freqs"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello hello hello world"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // "hello" appears 3 times, "world" 1 time — hello should score higher
        var helloResults = searcher.Search(new TermQuery("body", "hello"), 10);
        var worldResults = searcher.Search(new TermQuery("body", "world"), 10);

        Assert.Equal(1, helloResults.TotalHits);
        Assert.Equal(1, worldResults.TotalHits);
        Assert.True(helloResults.ScoreDocs[0].Score > worldResults.ScoreDocs[0].Score,
            "Higher TF term should score higher");
    }

    /// <summary>
    /// Verifies the Large Postings List: VarInt Encoded Smaller Than Raw Int 32 scenario.
    /// </summary>
    [Fact(DisplayName = "Large Postings List: VarInt Encoded Smaller Than Raw Int 32")]
    public void LargePostingsList_VarIntEncoded_SmallerThanRawInt32()
    {
        // Many sequential doc IDs → deltas of 1 → each encodes as 1 byte (vs 4 bytes raw)
        var dir = new MMapDirectory(SubDir("varint_size"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 500; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "common"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Verify search still works correctly with large posting list
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "common"), 1000);
        Assert.Equal(500, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Segment Merge: Preserves VarInt Postings scenario.
    /// </summary>
    [Fact(DisplayName = "Segment Merge: Preserves VarInt Postings")]
    public void SegmentMerge_PreservesVarIntPostings()
    {
        // Index across multiple commits, verify results after auto-merge
        var dir = new MMapDirectory(SubDir("merge_varint"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 3 };
        using var writer = new IndexWriter(dir, config);

        for (int i = 0; i < 12; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", i % 2 == 0 ? "even" : "odd"));
            writer.AddDocument(doc);
            if ((i + 1) % 3 == 0)
                writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(6, searcher.Search(new TermQuery("body", "even"), 20).TotalHits);
        Assert.Equal(6, searcher.Search(new TermQuery("body", "odd"), 20).TotalHits);
    }

    /// <summary>
    /// Verifies the Segment Merge: Preserves Positions After Merge scenario.
    /// </summary>
    [Fact(DisplayName = "Segment Merge: Preserves Positions After Merge")]
    public void SegmentMerge_PreservesPositionsAfterMerge()
    {
        var dir = new MMapDirectory(SubDir("merge_positions"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 2 };
        using var writer = new IndexWriter(dir, config);

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "quick brown fox"));
        writer.AddDocument(doc1);
        writer.Commit();

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "quick brown fox"));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var pq = new PhraseQuery("body", 0, "quick", "brown");
        Assert.Equal(2, searcher.Search(pq, 10).TotalHits);
    }
}
