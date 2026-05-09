using System.Diagnostics;
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
/// Regression tests for phrase queries at scale (1K+ docs).
/// These validate correct position decoding across v3 block boundaries
/// (128-doc blocks with skip entries containing impact metadata).
/// </summary>
[Trait("Category", "Search")]
public sealed class PhraseQueryScaleTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PhraseQueryScaleTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name) => Path.Combine(_fixture.Path, name);

    /// <summary>
    /// Verifies the Phrase Query: 1000 Docs Returns Correct Results scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: 1000 Docs Returns Correct Results")]
    public void PhraseQuery_1000Docs_ReturnsCorrectResults()
    {
        const int docCount = 1000;
        const string targetPhrase = "quick brown fox";
        int expectedHits = 0;

        var dir = new MMapDirectory(SubDir("phrase_1k"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var rng = new Random(42);
            string[] filler = ["alpha", "beta", "gamma", "delta", "epsilon"];

            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                if (i % 50 == 0)
                {
                    doc.Add(new TextField("body", targetPhrase));
                    expectedHits++;
                }
                else
                {
                    var words = Enumerable.Range(0, 8).Select(_ => filler[rng.Next(filler.Length)]);
                    doc.Add(new TextField("body", string.Join(" ", words)));
                }
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", "quick", "brown", "fox"), expectedHits + 10);

        _output.WriteLine($"1K docs: expected {expectedHits} hits, got {results.TotalHits}");
        Assert.Equal(expectedHits, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Phrase Query: 5000 Docs Returns Correct Results scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: 5000 Docs Returns Correct Results")]
    public void PhraseQuery_5000Docs_ReturnsCorrectResults()
    {
        const int docCount = 5000;
        const string targetPhrase = "search engine optimisation";
        int expectedHits = 0;

        var dir = new MMapDirectory(SubDir("phrase_5k"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var rng = new Random(123);
            string[] filler = ["data", "index", "query", "result", "score", "rank"];

            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                if (i % 100 == 7)
                {
                    doc.Add(new TextField("body", targetPhrase));
                    expectedHits++;
                }
                else
                {
                    var words = Enumerable.Range(0, 10).Select(_ => filler[rng.Next(filler.Length)]);
                    doc.Add(new TextField("body", string.Join(" ", words)));
                }
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", "search", "engine", "optimisation"), expectedHits + 10);

        _output.WriteLine($"5K docs: expected {expectedHits} hits, got {results.TotalHits}");
        Assert.Equal(expectedHits, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Phrase Query: 2 Word Phrase 1500 Docs Matches Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: 2 Word Phrase 1500 Docs Matches Correctly")]
    public void PhraseQuery_2WordPhrase_1500Docs_MatchesCorrectly()
    {
        const int docCount = 1500;
        int expectedHits = 0;

        var dir = new MMapDirectory(SubDir("phrase_2w_1500"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var rng = new Random(77);
            string[] filler = ["red", "blue", "green", "yellow", "orange"];

            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                if (i % 30 == 0)
                {
                    doc.Add(new TextField("body", "hello world and more words"));
                    expectedHits++;
                }
                else
                {
                    var words = Enumerable.Range(0, 6).Select(_ => filler[rng.Next(filler.Length)]);
                    doc.Add(new TextField("body", string.Join(" ", words)));
                }
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", "hello", "world"), expectedHits + 10);

        _output.WriteLine($"1.5K docs, 2-word phrase: expected {expectedHits} hits, got {results.TotalHits}");
        Assert.Equal(expectedHits, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Slop Phrase: 1000 Docs Matches Within Slop scenario.
    /// </summary>
    [Fact(DisplayName = "Slop Phrase: 1000 Docs Matches Within Slop")]
    public void SlopPhrase_1000Docs_MatchesWithinSlop()
    {
        const int docCount = 1000;
        int expectedHits = 0;

        var dir = new MMapDirectory(SubDir("slop_1k"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var rng = new Random(99);
            string[] filler = ["cat", "dog", "bird", "fish", "snake"];

            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                if (i % 40 == 0)
                {
                    // "quick" and "fox" are 2 positions apart (slop=2 should match)
                    doc.Add(new TextField("body", "the quick brown fox jumps"));
                    expectedHits++;
                }
                else
                {
                    var words = Enumerable.Range(0, 6).Select(_ => filler[rng.Next(filler.Length)]);
                    doc.Add(new TextField("body", string.Join(" ", words)));
                }
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", 2, "quick", "fox"), expectedHits + 10);

        _output.WriteLine($"1K docs, slop phrase: expected {expectedHits} hits, got {results.TotalHits}");
        Assert.Equal(expectedHits, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Phrase Query: Multi Segment 1000 Docs Per Segment scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: Multi Segment 1000 Docs Per Segment")]
    public void PhraseQuery_MultiSegment_1000DocsPerSegment()
    {
        const int docsPerSegment = 1000;
        const int segmentCount = 3;
        int expectedHits = 0;

        var dir = new MMapDirectory(SubDir("phrase_multi_seg"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = docsPerSegment }))
        {
            var rng = new Random(55);
            string[] filler = ["north", "south", "east", "west", "centre"];

            for (int seg = 0; seg < segmentCount; seg++)
            {
                for (int i = 0; i < docsPerSegment; i++)
                {
                    var doc = new LeanDocument();
                    if (i % 100 == 0)
                    {
                        doc.Add(new TextField("body", "machine learning pipeline"));
                        expectedHits++;
                    }
                    else
                    {
                        var words = Enumerable.Range(0, 7).Select(_ => filler[rng.Next(filler.Length)]);
                        doc.Add(new TextField("body", string.Join(" ", words)));
                    }
                    writer.AddDocument(doc);
                }
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", "machine", "learning"), expectedHits + 10);

        _output.WriteLine($"Multi-segment ({segmentCount}×{docsPerSegment}): expected {expectedHits} hits, got {results.TotalHits}");
        Assert.Equal(expectedHits, results.TotalHits);
    }
}
