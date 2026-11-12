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
/// Metamorphic relation (ADR048): hits, scores and order are the same whether a fixed document set
/// is searched as many segments or after merging to one, for every scored query family. MoreLikeThis
/// uses exact global document frequencies for term selection, including frequency limits.
/// MoreLikeThisGlobalStatisticsTests also covers an unrepresentative source segment.
/// </summary>
[Category(TestCategory.Chaos)]
[Area(TestArea.Search)]
public sealed class QueryFamilySegmentInvarianceTests
{
    private static readonly string[] Bodies =
    [
        "government market national economy",
        "governments market reform",
        "governor economic market",
        "mismanagement of the national market",
        "movement towards government reform",
        "international economy market nations",
        "economic government government policy",
        "markets and governments",
        "the market moves",
        "national government economy economy",
        "reform reform market",
        "unrelated filler words"
    ];

    [Theory(DisplayName = "Query Families: Scores And Order Are Invariant Across Segments And Merge")]
    [InlineData("term")]
    [InlineData("boolean")]
    [InlineData("phrase")]
    [InlineData("prefix")]
    [InlineData("wildcard")]
    [InlineData("regexp-prefix")]
    [InlineData("regexp-contains")]
    [InlineData("regexp-fallback")]
    [InlineData("fuzzy")]
    [InlineData("span-or")]
    [InlineData("span-near")]
    [InlineData("more-like-this")]
    public void QueryFamilies_ScoresAndOrder_AreInvariantAcrossSegmentsAndMerge(string family)
    {
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("tests"), "LeanCorpusSegmentInvariance", $"{family}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance, StoreTermVectors = true }))
            {
                for (int i = 0; i < Bodies.Length; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), stored: true));
                    document.Add(new TextField("body", Bodies[i], stored: true));
                    writer.AddDocument(document);
                    writer.Commit();
                }
                Assert.True(writer.GetNrtSegments().Count > 1);
            }

            Hit[] segmented = Search(directory, family);

            using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance, StoreTermVectors = true }))
            {
                Assert.True(writer.ForceMerge(1) > 0);
                writer.Commit();
                Assert.Single(writer.GetNrtSegments());
            }

            Hit[] merged = Search(directory, family);

            Assert.NotEmpty(merged);
            bool invariant = merged.Select(static hit => hit.Id).SequenceEqual(segmented.Select(static hit => hit.Id)) &&
                             merged.Zip(segmented).All(static pair => Math.Abs(pair.First.Score - pair.Second.Score) <= 1e-5f * Math.Max(1f, Math.Abs(pair.First.Score)));
            string Describe(Hit[] hits) => string.Join(", ", hits.Select(static hit => string.Create(
                System.Globalization.CultureInfo.InvariantCulture, $"{hit.Id}={hit.Score:F5}")));
            Assert.True(invariant,
                $"merged [{Describe(merged)}] segmented [{Describe(segmented)}]");
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    private static Hit[] Search(MMapDirectory directory, string family)
    {
        using var searcher = new IndexSearcher(directory);
        Query query = family switch
        {
            "term" => new TermQuery("body", "market"),
            "boolean" => new BooleanQuery.Builder()
                .Add(new TermQuery("body", "market"), Occur.Should)
                .Add(new TermQuery("body", "economy"), Occur.Should)
                .Build(),
            "phrase" => new PhraseQuery("body", "national", "economy"),
            "prefix" => new PrefixQuery("body", "gov"),
            "wildcard" => new WildcardQuery("body", "m*rket*"),
            "regexp-prefix" => new RegexpQuery("body", "mark.*"),
            "regexp-contains" => new RegexpQuery("body", ".*nation.*"),
            "regexp-fallback" => new RegexpQuery("body", "[gm].*t"),
            "fuzzy" => new FuzzyQuery("body", "econmic", 1),
            "span-or" => new SpanOrQuery(new SpanTermQuery("body", "market"), new SpanTermQuery("body", "reform")),
            "span-near" => new SpanNearQuery([new SpanTermQuery("body", "government"), new SpanTermQuery("body", "reform")], 2, inOrder: false),
            "more-like-this" => new MatchNoDocsQuery(),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        var results = family == "more-like-this"
            ? searcher.MoreLikeThis(
                searcher.Search(new TermQuery("id", "00"), 1, TestContext.Current.CancellationToken).ScoreDocs[0].DocId,
                ["body"], 100, new MoreLikeThisParameters())
            : searcher.Search(query, 100, TestContext.Current.CancellationToken);
        return results.ScoreDocs
            .Select(hit => new Hit(searcher.GetStoredFields(hit.DocId)["id"][0], hit.Score))
            .OrderByDescending(static hit => hit.Score)
            .ThenBy(static hit => hit.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record Hit(string Id, float Score);
}
