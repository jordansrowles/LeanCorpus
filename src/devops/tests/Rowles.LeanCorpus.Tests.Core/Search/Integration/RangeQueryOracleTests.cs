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
/// Checks numeric range results against an independent brute-force oracle: inclusive and exclusive
/// boundaries, duplicate values, deleted documents, NaN, infinities and documents without the field,
/// on many segments and after a merge to one.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class RangeQueryOracleTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public RangeQueryOracleTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Range Query: Matches An Independent Oracle")]
    public void RangeQuery_MatchesAnIndependentOracle()
    {
        double?[] values =
        [
            0, 0, -0.0, 1, 1, 2.5, 5, 5, 5, 10, 10, 99.75, 100, -1, -100, 1e308, -1e308,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity, null, null, 7, 7, 42, 3.25
        ];
        // Documents deleted after indexing; the oracle must skip them.
        int[] deleted = [1, 7, 18, 22];

        string path = Path.Combine(_fixture.Path, "range_oracle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 4, MergePolicy = NoMergePolicy.Instance }))
        {
            for (int i = 0; i < values.Length; i++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), stored: true));
                if (values[i] is double value)
                    document.Add(new NumericField("value", value));
                writer.AddDocument(document);
            }
            writer.Commit();
            foreach (int id in deleted)
                writer.DeleteDocuments(new TermQuery("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            writer.Commit();
        }

        (double Min, double Max)[] ranges =
        [
            (0, 10), (5, 5), (-0.0, 0), (1, 1), (-1, 1), (2.5, 99.75), (10, 100), (-100, -1), (100, 1e308),
            (double.NegativeInfinity, double.PositiveInfinity), (double.NegativeInfinity, 0), (0, double.PositiveInfinity),
            (11, 41), (42, 42), (double.NaN, 10), (0, double.NaN), (6, 4)
        ];
        bool[] flags = [true, false];

        void Check(string topology)
        {
            using var searcher = new IndexSearcher(directory);
            foreach (var (min, max) in ranges)
            foreach (bool includeMin in flags)
            foreach (bool includeMax in flags)
            {
                string[] expected = Enumerable.Range(0, values.Length)
                    .Where(i => !deleted.Contains(i) && values[i] is double v &&
                                (includeMin ? v >= min : v > min) && (includeMax ? v <= max : v < max))
                    .Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var results = searcher.Search(new RangeQuery("value", min, max, includeMin, includeMax), 1_000, TestContext.Current.CancellationToken);
                string[] actual = results.ScoreDocs
                    .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.True(expected.SequenceEqual(actual),
                    $"{topology} [{min}, {max}] includeMin={includeMin} includeMax={includeMax}: expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
                Assert.Equal(expected.Length, results.TotalHits);
            }
        }

        Check("segmented");
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }
        Check("merged");
    }
}
