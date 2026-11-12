using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Flat facets over single- and multi-valued fields against an independent oracle computed from
/// the raw documents: exact bucket labels, counts, order, offset pages, missing values, values
/// repeated within one document, many segments and deletes.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class FacetOrdinalOracleTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 900;

    private readonly TestDirectoryFixture _fixture;

    public FacetOrdinalOracleTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<string, int, int, FacetBucketOrder, bool> Requests => new()
    {
        { "topic", 0, int.MaxValue, FacetBucketOrder.CountDescending, false },
        { "topic", 5, 7, FacetBucketOrder.CountDescending, false },
        { "topic", 0, 10, FacetBucketOrder.ValueAscending, true },
        { "tag", 0, int.MaxValue, FacetBucketOrder.CountDescending, true },
        { "tag", 20, 10, FacetBucketOrder.CountAscending, false },
        { "tag", 90, 10, FacetBucketOrder.CountAscending, true },
        { "tag", 0, 5, FacetBucketOrder.ValueDescending, true },
        { "region", 0, int.MaxValue, FacetBucketOrder.ValueDescending, true }
    };

    [Theory(DisplayName = "Facets: Buckets Match An Oracle Across Segments And Deletes")]
    [MemberData(nameof(Requests))]
    public void Facets_MatchOracle(string field, int offset, int limit, FacetBucketOrder order, bool includeMissing)
    {
        int[] deleted = Enumerable.Range(0, DocumentCount).Where(static i => i % 11 == 4).ToArray();
        var request = new FacetRequest(field, offset, limit, order, includeMissing);
        var expected = Oracle(field, offset, limit, order, includeMissing, deleted);
        foreach (int segments in new[] { 1, 6 })
        {
            using var directory = Build("facet_oracle_" + segments, segments, deleted);
            using var searcher = new IndexSearcher(directory);
            var (results, facets) = searcher.SearchWithFacetRequests(new TermQuery("kind", "doc"), 5, [request]);
            Assert.Equal(DocumentCount - deleted.Length, results.TotalHits);
            var facet = Assert.Single(facets);
            Assert.Equal(expected.Buckets, facet.Buckets.Select(static b => (b.Value, b.Count, b.IsMissing)).ToArray());
            Assert.Equal(expected.Total, facet.TotalBucketCount);
            Assert.Equal(expected.Missing, facet.MissingCount);
        }
    }

    // Buckets are (value, count, is-missing). The opt-in missing bucket has an empty value, is ordered
    // with the others by count, and sorts after every value bucket wherever values decide the order.
    private static ((string, int, bool)[] Buckets, int Total, int? Missing) Oracle(
        string field, int offset, int limit, FacetBucketOrder order, bool includeMissing, int[] deleted)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int missing = 0;
        foreach (int i in Enumerable.Range(0, DocumentCount).Where(i => !deleted.Contains(i)))
        {
            // A value repeated within one document counts once.
            string[] values = Values(i, field).Distinct(StringComparer.Ordinal).ToArray();
            if (values.Length == 0)
                missing++;
            foreach (string value in values)
                counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        var buckets = counts.Select(static p => (Value: p.Key, Count: p.Value, IsMissing: false)).ToList();
        if (includeMissing && missing > 0)
            buckets.Add((string.Empty, missing, true));

        IEnumerable<(string Value, int Count, bool IsMissing)> ordered = order switch
        {
            FacetBucketOrder.CountDescending => buckets.OrderByDescending(static b => b.Count).ThenBy(static b => b.IsMissing).ThenBy(static b => b.Value, StringComparer.Ordinal),
            FacetBucketOrder.CountAscending => buckets.OrderBy(static b => b.Count).ThenBy(static b => b.IsMissing).ThenBy(static b => b.Value, StringComparer.Ordinal),
            FacetBucketOrder.ValueAscending => buckets.OrderBy(static b => b.IsMissing).ThenBy(static b => b.Value, StringComparer.Ordinal).ThenByDescending(static b => b.Count),
            _ => buckets.OrderBy(static b => b.IsMissing).ThenByDescending(static b => b.Value, StringComparer.Ordinal).ThenByDescending(static b => b.Count)
        };
        var page = ordered.Skip(offset).Take(limit).ToArray();
        return (page, buckets.Count, includeMissing ? missing : null);
    }

    // topic: two or three values per document, sometimes the same value twice; tag: absent from one
    // document in five; region: single-valued.
    private static string[] Values(int i, string field) => field switch
    {
        "topic" => i % 6 == 0
            ? [Topic(i), Topic(i)]
            : i % 6 == 1 ? [Topic(i), Topic(i + 7), Topic(i + 13)] : [Topic(i), Topic(i + 7)],
        "tag" => i % 5 == 3 ? [] : ["tag" + (i % 97).ToString(CultureInfo.InvariantCulture)],
        "region" => ["region" + (i % 6).ToString(CultureInfo.InvariantCulture)],
        _ => []
    };

    private static string Topic(int i) => "topic" + (i % 23).ToString(CultureInfo.InvariantCulture);

    private MMapDirectory Build(string name, int segments, int[] deleted)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(DocumentCount / (double)segments),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new StringField("kind", "doc"));
            foreach (string field in new[] { "topic", "tag", "region" })
            foreach (string value in Values(i, field))
                document.Add(new StringField(field, value));
            writer.AddDocument(document);
        }
        writer.Commit();
        foreach (int id in deleted)
            writer.DeleteDocuments(new TermQuery("id", id.ToString(CultureInfo.InvariantCulture)));
        writer.Commit();
        return directory;
    }
}
