using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for Token Budget.
/// </summary>
public sealed class TokenBudgetTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public TokenBudgetTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Truncate: Limits Tokens To Configured Budget scenario.
    /// </summary>
    [Fact(DisplayName = "Truncate: Limits Tokens To Configured Budget")]
    public void Truncate_LimitsTokensToConfiguredBudget()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(Truncate_LimitsTokensToConfiguredBudget));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);
        var config = new IndexWriterConfig
        {
            MaxTokensPerDocument = 3,
            TokenBudgetPolicy = TokenBudgetPolicy.Truncate
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "one two three four five six"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        // Act — search for a term that would only exist beyond the budget
        using var searcher = new IndexSearcher(mmap);
        var hitsFour = searcher.Search(new TermQuery("body", "four"), 10);
        var hitsOne = searcher.Search(new TermQuery("body", "one"), 10);

        // Assert
        Assert.Equal(0, hitsFour.TotalHits);
        Assert.Equal(1, hitsOne.TotalHits);
    }

    /// <summary>
    /// Verifies the Reject: Throws When Budget Exceeded scenario.
    /// </summary>
    [Fact(DisplayName = "Reject: Throws When Budget Exceeded")]
    public void Reject_ThrowsWhenBudgetExceeded()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(Reject_ThrowsWhenBudgetExceeded));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);
        var config = new IndexWriterConfig
        {
            MaxTokensPerDocument = 2,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject
        };
        using var writer = new IndexWriter(mmap, config);

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "one two three"));

        // Act & Assert
        var ex = Assert.Throws<TokenBudgetExceededException>(
            () => writer.AddDocument(doc));
        Assert.Equal(2, ex.Budget);
        Assert.True(ex.TokenCount > 2);
    }

    /// <summary>
    /// Verifies the span analysis path rejects before indexing when the budget is exceeded.
    /// </summary>
    [Fact(DisplayName = "Reject: Span Analysis Throws When Budget Exceeded")]
    public void Reject_SpanAnalysis_ThrowsWhenBudgetExceeded()
    {
        var dir = Path.Combine(_path, nameof(Reject_SpanAnalysis_ThrowsWhenBudgetExceeded));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new Analyser(new NGramTokeniser(2, 2)),
            MaxTokensPerDocument = 1,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject
        };
        using var writer = new IndexWriter(mmap, config);

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "abc"));

        var ex = Assert.Throws<TokenBudgetExceededException>(
            () => writer.AddDocument(doc));
        Assert.Equal(1, ex.Budget);
        Assert.Equal(2, ex.TokenCount);
    }

    /// <summary>
    /// Verifies the span analysis path truncates tokens at the configured budget.
    /// </summary>
    [Fact(DisplayName = "Truncate: Span Analysis Limits Tokens To Budget")]
    public void Truncate_SpanAnalysis_LimitsTokensToBudget()
    {
        var dir = Path.Combine(_path, nameof(Truncate_SpanAnalysis_LimitsTokensToBudget));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new Analyser(new NGramTokeniser(2, 2)),
            MaxTokensPerDocument = 1,
            TokenBudgetPolicy = TokenBudgetPolicy.Truncate
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "abc"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var first = searcher.Search(new TermQuery("body", "ab"), 10);
        var second = searcher.Search(new TermQuery("body", "bc"), 10);
        Assert.Equal(1, first.TotalHits);
        Assert.Equal(0, second.TotalHits);
    }

    /// <summary>
    /// Verifies the Warn: Allows All Tokens Through Without Throwing scenario.
    /// </summary>
    [Fact(DisplayName = "Warn: Allows All Tokens Through Without Throwing")]
    public void Warn_AllowsAllTokensThroughWithoutThrowing()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(Warn_AllowsAllTokensThroughWithoutThrowing));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);
        var config = new IndexWriterConfig
        {
            MaxTokensPerDocument = 2,
            TokenBudgetPolicy = TokenBudgetPolicy.Warn
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "one two three four"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        // Act — all tokens should be indexed
        using var searcher = new IndexSearcher(mmap);
        var hits = searcher.Search(new TermQuery("body", "four"), 10);

        // Assert
        Assert.Equal(1, hits.TotalHits);
    }

    /// <summary>
    /// Verifies the Zero Budget: Means Unlimited scenario.
    /// </summary>
    [Fact(DisplayName = "Zero Budget: Means Unlimited")]
    public void ZeroBudget_MeansUnlimited()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(ZeroBudget_MeansUnlimited));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);
        var config = new IndexWriterConfig { MaxTokensPerDocument = 0 };

        using (var writer = new IndexWriter(mmap, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "a b c d e f g h i j"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var hits = searcher.Search(new TermQuery("body", "j"), 10);

        // Assert — all tokens indexed
        Assert.Equal(1, hits.TotalHits);
    }
}
