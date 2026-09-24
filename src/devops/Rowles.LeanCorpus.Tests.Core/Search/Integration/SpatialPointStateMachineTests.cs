using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
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
public sealed class SpatialPointStateMachineTests : IDisposable
{
    private const int ReplaySeed = 0x7002_032;
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_spatial_model_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact(DisplayName = "NRT spatial snapshot reopens packed Geo and XY segments before commit")]
    public void NrtSnapshotReopensPackedSpatialSegmentsBeforeCommit()
    {
        using var directory = new MMapDirectory(_path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 2,
            MergePolicy = NoMergePolicy.Instance
        });

        writer.AddDocument(CreateDocument(new ModelDocument(
            "committed", 0, [new GeoPoint(4, 4)], [new XYPoint(4, 4)])));
        writer.Commit();
        writer.AddDocument(CreateDocument(new ModelDocument(
            "uncommitted", 1, [new GeoPoint(0.01, 0.01)], [new XYPoint(0.01f, 0.01f)])));

        IReadOnlyList<SegmentInfo> nrtSegments = writer.GetNrtSegments();
        Assert.Equal(2, nrtSegments.Count);
        using (var nrtDirectory = new MMapDirectory(_path))
        using (var nrtSearcher = new IndexSearcher(nrtDirectory, nrtSegments))
        {
            var matchAll = new MatchAllDocsQuery();
            TopDocs geoNearest = nrtSearcher.Search(
                matchAll, 2, SortField.GeoDistance("location", new GeoPoint(0, 0)));
            TopDocs xyNearest = nrtSearcher.Search(
                matchAll, 2, SortField.XYDistance("position", new XYPoint(0, 0)));

            Assert.Equal(["uncommitted", "committed"], GetIds(nrtSearcher, geoNearest));
            Assert.Equal(["uncommitted", "committed"], GetIds(nrtSearcher, xyNearest));
            Assert.Equal(2, geoNearest.TotalHits);
            Assert.Equal(2, xyNearest.TotalHits);
        }

        writer.Commit();
        using var committedDirectory = new MMapDirectory(_path);
        using var committedSearcher = new IndexSearcher(committedDirectory);
        TopDocs committedNearest = committedSearcher.Search(
            new MatchAllDocsQuery(), 2, SortField.GeoDistance("location", new GeoPoint(0, 0)));
        Assert.Equal(["uncommitted", "committed"], GetIds(committedSearcher, committedNearest));
    }

    [Fact(DisplayName = "Spatial point state machine matches the model (replay seed 0x07002032)")]
    public void SeededPointUpdatesDeletesCommitsAndMergeMatchIndependentModel()
    {
        var random = new Random(ReplaySeed);
        var model = new Dictionary<string, ModelDocument>(StringComparer.Ordinal);
        int nextId = 0;
        long nextVersion = 0;

        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 3 }))
        {
            for (int step = 0; step < 48; step++)
            {
                int operation = model.Count == 0 ? 0 : random.Next(100);
                if (operation < 48)
                {
                    string id = $"doc-{nextId++:D3}";
                    ModelDocument state = CreateModelDocument(id, nextVersion++, random);
                    writer.AddDocument(CreateDocument(state));
                    model.Add(id, state);
                }
                else
                {
                    string id = model.Keys.ElementAt(random.Next(model.Count));
                    if (operation < 78)
                    {
                        ModelDocument replacement = CreateModelDocument(id, nextVersion++, random);
                        writer.UpdateDocument("id", id, CreateDocument(replacement));
                        model[id] = replacement;
                    }
                    else
                    {
                        writer.DeleteDocuments(new TermQuery("id", id));
                        model.Remove(id);
                    }
                }

                if (step % 8 == 7 || step == 47)
                {
                    if (step == 23)
                        writer.ForceMerge(1);
                    writer.Commit();
                    ValidateAgainstModel(model);
                }
            }
        }
    }

    private void ValidateAgainstModel(IReadOnlyDictionary<string, ModelDocument> model)
    {
        using var directory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directory);
        var matchAll = new MatchAllDocsQuery();
        var geoOrigin = new GeoPoint(12.5, -33.25);
        var xyOrigin = new XYPoint(7.5f, -4.25f);
        var geoSort = SortField.GeoDistance("location", geoOrigin);
        var xySort = SortField.XYDistance("position", xyOrigin);

        string[] expectedGeo = model.Values
            .Where(static document => document.GeoPoints.Length > 0)
            .OrderBy(document => document.GeoPoints.Min(point => GeoEncodingUtils.HaversineDistance(
                geoOrigin.Latitude, geoOrigin.Longitude, point.Latitude, point.Longitude)))
            .ThenBy(static document => document.Version)
            .Select(static document => document.Id)
            .Concat(model.Values.Where(static document => document.GeoPoints.Length == 0)
                .OrderBy(static document => document.Version)
                .Select(static document => document.Id))
            .ToArray();
        string[] expectedXy = model.Values
            .Where(static document => document.XYPoints.Length > 0)
            .OrderBy(document => document.XYPoints.Min(point =>
            {
                double dx = point.X - xyOrigin.X;
                double dy = point.Y - xyOrigin.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }))
            .ThenBy(static document => document.Version)
            .Select(static document => document.Id)
            .Concat(model.Values.Where(static document => document.XYPoints.Length == 0)
                .OrderBy(static document => document.Version)
                .Select(static document => document.Id))
            .ToArray();

        TopDocs actualGeo = searcher.Search(matchAll, model.Count + 1, geoSort);
        TopDocs actualXy = searcher.Search(matchAll, model.Count + 1, xySort);
        Assert.Equal(model.Count, actualGeo.TotalHits);
        Assert.Equal(model.Count, actualXy.TotalHits);
        Assert.Equal(expectedGeo, GetIds(searcher, actualGeo));
        Assert.Equal(expectedXy, GetIds(searcher, actualXy));

        var geoBounds = new GeoBoundingBoxQuery("location", -20, 40, -80, 10);
        var expectedGeoBounds = model.Values
            .Where(document => document.GeoPoints.Any(point =>
                point.Latitude >= -20 && point.Latitude <= 40
                && point.Longitude >= -80 && point.Longitude <= 10))
            .Select(static document => document.Id)
            .ToHashSet(StringComparer.Ordinal);
        var actualGeoBounds = searcher.Search(geoBounds, model.Count + 1);
        Assert.Equal(expectedGeoBounds, GetIds(searcher, actualGeoBounds).ToHashSet(StringComparer.Ordinal));

        var geoDistance = new GeoDistanceQuery("location", geoOrigin.Latitude, geoOrigin.Longitude, 3_500_000);
        var expectedGeoDistance = model.Values
            .Where(document => document.GeoPoints.Any(point => GeoEncodingUtils.HaversineDistance(
                geoOrigin.Latitude, geoOrigin.Longitude, point.Latitude, point.Longitude) <= 3_500_000))
            .Select(static document => document.Id)
            .ToHashSet(StringComparer.Ordinal);
        TopDocs actualGeoDistance = searcher.Search(geoDistance, model.Count + 1);
        Assert.Equal(expectedGeoDistance, GetIds(searcher, actualGeoDistance).ToHashSet(StringComparer.Ordinal));

        var xyBounds = new XYRectangle(-50, -25, 25, 40);
        var expectedXyBounds = model.Values
            .Where(document => document.XYPoints.Any(point =>
                point.X >= xyBounds.MinX && point.X <= xyBounds.MaxX
                && point.Y >= xyBounds.MinY && point.Y <= xyBounds.MaxY))
            .Select(static document => document.Id)
            .ToHashSet(StringComparer.Ordinal);
        var actualXyBounds = searcher.Search(new XYBoundingBoxQuery("position", xyBounds), model.Count + 1);
        Assert.Equal(expectedXyBounds, GetIds(searcher, actualXyBounds).ToHashSet(StringComparer.Ordinal));

        var xyDistance = new XYDistanceQuery("position", xyOrigin, 400);
        var expectedXyDistance = model.Values
            .Where(document => document.XYPoints.Any(point =>
            {
                double dx = point.X - xyOrigin.X;
                double dy = point.Y - xyOrigin.Y;
                return dx * dx + dy * dy <= 400 * 400;
            }))
            .Select(static document => document.Id)
            .ToHashSet(StringComparer.Ordinal);
        TopDocs actualXyDistance = searcher.Search(xyDistance, model.Count + 1);
        Assert.Equal(expectedXyDistance, GetIds(searcher, actualXyDistance).ToHashSet(StringComparer.Ordinal));
    }

    private static string[] GetIds(IndexSearcher searcher, TopDocs documents)
        => documents.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .ToArray();

    private static ModelDocument CreateModelDocument(string id, long version, Random random)
    {
        GeoPoint[] geoPoints = random.Next(5) == 0
            ? []
            : Enumerable.Range(0, random.Next(1, 4))
                .Select(_ => new GeoPoint(random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180))
                .ToArray();
        XYPoint[] xyPoints = random.Next(5) == 0
            ? []
            : Enumerable.Range(0, random.Next(1, 4))
                .Select(_ => new XYPoint((float)(random.NextDouble() * 2_000 - 1_000), (float)(random.NextDouble() * 2_000 - 1_000)))
                .ToArray();
        return new ModelDocument(id, version, geoPoints, xyPoints);
    }

    private static LeanDocument CreateDocument(ModelDocument state)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", state.Id));
        foreach (GeoPoint point in state.GeoPoints)
            document.Add(new GeoPointField("location", point.Latitude, point.Longitude));
        foreach (XYPoint point in state.XYPoints)
            document.Add(new XYPointField("position", point.X, point.Y));
        return document;
    }

    private sealed record ModelDocument(string Id, long Version, GeoPoint[] GeoPoints, XYPoint[] XYPoints);
}
