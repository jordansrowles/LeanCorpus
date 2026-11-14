using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search.Chaos;

/// <summary>
/// Metamorphic relation (ADR048) for the positional span fast path: a near or not-near query over
/// terms with unequal in-document frequencies scores the same whether its document sits in a segment
/// where one term is rarer or, after merging, where the other term is. The traversal leader is chosen
/// by segment-local document frequency, so the score must not depend on which clause leads.
/// </summary>
[Category(TestCategory.Chaos)]
[Area(TestArea.Search)]
public sealed class SpanScoreSegmentInvarianceTests
{
    // In the first document's own segment alpha and beta both have DF 1, so alpha (clause 0) leads
    // with tf 2. After merging, beta (DF 1) is rarer than alpha (DF 4) and leads with tf 1.
    private static readonly string[] Bodies =
    [
        "alpha alpha beta",
        "alpha",
        "alpha",
        "alpha beta beta beta",
    ];

    public static TheoryData<string> Families() => new() { "near-ordered", "near-unordered", "not-absent-term", "not-absent-or" };

    [Theory(DisplayName = "Span Scores: Unequal Clause Frequencies Are Invariant Across Segments And Merge")]
    [MemberData(nameof(Families))]
    public void SpanScores_UnequalClauseFrequencies_AreInvariantAcrossSegmentsAndMerge(string family)
    {
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("tests"), "LeanCorpusSpanScoreInvariance", $"{family}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
            {
                for (int i = 0; i < Bodies.Length; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), stored: true));
                    document.Add(new TextField("body", Bodies[i]));
                    writer.AddDocument(document);
                    writer.Commit();
                }
                Assert.Equal(Bodies.Length, writer.GetNrtSegments().Count);
            }

            (string Id, float Score)[] segmented = Search(directory, family);

            using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
            {
                Assert.True(writer.ForceMerge(1) > 0);
                writer.Commit();
                Assert.Single(writer.GetNrtSegments());
            }

            (string Id, float Score)[] merged = Search(directory, family);

            Assert.Equal(["00", "03"], merged.Select(static hit => hit.Id).Order(StringComparer.Ordinal));
            Assert.Equal(merged.Select(static hit => hit.Id), segmented.Select(static hit => hit.Id));
            for (int i = 0; i < merged.Length; i++)
            {
                Assert.True(Math.Abs(merged[i].Score - segmented[i].Score) <= 1e-5f * Math.Max(1f, Math.Abs(merged[i].Score)),
                    $"{merged[i].Id}: merged {merged[i].Score:R}, segmented {segmented[i].Score:R}");
            }
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    private static (string Id, float Score)[] Search(MMapDirectory directory, string family)
    {
        using var searcher = new IndexSearcher(directory);
        var near = family == "near-unordered"
            ? new SpanNearQuery([new SpanTermQuery("body", "alpha"), new SpanTermQuery("body", "beta")], 3, inOrder: false)
            : new SpanNearQuery([new SpanTermQuery("body", "alpha"), new SpanTermQuery("body", "beta")], 3, inOrder: true);
        Query query = family switch
        {
            "near-ordered" or "near-unordered" => near,
            "not-absent-term" => new SpanNotQuery(near, new SpanTermQuery("body", "gamma")),
            "not-absent-or" => new SpanNotQuery(near, new SpanOrQuery(new SpanTermQuery("body", "gamma"), new SpanTermQuery("body", "delta"))),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        return searcher.Search(query, 10, TestContext.Current.CancellationToken).ScoreDocs
            .Select(hit => (searcher.GetStoredFields(hit.DocId)["id"][0], hit.Score))
            .OrderByDescending(static hit => hit.Item2)
            .ThenBy(static hit => hit.Item1, StringComparer.Ordinal)
            .ToArray();
    }
}
