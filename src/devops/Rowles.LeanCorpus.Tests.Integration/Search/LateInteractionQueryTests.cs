using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>End-to-end exact MaxSim coverage for versioned late-interaction payloads.</summary>
[Trait("Category", "Search")]
public sealed class LateInteractionQueryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "ll_late_" + Guid.NewGuid().ToString("N"));

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact(DisplayName = "Late interaction: Weighted MaxSim ranks the exact winner")]
    public void WeightedMaxSim_RanksExactWinner()
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 8 }))
        {
            writer.AddDocument(Document("tokens", [new float[] { 1f, 0f }, new float[] { 0f, 1f }]));
            writer.AddDocument(Document("tokens", [new float[] { 0.7f, 0.7f }, new float[] { 1f, 0f }]));
            writer.Commit();
        }
        using var searcher = new IndexSearcher(directory);
        var query = new LateInteractionQuery(
            "tokens",
            [new float[] { 1f, 0f }, new float[] { 0f, 1f }],
            weights: [2f, 1f]);

        var result = searcher.Search(query, 2);

        Assert.Equal(0, Assert.IsType<int>(result.ScoreDocs[0].DocId));
        Assert.Equal(3f, result.ScoreDocs[0].Score, 5);
        Assert.Equal(2.7f, result.ScoreDocs[1].Score, 5);
    }

    [Fact(DisplayName = "Late interaction: Empty and missing fields remain distinct through force merge")]
    public void EmptyAndMissingFields_RemainDistinctThroughForceMerge()
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 1 }))
        {
            writer.AddDocument(new LeanDocument());
            writer.AddDocument(Document("tokens", []));
            writer.AddDocument(Document("tokens", [new float[] { 1f, 0f }]));
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(directory);
        var reader = Assert.Single(searcher.GetSegmentReaders());

        Assert.False(reader.TryGetBinaryDocValues("tokens", 0, out _));
        Assert.True(reader.TryGetBinaryDocValues("tokens", 1, out var empty));
        Assert.Single(empty);
        Assert.True(reader.TryGetBinaryDocValues("tokens", 2, out var populated));
        Assert.Single(populated);
        Assert.DoesNotContain(
            searcher.Search(new LateInteractionQuery("tokens", [new float[] { 1f, 0f }]), 10).ScoreDocs,
            hit => hit.DocId == 1);
    }

    [Fact(DisplayName = "Late interaction: Contract validation rejects inconsistent token vectors")]
    public void ContractValidation_RejectsInconsistentTokenVectors()
    {
        Assert.Throws<ArgumentException>(() => new MultiVectorField(
            "tokens",
            [new float[] { 1f }, new float[] { 1f, 2f }]));
        Assert.Throws<ArgumentException>(() => new LateInteractionQuery(
            "tokens",
            [new float[] { 1f, 0f }],
            weights: [float.NaN]));
    }

    [Fact(DisplayName = "Late interaction: Participates in bounded fusion")]
    public void LateInteraction_ParticipatesInBoundedFusion()
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            writer.AddDocument(Document("tokens", [new float[] { 1f, 0f }]));
            writer.AddDocument(Document("tokens", [new float[] { 0f, 1f }]));
            writer.Commit();
        }
        using var searcher = new IndexSearcher(directory);
        var fusion = new FusionQuery().Add(
            new LateInteractionQuery("tokens", [new float[] { 1f, 0f }]),
            candidateWindow: 1);

        var result = searcher.Search(fusion, 1);

        Assert.Equal(0, Assert.Single(result.ScoreDocs).DocId);
    }

    private static LeanDocument Document(string field, IReadOnlyList<float[]> vectors)
    {
        var document = new LeanDocument();
        document.Add(new MultiVectorField(field, vectors.Select(vector => (ReadOnlyMemory<float>)vector)));
        return document;
    }
}
