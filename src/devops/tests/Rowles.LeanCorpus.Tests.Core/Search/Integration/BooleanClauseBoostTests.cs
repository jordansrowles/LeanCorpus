using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// A term clause's own boost multiplies that clause's contribution to a Boolean score, on every
/// execution path: MUST, SHOULD, MUST with SHOULD, the heap union for many SHOULD clauses, the
/// generic fallback and with Block-Max WAND enabled. A boost of one changes nothing.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class BooleanClauseBoostTests : IClassFixture<TestDirectoryFixture>
{
    private const float Boost = 2.5f;

    private readonly TestDirectoryFixture _fixture;

    public BooleanClauseBoostTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<string> Shapes => new() { "should", "must", "must-should", "heap", "fallback" };

    [Theory(DisplayName = "Boolean: A Clause Boost Multiplies That Clause's Score")]
    [MemberData(nameof(Shapes))]
    public void Boolean_ClauseBoost_MultipliesThatClausesScore(string shape)
    {
        foreach (bool wand in new[] { false, true })
        {
            using var directory = Build("clause_boost_" + shape, segments: 3);
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableBlockMaxWand = wand });

            var plain = Hits(searcher, Query(shape, boost: 1f));
            var boosted = Hits(searcher, Query(shape, boost: Boost));
            var alphaAlone = Hits(searcher, new TermQuery("body", "alpha"));
            var betaAlone = Hits(searcher, new TermQuery("body", "beta"));

            Assert.Equal(plain.Keys.Order(), boosted.Keys.Order());
            foreach (var (id, score) in plain)
            {
                // "alpha" carries the boost; every other clause contributes as before.
                float alpha = alphaAlone.GetValueOrDefault(id);
                float expected = score + ((Boost - 1f) * alpha);
                Assert.True(Math.Abs(boosted[id] - expected) <= 1e-4f * Math.Max(1f, expected),
                    $"{shape} wand={wand} doc {id}: boosted {boosted[id]}, expected {expected} (plain {score}, alpha {alpha})");
            }

            // Documents matching only the boosted clause score exactly its boosted term score.
            if (shape == "should")
            {
                foreach (var (id, alpha) in alphaAlone.Where(pair => !betaAlone.ContainsKey(pair.Key)))
                    Assert.Equal(Boost * alpha, boosted[id], 4);
            }
        }
    }

    [Fact(DisplayName = "Boolean: A Clause Boost Of One Leaves Scores Unchanged")]
    public void Boolean_ClauseBoostOfOne_LeavesScoresUnchanged()
    {
        using var directory = Build("clause_boost_one", segments: 3);
        using var searcher = new IndexSearcher(directory);
        foreach (string shape in new[] { "should", "must", "must-should", "heap", "fallback" })
        {
            var unset = Hits(searcher, Query(shape, boost: null));
            var one = Hits(searcher, Query(shape, boost: 1f));
            Assert.Equal(unset, one);
        }
    }

    // "alpha" is the boosted clause in every shape.
    private static Query Query(string shape, float? boost)
    {
        var alpha = new TermQuery("body", "alpha");
        if (boost is { } value)
            alpha.Boost = value;
        var builder = new BooleanQuery.Builder();
        switch (shape)
        {
            case "should":
                builder.Add(alpha, Occur.Should).Add(new TermQuery("body", "beta"), Occur.Should);
                break;
            case "must":
                builder.Add(alpha, Occur.Must).Add(new TermQuery("body", "gamma"), Occur.Must);
                break;
            case "must-should":
                builder.Add(new TermQuery("body", "gamma"), Occur.Must).Add(alpha, Occur.Should).Add(new TermQuery("body", "beta"), Occur.Should);
                break;
            case "heap":
                builder.Add(alpha, Occur.Should).Add(new TermQuery("body", "beta"), Occur.Should);
                for (int i = 0; i < 70; i++)
                    builder.Add(new TermQuery("body", "filler" + i.ToString(CultureInfo.InvariantCulture)), Occur.Should);
                break;
            case "fallback":
                // A nested Boolean clause routes execution through the generic fallback.
                builder.Add(alpha, Occur.Should)
                    .Add(new BooleanQuery.Builder().Add(new TermQuery("body", "beta"), Occur.Should).Build(), Occur.Should);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
        return builder.Build();
    }

    private static Dictionary<string, float> Hits(IndexSearcher searcher, Query query)
    {
        var results = searcher.Search(query, 1_000, TestContext.Current.CancellationToken);
        return results.ScoreDocs.ToDictionary(hit => searcher.GetStoredFields(hit.DocId)["id"][0], static hit => hit.Score, StringComparer.Ordinal);
    }

    private MMapDirectory Build(string name, int segments)
    {
        const int count = 300;
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(count / (double)segments),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < count; i++)
        {
            var words = new List<string>();
            if (i % 2 == 0) words.Add("alpha");
            if (i % 3 == 0) words.Add("beta");
            if (i % 5 != 4) words.Add("gamma");
            if (i % 7 == 0) words.Add("alpha");
            words.Add("filler" + (i % 70).ToString(CultureInfo.InvariantCulture));
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new TextField("body", string.Join(' ', words)));
            writer.AddDocument(document);
        }
        writer.Commit();
        return directory;
    }
}
