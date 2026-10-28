using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.Sorting;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialDistanceSortTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_distance_sort_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact]
    public void GeoSortUsesMinimumPointDistanceAndKeepsMissingLastInBothDirections()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var multi = new LeanDocument();
            multi.Add(new GeoPointField("location", 0, 5));
            multi.Add(new GeoPointField("location", 0, 1));
            writer.AddDocument(multi);
            AddGeo(writer, 0, 2);
            writer.AddDocument(new LeanDocument());
            AddGeo(writer, 0, -1);
            writer.AddDocument(new LeanDocument());
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directoryReader);
        var ascending = SortField.GeoDistance("location", new GeoPoint(0, 0));
        var descending = SortField.GeoDistance("location", new GeoPoint(0, 0), descending: true);
        var query = new MatchAllDocsQuery();

        TopDocs ascendingResults = searcher.Search(query, 10, ascending);
        TopDocs descendingResults = searcher.Search(query, 10, descending);

        AssertDocIds([0, 3, 1, 2, 4], ascendingResults);
        AssertDocIds([1, 0, 3, 2, 4], descendingResults);
        Assert.Equal(5, ascendingResults.TotalHits);

        SearchAfterValue[] missingBoundary = searcher.CaptureSortValues(ascendingResults.ScoreDocs[3], [ascending]);
        Assert.True(missingBoundary[0].IsMissing);
        Assert.Equal(0, missingBoundary[0].NumericValue);
    }

    [Fact]
    public void XYSortUsesMinimumPointDistanceAndOrdersCompoundFieldsFirst()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var first = new LeanDocument();
            first.Add(new NumericField("rank", 1));
            first.Add(new XYPointField("position", 3, 4));
            first.Add(new XYPointField("position", 1, 0));
            writer.AddDocument(first);

            var second = new LeanDocument();
            second.Add(new NumericField("rank", 1));
            second.Add(new XYPointField("position", -1, 0));
            writer.AddDocument(second);

            var third = new LeanDocument();
            third.Add(new NumericField("rank", 0));
            third.Add(new XYPointField("position", 10, 0));
            writer.AddDocument(third);

            var missing = new LeanDocument();
            missing.Add(new NumericField("rank", 1));
            writer.AddDocument(missing);
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directoryReader);
        var query = new MatchAllDocsQuery();
        var distance = SortField.XYDistance("position", new XYPoint(0, 0));
        TopDocs ascending = searcher.Search(query, 10, distance);
        TopDocs descending = searcher.Search(
            query,
            10,
            SortField.XYDistance("position", new XYPoint(0, 0), descending: true));
        TopDocs compound = searcher.Search(
            query,
            10,
            new SortField[] { SortField.Numeric("rank"), distance });

        AssertDocIds([0, 1, 2, 3], ascending);
        AssertDocIds([2, 0, 1, 3], descending);
        AssertDocIds([2, 0, 1, 3], compound);
        Assert.Equal(1, searcher.CaptureSortValues(ascending.ScoreDocs[0], [distance])[0].NumericValue);
    }

    [Fact]
    public void SearchAfterAndSessionCursorsKeepSpatialSortOriginAndMissingState()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddGeo(writer, 0, 1);
            AddGeo(writer, 0, 2);
            writer.AddDocument(new LeanDocument());
            writer.AddDocument(new LeanDocument());
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directoryReader);
        var query = new MatchAllDocsQuery();
        var sort = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs full = searcher.Search(query, 10, sort);
        TopDocs first = searcher.Search(query, 2, sort);
        TopDocs second = searcher.SearchAfter(first.ScoreDocs[^1], query, 2, sort);
        var boundary = searcher.CaptureSortValues(first.ScoreDocs[^1], [sort]);
        TopDocs explicitSecond = searcher.SearchAfter(boundary, query, 2, [sort]);

        AssertDocIds(full.ScoreDocs.Select(static hit => hit.DocId),
            new TopDocs(full.TotalHits, first.ScoreDocs.Concat(second.ScoreDocs).ToArray()));
        AssertDocIds(second.ScoreDocs.Select(static hit => hit.DocId), explicitSecond);
        Assert.True(searcher.CaptureSortValues(full.ScoreDocs[^1], [sort])[0].IsMissing);

        using var manager = new SearcherManager(directoryReader);
        using var sessions = new SearchSessionManager(manager);
        using var session = sessions.OpenSession();
        SearchSessionPage firstPage = session.Search(query, 1, sorts: [sort]);
        Assert.NotNull(firstPage.NextCursor);

        SearchSessionPage secondPage = session.Search(query, 1, firstPage.NextCursor, [sort]);
        SearchSessionPage thirdPage = session.Search(query, 1, secondPage.NextCursor, [sort]);
        SearchSessionPage fourthPage = session.Search(query, 1, thirdPage.NextCursor, [sort]);
        var sessionPageResults = firstPage.Results.ScoreDocs
            .Concat(secondPage.Results.ScoreDocs)
            .Concat(thirdPage.Results.ScoreDocs)
            .Concat(fourthPage.Results.ScoreDocs)
            .Select(static hit => hit.DocId);
        Assert.Equal(full.ScoreDocs.Select(static hit => hit.DocId), sessionPageResults);

        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            SearchSessionPage nextPage = session.Search(query, 1, firstPage.NextCursor, [sort]);
            Assert.NotEmpty(nextPage.Results.ScoreDocs);
            Assert.Throws<SearchSessionException>(() => session.Search(
                query,
                1,
                firstPage.NextCursor,
                [SortField.GeoDistance("location", new GeoPoint(1, 1))]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static void AddGeo(IndexWriter writer, double latitude, double longitude)
    {
        var document = new LeanDocument();
        document.Add(new GeoPointField("location", latitude, longitude));
        writer.AddDocument(document);
    }

    private static void AssertDocIds(IEnumerable<int> expected, TopDocs actual)
        => Assert.Equal(expected, actual.ScoreDocs.Select(static hit => hit.DocId));
}
