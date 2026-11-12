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
/// Checks <see cref="CombinedFieldsQuery"/> scores against an independent BM25F oracle computed from
/// the raw documents. The oracle uses k1 = 1.2 and b = 0.75; N is every document in the snapshot,
/// deleted ones included; the document frequency is the number of live documents containing the
/// term in any participating field, counted once; each field's average length is taken over live
/// documents that contain the field. A term's pseudo frequency sums
/// <c>weight * boost * tf / (1 - b + b * length / average)</c> over fields, and a matching term scores
/// <c>idf * tf~ * (k1 + 1) / (tf~ + k1)</c>.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class CombinedFieldsOracleTests : IClassFixture<TestDirectoryFixture>
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private static readonly string[] Fields = ["title", "body"];

    // Title, body and the index-time title boost. A null field is absent from the document.
    private static readonly (string? Title, string? Body, float TitleBoost)[] Documents =
    [
        ("apple banana", "apple cherry apple apple", 1f),       // repeated term within a field
        ("apple", null, 1f),                                     // body missing
        (null, "banana banana cherry", 1f),                      // title missing
        ("cherry date", "elder fig grape", 1f),                  // no query term
        ("apple apple", "apple banana", 2.5f),                   // index-time boost, term in both fields
        ("banana", "banana", 1f),                                // same term in both fields: DF counts once
        ("grape fig", "apple fig grape elder date cherry", 1f),  // long body
        ("banana apple", "cherry", 0.5f),                        // both query terms, boost below one
        ("date", "date date date apple", 1f),
        ("elder", "banana elder", 1f),
        ("fig apple banana", null, 3f),
        (null, "apple", 1f),
        ("cherry", "banana fig", 1f),
        ("apple cherry banana", "apple banana cherry", 1f)
    ];

    private readonly TestDirectoryFixture _fixture;

    public CombinedFieldsOracleTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<string, int, bool> Cases => new()
    {
        // Zero, one and two matching terms, unweighted and weighted, with MinimumShouldMatch 1 and 2.
        { "apple", 1, false },
        { "apple banana", 1, false },
        { "apple banana", 2, false },
        { "apple banana", 1, true },
        { "apple banana", 2, true },
        { "cherry zzzabsent", 1, true },
        { "zzzabsent", 1, false },
        { "apple apple banana", 1, true }  // a repeated query term counts once
    };

    [Theory(DisplayName = "Combined Fields: Scores Match An Independent BM25F Oracle")]
    [MemberData(nameof(Cases))]
    public void CombinedFields_MatchesOracle(string terms, int minimumShouldMatch, bool weighted)
    {
        var query = Query(terms, minimumShouldMatch, weighted);
        int[] deleted = [];
        foreach (int segments in new[] { 1, 3, Documents.Length })
        {
            using var directory = Build("cf_oracle_" + segments, segments, deleted);
            using var searcher = new IndexSearcher(directory);
            AssertMatchesOracle(searcher, query, deleted, maxDoc: Documents.Length, $"{segments} segments");
        }
    }

    [Theory(DisplayName = "Combined Fields: Deleted Documents Are Excluded From Hits And Statistics")]
    [MemberData(nameof(Cases))]
    public void CombinedFields_Deletes_MatchOracle(string terms, int minimumShouldMatch, bool weighted)
    {
        var query = Query(terms, minimumShouldMatch, weighted);
        int[] deleted = [0, 5, 9];
        using var directory = Build("cf_oracle_deletes", 4, deleted);
        using (var searcher = new IndexSearcher(directory))
            AssertMatchesOracle(searcher, query, deleted, maxDoc: Documents.Length, "segmented with deletes");

        // A merge drops deleted documents, so N becomes the live count.
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }
        using (var merged = new IndexSearcher(directory))
            AssertMatchesOracle(merged, query, deleted, maxDoc: Documents.Length - deleted.Length, "merged with deletes");
    }

    [Fact(DisplayName = "Combined Fields: Scores Are Invariant Across Segments And Merge")]
    public void CombinedFields_InvariantAcrossSegmentsAndMerge()
    {
        var query = Query("apple banana cherry", 1, weighted: true);
        using var one = Build("cf_invariant_one", 1, []);
        using var many = Build("cf_invariant_many", Documents.Length, []);
        Dictionary<string, float> single, segmented, merged;
        using (var searcher = new IndexSearcher(one))
            single = Hits(searcher, query);
        using (var searcher = new IndexSearcher(many))
            segmented = Hits(searcher, query);
        using (var writer = new IndexWriter(many, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }
        using (var searcher = new IndexSearcher(many))
            merged = Hits(searcher, query);

        Assert.NotEmpty(single);
        Assert.Equal(single.Keys.Order(), segmented.Keys.Order());
        Assert.Equal(single.Keys.Order(), merged.Keys.Order());
        foreach (var (id, score) in single)
        {
            Assert.Equal(score, segmented[id], 5);
            Assert.Equal(score, merged[id], 5);
        }
    }

    [Fact(DisplayName = "Combined Fields: Union Document Frequency Counts A Document Once")]
    public void CombinedFields_UnionDocFreq_CountsOnce()
    {
        // "banana" is in both fields of documents 4, 5 and 13. A double count would raise the DF from
        // the union size and lower every banana score below the oracle's.
        var query = Query("banana", 1, weighted: false);
        int union = Enumerable.Range(0, Documents.Length).Count(i => Contains(i, "title", "banana") || Contains(i, "body", "banana"));
        int perField = Enumerable.Range(0, Documents.Length).Sum(i => (Contains(i, "title", "banana") ? 1 : 0) + (Contains(i, "body", "banana") ? 1 : 0));
        Assert.True(perField > union, "fixture must put the term in both fields of some documents");

        using var directory = Build("cf_union_df", 3, []);
        using var searcher = new IndexSearcher(directory);
        AssertMatchesOracle(searcher, query, [], maxDoc: Documents.Length, "union DF");
    }

    private static CombinedFieldsQuery Query(string terms, int minimumShouldMatch, bool weighted)
        => new(Fields, terms.Split(' '), minimumShouldMatch,
            weighted ? new Dictionary<string, float>(StringComparer.Ordinal) { ["title"] = 2.0f, ["body"] = 0.75f } : null);

    private static void AssertMatchesOracle(IndexSearcher searcher, CombinedFieldsQuery query, int[] deleted, int maxDoc, string topology)
    {
        var expected = Oracle(query, deleted, maxDoc);
        var actual = Hits(searcher, query);
        Assert.True(expected.Keys.Order(StringComparer.Ordinal).SequenceEqual(actual.Keys.Order(StringComparer.Ordinal)),
            $"{topology}: expected hits [{string.Join(",", expected.Keys.Order())}], got [{string.Join(",", actual.Keys.Order())}]");
        foreach (var (id, score) in expected)
        {
            double relative = Math.Abs(actual[id] - score) / Math.Max(1e-9, Math.Abs(score));
            Assert.True(relative < 1e-5, $"{topology}: document {id} expected {score:R}, got {actual[id]:R}");
        }
    }

    private static Dictionary<string, double> Oracle(CombinedFieldsQuery query, int[] deleted, int maxDoc)
    {
        var live = Enumerable.Range(0, Documents.Length).Where(i => !deleted.Contains(i)).ToArray();
        var average = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string field in Fields)
        {
            var lengths = live.Select(i => Tokens(i, field).Length).Where(static length => length > 0).ToArray();
            average[field] = lengths.Length == 0 ? 1.0 : lengths.Average();
        }

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var matched = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string term in query.Terms)
        {
            int docFreq = live.Count(i => Fields.Any(field => Contains(i, field, term)));
            if (docFreq == 0)
                continue;
            double idf = Math.Log(1.0 + ((maxDoc - docFreq + 0.5) / (docFreq + 0.5)));
            foreach (int i in live)
            {
                double pseudo = 0;
                foreach (string field in Fields)
                {
                    string[] tokens = Tokens(i, field);
                    int tf = tokens.Count(token => token == term);
                    if (tf == 0)
                        continue;
                    double weight = query.FieldWeights.TryGetValue(field, out float w) ? w : 1.0;
                    double boost = field == "title" ? Documents[i].TitleBoost : 1.0;
                    pseudo += weight * boost * tf / (1.0 - B + (B * tokens.Length / average[field]));
                }
                if (pseudo <= 0)
                    continue;
                string id = Id(i);
                scores[id] = scores.GetValueOrDefault(id) + (idf * pseudo * (K1 + 1.0) / (pseudo + K1));
                matched[id] = matched.GetValueOrDefault(id) + 1;
            }
        }

        return scores.Where(pair => matched[pair.Key] >= query.MinimumShouldMatch)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, float> Hits(IndexSearcher searcher, CombinedFieldsQuery query)
    {
        var results = searcher.Search(query, 100, TestContext.Current.CancellationToken);
        Assert.Equal(results.TotalHits, results.ScoreDocs.Length);
        return results.ScoreDocs.ToDictionary(hit => searcher.GetStoredFields(hit.DocId)["id"][0], static hit => hit.Score, StringComparer.Ordinal);
    }

    private MMapDirectory Build(string name, int segments, int[] deleted)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = Math.Max(1, (int)Math.Ceiling(Documents.Length / (double)segments)),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < Documents.Length; i++)
        {
            var (title, body, titleBoost) = Documents[i];
            var document = new LeanDocument();
            document.Add(new StringField("id", Id(i), stored: true));
            if (title is not null)
                document.Add(new TextField("title", title, stored: false, boost: titleBoost));
            if (body is not null)
                document.Add(new TextField("body", body));
            writer.AddDocument(document);
        }
        writer.Commit();
        foreach (int id in deleted)
            writer.DeleteDocuments(new TermQuery("id", Id(id)));
        writer.Commit();
        return directory;
    }

    private static string[] Tokens(int i, string field)
    {
        string? text = field == "title" ? Documents[i].Title : Documents[i].Body;
        return text is null ? [] : text.Split(' ');
    }

    private static bool Contains(int i, string field, string term) => Tokens(i, field).Contains(term);

    private static string Id(int i) => i.ToString(CultureInfo.InvariantCulture);
}
