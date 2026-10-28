using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.Sorting;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialNearestSearchTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_nearest_sort_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact]
    public void GeoNearestMatchesExhaustiveDistanceSortAcrossSegmentsAndMissingValues()
    {
        string path = Path.Combine(_path, "geo");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 4 }))
        {
            AddGeo(writer, 0, 2);
            AddGeo(writer, 0, -2);
            for (int i = 0; i < 100; i++)
            {
                if (i % 13 == 0)
                {
                    writer.AddDocument(new LeanDocument());
                }
                else
                {
                    var document = new LeanDocument();
                    document.Add(new GeoPointField("location", (i % 19) - 9.25, ((i * 71) % 360) - 179.5));
                    if (i % 11 == 0)
                        document.Add(new GeoPointField("location", 0.125, -0.25));
                    writer.AddDocument(document);
                }

                if (i == 47)
                    writer.Commit();
            }
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        Assert.True(searcher.GetSegmentReaders().Count > 1);
        var query = new MatchAllDocsQuery { Boost = 2.5f };
        var distance = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs nearest = searcher.Search(query, 12, distance);
        TopDocs exhaustive = searcher.Search(query, 12, [distance, SortField.DocId]);

        Assert.Equal(exhaustive.TotalHits, nearest.TotalHits);
        Assert.Equal(exhaustive.ScoreDocs.Select(static hit => hit.DocId), nearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.All(nearest.ScoreDocs, static hit => Assert.Equal(2.5f, hit.Score));

        TopDocs completeNearest = searcher.Search(query, 200, distance);
        TopDocs completeExhaustive = searcher.Search(query, 200, [distance, SortField.DocId]);
        Assert.Equal(completeExhaustive.ScoreDocs.Select(static hit => hit.DocId), completeNearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(completeExhaustive.TotalHits, completeNearest.TotalHits);
    }

    [Fact]
    public void XYNearestMatchesExhaustiveDistanceSortForMultiValueTiesAndMissingFields()
    {
        string path = Path.Combine(_path, "xy");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 4 }))
        {
            AddXY(writer, 3, 4);
            AddXY(writer, -3, -4);
            for (int i = 0; i < 100; i++)
            {
                if (i % 13 == 0)
                {
                    writer.AddDocument(new LeanDocument());
                }
                else
                {
                    var document = new LeanDocument();
                    document.Add(new XYPointField("position", (i % 23) - 11.5f, ((i * 17) % 31) - 15.25f));
                    if (i % 11 == 0)
                        document.Add(new XYPointField("position", 0.25f, -0.5f));
                    writer.AddDocument(document);
                }

                if (i == 47)
                    writer.Commit();
            }
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        Assert.True(searcher.GetSegmentReaders().Count > 1);
        var query = new MatchAllDocsQuery();
        var distance = SortField.XYDistance("position", new XYPoint(0, 0));
        TopDocs nearest = searcher.Search(query, 12, distance);
        TopDocs exhaustive = searcher.Search(query, 12, [distance, SortField.DocId]);

        Assert.Equal(exhaustive.TotalHits, nearest.TotalHits);
        Assert.Equal(exhaustive.ScoreDocs.Select(static hit => hit.DocId), nearest.ScoreDocs.Select(static hit => hit.DocId));

        TopDocs completeNearest = searcher.Search(query, 200, distance);
        TopDocs completeExhaustive = searcher.Search(query, 200, [distance, SortField.DocId]);
        Assert.Equal(completeExhaustive.ScoreDocs.Select(static hit => hit.DocId), completeNearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(completeExhaustive.TotalHits, completeNearest.TotalHits);
    }

    [Fact]
    public void ConstantScoreFilterUsesSegmentBitmapAndPreservesScoreAndTotalHits()
    {
        string path = Path.Combine(_path, "filtered");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 4 }))
        {
            for (int i = 0; i < 60; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("status", i % 3 == 0 ? "eligible" : "other"));
                if (i % 9 != 0)
                    document.Add(new GeoPointField("location", (i % 15) - 7, ((i * 31) % 350) - 175));
                writer.AddDocument(document);
                if (i == 29)
                    writer.Commit();
            }
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        var query = new ConstantScoreQuery(new TermQuery("status", "eligible"), 3)
        {
            Boost = 2,
        };
        var distance = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs nearest = searcher.Search(query, 30, distance);
        TopDocs exhaustive = searcher.Search(query, 30, [distance, SortField.DocId]);

        Assert.Equal(20, nearest.TotalHits);
        Assert.Equal(exhaustive.TotalHits, nearest.TotalHits);
        Assert.Equal(exhaustive.ScoreDocs.Select(static hit => hit.DocId), nearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.All(nearest.ScoreDocs, static hit => Assert.Equal(6, hit.Score));
    }

    [Fact]
    public void GeneratedNearestScenariosMatchExhaustiveAcrossFiltersTopNAndDistributions()
    {
        const int documentCount = 100;
        var random = new Random(0x0700_2032);
        string[] kinds = ["Geo", "XY"];
        string[] distributions = ["Uniform", "Clustered", "MultiValue"];
        (string? Term, int ExpectedHits)[] filters =
        [
            (null, documentCount),
            ("half", documentCount / 2),
            ("tenth", documentCount / 10),
            ("one", 1),
            ("empty", 0)
        ];
        int[] topNs = [1, 10, 100, 150];

        foreach (string kind in kinds)
        foreach (string distribution in distributions)
        {
            string path = Path.Combine(_path, $"matrix-{kind}-{distribution}");
            using (var directory = new MMapDirectory(path))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 4 }))
            {
                for (int i = 0; i < documentCount; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", $"{kind}-{distribution}-{i:D3}"));
                    if (i % 2 == 0)
                        document.Add(new StringField("eligibility", "half", stored: false));
                    if (i % 10 == 0)
                        document.Add(new StringField("eligibility", "tenth", stored: false));
                    if (i == 0)
                        document.Add(new StringField("eligibility", "one", stored: false));

                    int pointCount = distribution == "MultiValue"
                        ? 1 + i % 3
                        : i % 17 == 0 ? 2 : 1;
                    for (int point = 0; point < pointCount; point++)
                    {
                        (double latitude, double longitude, float x, float y) =
                            CreateScenarioPoint(random, distribution, i, point);
                        if (kind == "Geo")
                            document.Add(new GeoPointField("location", latitude, longitude));
                        else
                            document.Add(new XYPointField("position", x, y));
                    }

                    writer.AddDocument(document);
                    if (i is 32 or 65)
                        writer.Commit();
                }
                writer.Commit();
            }

            using var searchDirectory = new MMapDirectory(path);
            using var searcher = new IndexSearcher(searchDirectory);
            SortField sort = kind == "Geo"
                ? SortField.GeoDistance("location", new GeoPoint(0, 0))
                : SortField.XYDistance("position", new XYPoint(0, 0));

            foreach ((string? term, int expectedHits) in filters)
            {
                Query query = term is null
                    ? new MatchAllDocsQuery()
                    : new ConstantScoreQuery(new TermQuery("eligibility", term));
                foreach (int topN in topNs)
                {
                    TopDocs nearest = searcher.Search(query, topN, sort);
                    TopDocs exhaustive = searcher.Search(query, topN, [sort, SortField.DocId]);
                    Assert.Equal(expectedHits, nearest.TotalHits);
                    Assert.Equal(exhaustive.TotalHits, nearest.TotalHits);
                    Assert.Equal(
                        exhaustive.ScoreDocs.Select(static hit => hit.DocId),
                        nearest.ScoreDocs.Select(static hit => hit.DocId));
                }
            }
        }
    }

    [Fact]
    public void GeoNearestMergesLegacyAndPackedSegmentsByExactDistanceAndGlobalDocId()
    {
        string path = Path.Combine(_path, "mixed-geo");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddLegacyGeo(writer, 0, 0.02);
            AddLegacyGeo(writer, 0, -0.01);
            writer.AddDocument(new LeanDocument());
            writer.Commit();
        }

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 }))
        {
            AddGeo(writer, 0, 0.01);
            AddGeo(writer, 0, -0.01);
            AddGeo(writer, 12, 120);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        Assert.True(searcher.GetSegmentReaders().Count > 1);
        var query = new MatchAllDocsQuery();
        var distance = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs nearest = searcher.Search(query, 6, distance);
        TopDocs exhaustive = searcher.Search(query, 6, [distance, SortField.DocId]);

        Assert.Equal(exhaustive.TotalHits, nearest.TotalHits);
        Assert.Equal(exhaustive.ScoreDocs.Select(static hit => hit.DocId), nearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(6, nearest.ScoreDocs.Length);
    }

    [Fact]
    public void GeoNearestIncludesLegacyOnlyDocumentsAfterMixedSegmentsAreForceMerged()
    {
        const int topN = 3;
        string path = Path.Combine(_path, "force-merged-mixed-geo");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddLegacyGeo(writer, 0, 0.00001, includeInEligibilityFilter: true);
            writer.Commit();
        }

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 }))
        {
            for (int i = 1; i <= 5; i++)
                AddGeo(writer, 0, i / 10d, includeInEligibilityFilter: i <= topN);

            writer.Commit();
            writer.ForceMerge(1);
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        Assert.Single(searcher.GetSegmentReaders());

        var distance = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs nearest = searcher.Search(new MatchAllDocsQuery(), topN, distance);
        TopDocs exhaustive = searcher.Search(
            new MatchAllDocsQuery(), topN, [distance, SortField.DocId]);

        Assert.Equal(exhaustive.TotalHits, nearest.TotalHits);
        Assert.Equal(
            exhaustive.ScoreDocs.Select(static hit => hit.DocId),
            nearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(0, nearest.ScoreDocs[0].DocId);

        var filteredQuery = new ConstantScoreQuery(new TermQuery("eligibility", "eligible"), 3)
        {
            Boost = 2
        };
        TopDocs filteredNearest = searcher.Search(filteredQuery, topN, distance);
        TopDocs filteredExhaustive = searcher.Search(
            filteredQuery, topN, [distance, SortField.DocId]);

        Assert.Equal(4, filteredNearest.TotalHits);
        Assert.Equal(filteredExhaustive.TotalHits, filteredNearest.TotalHits);
        Assert.Equal(
            filteredExhaustive.ScoreDocs.Select(static hit => hit.DocId),
            filteredNearest.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(0, filteredNearest.ScoreDocs[0].DocId);
        Assert.All(filteredNearest.ScoreDocs, static hit => Assert.Equal(6, hit.Score));
    }

    private static void AddGeo(
        IndexWriter writer,
        double latitude,
        double longitude,
        bool includeInEligibilityFilter = false)
    {
        var document = new LeanDocument();
        document.Add(new GeoPointField("location", latitude, longitude));
        if (includeInEligibilityFilter)
            document.Add(new StringField("eligibility", "eligible", stored: false));
        writer.AddDocument(document);
    }

    private static void AddXY(IndexWriter writer, float x, float y)
    {
        var document = new LeanDocument();
        document.Add(new XYPointField("position", x, y));
        writer.AddDocument(document);
    }

    private static void AddLegacyGeo(
        IndexWriter writer,
        double latitude,
        double longitude,
        bool includeInEligibilityFilter = false)
    {
        var document = new LeanDocument();
        document.Add(new NumericField("location_lat", latitude, stored: false));
        document.Add(new NumericField("location_lon", longitude, stored: false));
        if (includeInEligibilityFilter)
            document.Add(new StringField("eligibility", "eligible", stored: false));
        writer.AddDocument(document);
    }

    private static (double Latitude, double Longitude, float X, float Y) CreateScenarioPoint(
        Random random,
        string distribution,
        int document,
        int point)
    {
        if (distribution == "Clustered")
            return (random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2,
                (float)(random.NextDouble() * 4 - 2), (float)(random.NextDouble() * 4 - 2));

        if (distribution == "MultiValue")
        {
            // Include exact ties and duplicate coordinates as well as independent
            // values so each document's minimum distance is exercised.
            if (document % 10 == 0 && point == 0)
                return (0, 0, 0, 0);
            if (document % 10 == 1 && point == 0)
                return (0, 1, 1, 0);
        }

        return (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180,
            (float)(random.NextDouble() * 2_000 - 1_000), (float)(random.NextDouble() * 2_000 - 1_000));
    }
}
