using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

[Trait("Category", "Search")]
public sealed class FusionQueryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public FusionQueryTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    [Fact(DisplayName = "Fusion: Normalised linear combines independently scaled scores")]
    public void NormalisedLinear_CombinesIndependentlyScaledScores()
    {
        var first = new TopDocs(
            3,
            [new ScoreDoc(1, 100f), new ScoreDoc(2, 90f), new ScoreDoc(4, 0f)]);
        var second = new TopDocs(2, [new ScoreDoc(2, 1f), new ScoreDoc(3, 0f)]);

        var result = FusionQuery.Combine(
            [first, second],
            [1f, 1f],
            topN: 3,
            FusionMethod.NormalisedLinear);

        Assert.Equal(2, result.ScoreDocs[0].DocId);
    }

    [Fact(DisplayName = "Fusion: Log odds requires calibrated child scores")]
    public void LogOdds_RequiresCalibratedChildScores()
    {
        var invalid = new TopDocs(1, [new ScoreDoc(1, 1.1f)]);

        Assert.Throws<InvalidDataException>(
            () => FusionQuery.Combine(
                [invalid],
                [1f],
                topN: 1,
                FusionMethod.LogOdds));
    }

    [Fact(DisplayName = "Fusion: Ordering is deterministic after score and best-rank ties")]
    public void Ordering_IsDeterministicAfterTies()
    {
        var first = new TopDocs(2, [new ScoreDoc(9, 1f), new ScoreDoc(3, 0f)]);
        var second = new TopDocs(2, [new ScoreDoc(3, 1f), new ScoreDoc(9, 0f)]);

        var result = FusionQuery.Combine(
            [first, second],
            [1f, 1f],
            topN: 2,
            FusionMethod.NormalisedLinear);

        Assert.Equal([3, 9], result.ScoreDocs.Select(hit => hit.DocId));
    }

    [Fact(DisplayName = "Fusion Query: Executes bounded weighted RRF children")]
    public void FusionQuery_ExecutesBoundedWeightedRrfChildren()
    {
        string path = Path.Combine(_path, nameof(FusionQuery_ExecutesBoundedWeightedRrfChildren));
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var first = new LeanDocument();
            first.Add(new TextField("title", "alpha"));
            writer.AddDocument(first);

            var overlap = new LeanDocument();
            overlap.Add(new TextField("title", "alpha filler filler"));
            overlap.Add(new TextField("body", "beta"));
            writer.AddDocument(overlap);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new TermQuery("title", "alpha"), candidateWindow: 2)
            .Add(new TermQuery("body", "beta"), candidateWindow: 1);

        var execution = searcher.SearchWithDiagnostics(query, 1);

        Assert.Equal(1, Assert.Single(execution.Results.ScoreDocs).DocId);
        Assert.Equal(SearchExecutionStrategy.Fusion, execution.Diagnostics.Strategy);
    }

    [Fact(DisplayName = "Fusion Query: Learned sparse candidates seed bounded dense traversal")]
    public void FusionQuery_LearnedSparseCandidatesSeedBoundedDenseTraversal()
    {
        string path = Path.Combine(_path, nameof(FusionQuery_LearnedSparseCandidatesSeedBoundedDenseTraversal));
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            HnswSeed = 1,
        }))
        {
            var seeded = new LeanDocument();
            seeded.Add(new VectorField("emb", new float[] { 1f, 0f }));
            seeded.Add(new SparseImpactField("impact", [new SparseImpact("alpha", 2f)]));
            writer.AddDocument(seeded);

            var other = new LeanDocument();
            other.Add(new VectorField("emb", new float[] { 0f, 1f }));
            other.Add(new SparseImpactField("impact", [new SparseImpact("beta", 2f)]));
            writer.AddDocument(other);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new VectorQuery("emb", [1f, 0f], topK: 1, efSearch: 1), candidateWindow: 1)
            .Add(new SparseImpactQuery("impact", [new SparseImpact("alpha", 1f)]), candidateWindow: 1)
            .UseSparseVectorSeeds(candidateLimit: 1);

        var results = searcher.Search(query, 1);

        Assert.Equal(0, Assert.Single(results.ScoreDocs).DocId);
    }

    [Fact(DisplayName = "Fusion Query: Equality includes method, window, and weight")]
    public void Equality_IncludesMethodWindowAndWeight()
    {
        var baseline = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new TermQuery("f", "a"), 10, 1f);
        var method = new FusionQuery(FusionMethod.NormalisedLinear)
            .Add(new TermQuery("f", "a"), 10, 1f);
        var window = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new TermQuery("f", "a"), 20, 1f);
        var weight = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new TermQuery("f", "a"), 10, 2f);
        var seedLimit = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new TermQuery("f", "a"), 10, 1f)
            .UseSparseVectorSeeds(10);

        Assert.NotEqual(baseline, method);
        Assert.NotEqual(baseline, window);
        Assert.NotEqual(baseline, weight);
        Assert.NotEqual(baseline, seedLimit);
    }
}
