using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>End-to-end coverage for learned-sparse impact fields and exact scoring.</summary>
[Trait("Category", "Hybrid")]
public sealed class SparseImpactQueryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SparseImpactQueryTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Sparse impacts: Exact weighted scores survive merge")]
    public void SparseImpacts_ExactWeightedScoresSurviveMerge()
    {
        string path = Path.Combine(_fixture.Path, "sparse_impacts_merge");
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            writer.AddDocument(Document("impact", ("apple", 2f), ("banana", 1f)));
            writer.Commit();
            writer.AddDocument(Document("impact", ("apple", 1f), ("banana", 3f)));
            writer.AddDocument(Document("impact", ("carrot", 5f)));
            writer.Commit();
            writer.ForceMerge(1);
        }

        using var searcher = new IndexSearcher(directory);
        var query = new SparseImpactQuery("impact",
        [
            new SparseImpact("apple", 1f),
            new SparseImpact("banana", 2f),
        ]);
        var result = searcher.Search(query, 10);

        Assert.Equal(2, result.TotalHits);
        Assert.Equal(1, result.ScoreDocs[0].DocId);
        Assert.Equal(7f, result.ScoreDocs[0].Score, 5);
        Assert.Equal(0, result.ScoreDocs[1].DocId);
        Assert.Equal(4f, result.ScoreDocs[1].Score, 5);
    }

    [Fact(DisplayName = "Sparse impacts: Invalid query and field impacts are rejected")]
    public void SparseImpacts_InvalidValuesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SparseImpactField(
            "impact", [new SparseImpact("term", 0f)]));
        Assert.Throws<ArgumentException>(() => new SparseImpactQuery(
            "impact", [new SparseImpact("term", float.NaN)]));
        Assert.Throws<ArgumentException>(() => new SparseImpactQuery(
                "impact", [new SparseImpact("term", 1f), new SparseImpact("term", 2f)]));
    }

    [Fact(DisplayName = "Sparse impacts: Safe upper-bound pruning preserves exact top result")]
    public void SparseImpacts_UpperBoundPruningPreservesExactTopResult()
    {
        string path = Path.Combine(_fixture.Path, "sparse_impacts_pruning");
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            writer.AddDocument(Document("impact", ("apple", 10f)));
            for (int i = 0; i < 20; i++)
                writer.AddDocument(Document("impact", ("banana", 0.1f)));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var result = searcher.Search(
            new SparseImpactQuery("impact", [new SparseImpact("apple", 1f)]), 1);

        Assert.Single(result.ScoreDocs);
        Assert.Equal(0, result.ScoreDocs[0].DocId);
        Assert.Equal(10f, result.ScoreDocs[0].Score, 5);
    }

    private static LeanDocument Document(string field, params (string Term, float Weight)[] impacts)
    {
        var document = new LeanDocument();
        document.Add(new SparseImpactField(field,
            impacts.Select(static impact => new SparseImpact(impact.Term, impact.Weight))));
        return document;
    }
}
