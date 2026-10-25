using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SortedSearchResourceControlTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SortedSearchResourceControlTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void SingleSort_PreCancelledOptions_ReturnsEmptyPartialResults()
    {
        using var directory = CreateDirectory(nameof(SingleSort_PreCancelledOptions_ReturnsEmptyPartialResults));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddSortableDocument(writer, rank: 1, tag: "one");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        TopDocs result = searcher.Search(
            new MatchAllDocsQuery(),
            topN: 1,
            SortField.Numeric("rank"),
            new SearchOptions { CancellationToken = cancellation.Token });

        Assert.True(result.IsPartial);
        Assert.Equal(0, result.TotalHits);
        Assert.Empty(result.ScoreDocs);
    }

    [Fact]
    public void MultiSort_PreCancelledOptions_ReturnsEmptyPartialResults()
    {
        using var directory = CreateDirectory(nameof(MultiSort_PreCancelledOptions_ReturnsEmptyPartialResults));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddSortableDocument(writer, rank: 1, tag: "one");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        TopDocs result = searcher.Search(
            new MatchAllDocsQuery(),
            topN: 1,
            [SortField.Numeric("rank"), SortField.String("tag")],
            new SearchOptions { CancellationToken = cancellation.Token });

        Assert.True(result.IsPartial);
        Assert.Equal(0, result.TotalHits);
        Assert.Empty(result.ScoreDocs);
    }

    [Fact]
    public void MultiSort_ZeroTimeout_ReturnsEmptyPartialResults()
    {
        using var directory = CreateDirectory(nameof(MultiSort_ZeroTimeout_ReturnsEmptyPartialResults));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddSortableDocument(writer, rank: 1, tag: "one");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        TopDocs result = searcher.Search(
            new MatchAllDocsQuery(),
            topN: 1,
            [SortField.Numeric("rank"), SortField.String("tag")],
            new SearchOptions { Timeout = TimeSpan.Zero });

        AssertEmptyPartial(result);
    }

    [Fact]
    public void SingleSort_ZeroTimeout_ReturnsEmptyPartialResults()
    {
        using var directory = CreateDirectory(nameof(SingleSort_ZeroTimeout_ReturnsEmptyPartialResults));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddSortableDocument(writer, rank: 1, tag: "one");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);

        TopDocs result = searcher.Search(
            new MatchAllDocsQuery(),
            topN: 1,
            SortField.Numeric("rank"),
            new SearchOptions { Timeout = TimeSpan.Zero });

        Assert.True(result.IsPartial);
        Assert.Equal(0, result.TotalHits);
        Assert.Empty(result.ScoreDocs);
    }

    [Fact]
    public void BestFirstGeoAndXY_PreCancelledAndZeroTimeout_ReturnEmptyPartialResults()
    {
        using var directory = CreateDirectory(nameof(BestFirstGeoAndXY_PreCancelledAndZeroTimeout_ReturnEmptyPartialResults));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            AddGeoDocument(writer, "geo", rank: 1, tag: "geo", latitude: 0, longitude: 0.2);
            writer.Commit();
            AddXYDocument(writer, "xy", rank: 2, tag: "xy", x: 0.2, y: 0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        foreach (SortField sort in new[]
                 {
                     SortField.GeoDistance("location", new GeoPoint(0, 0)),
                     SortField.XYDistance("location", new XYPoint(0, 0))
                 })
        {
            TopDocs cancelled = searcher.Search(
                new MatchAllDocsQuery(),
                1,
                sort,
                new SearchOptions { CancellationToken = cancellation.Token });
            AssertEmptyPartial(cancelled);

            TopDocs expired = searcher.Search(
                new MatchAllDocsQuery(),
                1,
                sort,
                new SearchOptions { Timeout = TimeSpan.Zero });
            AssertEmptyPartial(expired);
        }
    }

    [Fact]
    public void BestFirstConstantScore_CancelledBeforeTraversal_PreservesCompletedFilterHits()
    {
        using var directory = CreateDirectory(nameof(BestFirstConstantScore_CancelledBeforeTraversal_PreservesCompletedFilterHits));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddGeoDocument(writer, "a", rank: 1, tag: "a", latitude: 0, longitude: 0.1, body: "matching");
            AddGeoDocument(writer, "b", rank: 2, tag: "b", latitude: 0, longitude: 0.2, body: "matching");
            AddGeoDocument(writer, "c", rank: 3, tag: "c", latitude: 0, longitude: 0.3, body: "matching");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        using var cancellation = new CancellationTokenSource();
        int traversals = 0;
        searcher.SearchExecutionCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.AfterFilterBitmap)
                cancellation.Cancel();
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.BeforeSpatialTraversal)
                traversals++;
        };

        var filter = new ConstantScoreQuery(new WildcardQuery("body", "match*"));
        TopDocs result = searcher.Search(
            filter,
            2,
            SortField.GeoDistance("location", new GeoPoint(0, 0)),
            new SearchOptions { CancellationToken = cancellation.Token });

        Assert.True(result.IsPartial);
        Assert.Equal(3, result.TotalHits);
        Assert.Empty(result.ScoreDocs);
        Assert.Equal(0, traversals);
    }

    [Fact]
    public void BestFirstConstantScore_PreCancelledAndZeroTimeout_SkipFilterPreparation()
    {
        using var directory = CreateDirectory(nameof(BestFirstConstantScore_PreCancelledAndZeroTimeout_SkipFilterPreparation));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddGeoDocument(writer, "one", 1, "one", 0, 0.1, body: "matching");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int filterBitmaps = 0;
        int traversals = 0;
        searcher.SearchExecutionCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.BeforeFilterBitmap)
                filterBitmaps++;
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.BeforeSpatialTraversal)
                traversals++;
        };
        var query = new ConstantScoreQuery(new WildcardQuery("body", "match*"));
        var sort = SortField.GeoDistance("location", new GeoPoint(0, 0));

        AssertEmptyPartial(searcher.Search(
            query,
            1,
            sort,
            new SearchOptions { CancellationToken = cancellation.Token }));
        AssertEmptyPartial(searcher.Search(
            query,
            1,
            sort,
            new SearchOptions { Timeout = TimeSpan.Zero }));
        Assert.Equal(0, filterBitmaps);
        Assert.Equal(0, traversals);
    }

    [Fact]
    public void ConstantScoreNearest_WithoutResourceLimit_HasExactHitsAndScores()
    {
        using var directory = CreateDirectory(nameof(ConstantScoreNearest_WithoutResourceLimit_HasExactHitsAndScores));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddGeoDocument(writer, "far", rank: 1, tag: "far", latitude: 0, longitude: 0.3, body: "matching");
            AddGeoDocument(writer, "near", rank: 2, tag: "near", latitude: 0, longitude: 0.1, body: "matching");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        TopDocs result = searcher.Search(
            new ConstantScoreQuery(new WildcardQuery("body", "match*")),
            10,
            SortField.GeoDistance("location", new GeoPoint(0, 0)),
            SearchOptions.Default);

        Assert.False(result.IsPartial);
        Assert.Equal(2, result.TotalHits);
        Assert.Equal("near", searcher.GetStoredFields(result.ScoreDocs[0].DocId)["id"][0]);
        Assert.All(result.ScoreDocs, static hit => Assert.Equal(1f, hit.Score));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpatialFallback_PreCancelledOrZeroTimeout_ReturnsEmptyPartialResults(bool useZeroTimeout)
    {
        using var directory = CreateDirectory($"{nameof(SpatialFallback_PreCancelledOrZeroTimeout_ReturnsEmptyPartialResults)}_{useZeroTimeout}");
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            AddGeoDocument(writer, "geo", rank: 1, tag: "geo", latitude: 0, longitude: 0.2);
            writer.Commit();
            AddXYDocument(writer, "xy", rank: 2, tag: "xy", x: 0.2, y: 0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        SearchOptions options;
        if (useZeroTimeout)
        {
            options = new SearchOptions { Timeout = TimeSpan.Zero };
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            options = new SearchOptions { CancellationToken = cancellation.Token };
        }

        AssertEmptyPartial(searcher.Search(
            new TermQuery("body", "match"),
            1,
            SortField.GeoDistance("location", new GeoPoint(0, 0)),
            options));
        AssertEmptyPartial(searcher.Search(
            new TermQuery("body", "match"),
            1,
            SortField.XYDistance("location", new XYPoint(0, 0)),
            options));
    }

    [Fact]
    public void SingleSortFallback_GeoAndXYOrderMatchesIndependentDistanceOracle()
    {
        using var geoDirectory = CreateDirectory(nameof(SingleSortFallback_GeoAndXYOrderMatchesIndependentDistanceOracle) + "_geo");
        using (var writer = new IndexWriter(geoDirectory, new IndexWriterConfig()))
        {
            AddGeoDocument(writer, "g0", 0, "g", 0, 0.6);
            AddGeoDocument(writer, "g1", 0, "g", 0, -0.2);
            AddGeoDocument(writer, "g2", 0, "g", 0, 0.1);
            AddGeoDocument(writer, "g3", 0, "g", 0, 0.4);
            AddGeoDocument(writer, "g4", 0, "g", 0, -0.3);
            writer.Commit();
        }

        using (var searcher = new IndexSearcher(geoDirectory))
        {
            var query = new TermQuery("body", "match");
            TopDocs result = searcher.Search(
                query,
                5,
                SortField.GeoDistance("location", new GeoPoint(0, 0)),
                SearchOptions.Default);
            Assert.Equal(["g2", "g1", "g4", "g3", "g0"], GetIds(searcher, result));
            var scoresById = searcher.Search(query, 5).ScoreDocs
                .ToDictionary(hit => GetId(searcher, hit), static hit => hit.Score, StringComparer.Ordinal);
            Assert.Equal(scoresById["g2"], result.ScoreDocs[0].Score);
            Assert.Equal(scoresById["g1"], result.ScoreDocs[1].Score);
            Assert.Equal(scoresById["g4"], result.ScoreDocs[2].Score);
        }

        using var xyDirectory = CreateDirectory(nameof(SingleSortFallback_GeoAndXYOrderMatchesIndependentDistanceOracle) + "_xy");
        using (var writer = new IndexWriter(xyDirectory, new IndexWriterConfig()))
        {
            AddXYDocument(writer, "x0", 0, "x", 10, 0);
            AddXYDocument(writer, "x1", 0, "x", 2, 0);
            AddXYDocument(writer, "x2", 0, "x", -1, 0);
            AddXYDocument(writer, "x3", 0, "x", 7, 0);
            AddXYDocument(writer, "x4", 0, "x", -3, 0);
            writer.Commit();
        }

        using var xySearcher = new IndexSearcher(xyDirectory);
        var xyQuery = new TermQuery("body", "match");
        TopDocs xyResult = xySearcher.Search(
            xyQuery,
            5,
            SortField.XYDistance("location", new XYPoint(0, 0)),
            SearchOptions.Default);
        Assert.Equal(["x2", "x1", "x4", "x3", "x0"], GetIds(xySearcher, xyResult));
        var xyScoresById = xySearcher.Search(xyQuery, 5).ScoreDocs
            .ToDictionary(hit => GetId(xySearcher, hit), static hit => hit.Score, StringComparer.Ordinal);
        Assert.Equal(xyScoresById["x2"], xyResult.ScoreDocs[0].Score);
        Assert.Equal(xyScoresById["x1"], xyResult.ScoreDocs[1].Score);
        Assert.Equal(xyScoresById["x4"], xyResult.ScoreDocs[2].Score);
    }

    [Fact]
    public void MultiSort_MatchesIndependentOracleAndSearchAfterKeepsMissingBoundaryStable()
    {
        using var directory = CreateDirectory(nameof(MultiSort_MatchesIndependentOracleAndSearchAfterKeepsMissingBoundaryStable));
        var rows = new List<SpatialSortRow>();
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            rows.Add(new SpatialSortRow("geo-near", 0.1, 2, "b"));
            rows.Add(new SpatialSortRow("geo-mid", 0.3, 2, "a"));
            rows.Add(new SpatialSortRow("geo-far", 0.5, 1, "a"));
            rows.Add(new SpatialSortRow("geo-missing", null, 5, "z"));
            foreach (SpatialSortRow row in rows)
                AddGeoDocument(writer, row.Id, row.Rank, row.Tag, 0, row.GeoLongitude ?? 0, includePoint: row.GeoLongitude.HasValue);
            writer.Commit();

            rows.Add(new SpatialSortRow("xy-near", null, 9, "x"));
            rows.Add(new SpatialSortRow("xy-equal-a", null, 4, "same"));
            rows.Add(new SpatialSortRow("xy-equal-b", null, 4, "same"));
            foreach (SpatialSortRow row in rows.Skip(4))
                AddXYDocument(writer, row.Id, row.Rank, row.Tag, x: 0, y: 0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var sorts = new[]
        {
            SortField.GeoDistance("location", new GeoPoint(0, 0), descending: true),
            SortField.Numeric("rank", descending: true),
            SortField.String("tag")
        };
        var query = new MatchAllDocsQuery();
        TopDocs all = searcher.Search(query, rows.Count, sorts);
        var globalDocById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ScoreDoc hit in searcher.Search(query, rows.Count).ScoreDocs)
            globalDocById[GetId(searcher, hit)] = hit.DocId;

        string[] expected = rows
            .OrderBy(static row => row.GeoLongitude.HasValue ? 0 : 1)
            .ThenByDescending(static row => row.GeoLongitude.HasValue ? Math.Abs(row.GeoLongitude.Value) : 0)
            .ThenByDescending(static row => row.Rank)
            .ThenBy(static row => row.Tag, StringComparer.Ordinal)
            .ThenBy(row => globalDocById[row.Id])
            .Select(static row => row.Id)
            .ToArray();
        Assert.Equal(rows.Count, all.TotalHits);
        Assert.Equal(expected, GetIds(searcher, all));

        int validGeoCount = rows.Count(static row => row.GeoLongitude.HasValue);
        TopDocs firstPage = searcher.Search(query, validGeoCount + 1, sorts);
        SearchAfterValue[] boundaryValues = searcher.CaptureSortValues(firstPage.ScoreDocs[^1], sorts);
        Assert.True(boundaryValues[0].IsMissing);
        TopDocs secondPage = searcher.SearchAfter(firstPage.ScoreDocs[^1], query, rows.Count, sorts);
        Assert.Equal(expected, GetIds(searcher, firstPage).Concat(GetIds(searcher, secondPage)));
    }

    [Fact]
    public void SingleAndMultiSort_CancellationAfterFirstSegmentReturnsOnlyCompletedWork()
    {
        using var directory = CreateThreeSegmentIndex(nameof(SingleAndMultiSort_CancellationAfterFirstSegmentReturnsOnlyCompletedWork));

        AssertFirstSegmentPartial((searcher, options) => searcher.Search(
            new TermQuery("body", "match"),
            2,
            SortField.Numeric("rank"),
            options));

        AssertFirstSegmentPartial((searcher, options) => searcher.Search(
            new TermQuery("body", "match"),
            2,
            [SortField.Numeric("rank"), SortField.String("tag")],
            options));
    }

    [Fact]
    public void MultiSort_UnboundedOptionsRetainParallelSegmentCollection()
    {
        using var directory = CreateThreeSegmentIndex(nameof(MultiSort_UnboundedOptionsRetainParallelSegmentCollection));
        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            ParallelSearch = true,
            MaxConcurrency = 3
        });
        searcher.EnableSortedSearchDiagnosticsForTesting = true;

        TopDocs result = searcher.Search(
            new TermQuery("body", "match"),
            2,
            [SortField.Numeric("rank"), SortField.String("tag")],
            SearchOptions.Default);

        Assert.False(result.IsPartial);
        Assert.Equal(3, result.TotalHits);
        Assert.Equal(2, result.ScoreDocs.Length);
        Assert.Equal(["doc-0", "doc-1"], GetIds(searcher, result));
        Assert.Equal(2, searcher.LastSortedSearchPeakCandidateCountForTesting);
    }

    [Fact]
    public void IndexSortEarlyTermination_IsBoundedAndStopsAtSegmentCheckpoints()
    {
        using var directory = CreateDirectory(nameof(IndexSortEarlyTermination_IsBoundedAndStopsAtSegmentCheckpoints));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
               {
                   IndexSort = new IndexSort(SortField.Numeric("rank")),
                   MergePolicy = NoMergePolicy.Instance
               }))
        {
            for (int segment = 0; segment < 10; segment++)
            {
                for (int item = 0; item < 4; item++)
                    AddSortableDocument(writer, segment * 4 + item, $"tag-{segment:D2}", $"{segment:D2}-{item}");
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(directory);
        searcher.EnableSortedSearchDiagnosticsForTesting = true;
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        AssertEmptyPartial(searcher.Search(
            new TermQuery("body", "match"),
            3,
            SortField.Numeric("rank"),
            new SearchOptions { CancellationToken = preCancelled.Token }));
        AssertEmptyPartial(searcher.Search(
            new TermQuery("body", "match"),
            3,
            SortField.Numeric("rank"),
            new SearchOptions { Timeout = TimeSpan.Zero }));

        TopDocs normal = searcher.Search(
            new TermQuery("body", "match"),
            3,
            SortField.Numeric("rank"),
            SearchOptions.Default);
        Assert.True(normal.IsPartial);
        Assert.Equal(30, normal.TotalHits);
        Assert.Equal(3, searcher.LastSortedSearchPeakCandidateCountForTesting);
        Assert.Equal(["00-0", "00-1", "00-2"], GetIds(searcher, normal));

        using var cancellation = new CancellationTokenSource();
        int startedSegments = 0;
        int completedSegments = 0;
        searcher.SearchExecutionCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.BeforeSegment)
                startedSegments++;
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.AfterSegment
                && ++completedSegments == 1)
                cancellation.Cancel();
        };
        TopDocs partial = searcher.Search(
            new TermQuery("body", "match"),
            3,
            SortField.Numeric("rank"),
            new SearchOptions { CancellationToken = cancellation.Token });
        Assert.True(partial.IsPartial);
        Assert.Equal(3, partial.TotalHits);
        Assert.Equal(1, startedSegments);
        Assert.Equal(1, completedSegments);
        Assert.Equal(3, searcher.LastSortedSearchPeakCandidateCountForTesting);
    }

    [Fact]
    public void MaxResultBytes_RejectsOversizedSortedHeapBeforeAnySearchCheckpoint()
    {
        using var directory = CreateDirectory(nameof(MaxResultBytes_RejectsOversizedSortedHeapBeforeAnySearchCheckpoint));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddGeoDocument(writer, "one", 1, "one", 0, 0.1);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        bool searchStarted = false;
        searcher.SearchExecutionCheckpointForTesting = _ => searchStarted = true;
        var options = SearchOptions.WithBudget(2 * ScoreDoc.EstimatedBytes);
        var query = new MatchAllDocsQuery();

        Assert.Throws<ArgumentException>(() => searcher.Search(query, 3, SortField.Numeric("rank"), options));
        Assert.Throws<ArgumentException>(() => searcher.Search(
            query,
            3,
            [SortField.Numeric("rank"), SortField.String("tag")],
            options));
        Assert.Throws<ArgumentException>(() => searcher.Search(
            query,
            3,
            SortField.GeoDistance("location", new GeoPoint(0, 0)),
            options));
        Assert.False(searchStarted);
    }

    [Fact]
    public void LargeSortedQueries_RetainOnlyTopN_ForSingleMultiAndSpatialPaths()
    {
        using var directory = CreateDirectory(nameof(LargeSortedQueries_RetainOnlyTopN_ForSingleMultiAndSpatialPaths));
        const int documentCount = 100_000;
        const int topN = 10;
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            for (int i = 0; i < documentCount; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "match"));
                document.Add(new NumericField("rank", documentCount - i));
                document.Add(new StringField("tag", $"tag-{i % 100:D3}"));
                document.Add(new GeoPointField("location", 0, i * 0.000001));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        searcher.EnableSortedSearchDiagnosticsForTesting = true;
        var options = SearchOptions.WithBudget(topN * ScoreDoc.EstimatedBytes);
        var termQuery = new TermQuery("body", "match");

        TopDocs numeric = searcher.Search(termQuery, topN, SortField.Numeric("rank"), options);
        Assert.Equal(documentCount, numeric.TotalHits);
        Assert.Equal(topN, numeric.ScoreDocs.Length);
        Assert.InRange(searcher.LastSortedSearchPeakCandidateCountForTesting, 1, topN);

        TopDocs geoFallback = searcher.Search(
            termQuery,
            topN,
            SortField.GeoDistance("location", new GeoPoint(0, 0)),
            options);
        Assert.Equal(documentCount, geoFallback.TotalHits);
        Assert.Equal(topN, geoFallback.ScoreDocs.Length);
        Assert.InRange(searcher.LastSortedSearchPeakCandidateCountForTesting, 1, topN);

        TopDocs multi = searcher.Search(
            termQuery,
            topN,
            [SortField.Numeric("rank"), SortField.String("tag")],
            options);
        Assert.Equal(documentCount, multi.TotalHits);
        Assert.Equal(topN, multi.ScoreDocs.Length);
        Assert.InRange(searcher.LastSortedSearchPeakCandidateCountForTesting, 1, topN);

        TopDocs bestFirst = searcher.Search(
            new MatchAllDocsQuery(),
            topN,
            SortField.GeoDistance("location", new GeoPoint(0, 0)),
            options);
        Assert.Equal(documentCount, bestFirst.TotalHits);
        Assert.Equal(topN, bestFirst.ScoreDocs.Length);
        Assert.InRange(searcher.LastSortedSearchPeakCandidateCountForTesting, 1, topN);
    }

    private static void AssertEmptyPartial(TopDocs result)
    {
        Assert.True(result.IsPartial);
        Assert.Equal(0, result.TotalHits);
        Assert.Empty(result.ScoreDocs);
    }

    private void AssertFirstSegmentPartial(Func<IndexSearcher, SearchOptions, TopDocs> search)
    {
        using var directory = CreateThreeSegmentIndex($"first-segment-{Guid.NewGuid():N}");
        using var searcher = new IndexSearcher(directory);
        Assert.Equal(3, searcher.GetSegmentReaders().Count);
        using var cancellation = new CancellationTokenSource();
        int startedSegments = 0;
        int completedSegments = 0;
        searcher.SearchExecutionCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.BeforeSegment)
                startedSegments++;
            if (checkpoint == IndexSearcher.SearchExecutionCheckpoint.AfterSegment
                && ++completedSegments == 1)
                cancellation.Cancel();
        };
        TopDocs result = search(searcher, new SearchOptions { CancellationToken = cancellation.Token });
        Assert.True(result.IsPartial);
        Assert.Equal(1, result.TotalHits);
        Assert.Single(result.ScoreDocs);
        Assert.Equal(1, startedSegments);
        Assert.Equal(1, completedSegments);
    }

    private MMapDirectory CreateThreeSegmentIndex(string name)
    {
        var directory = CreateDirectory(name);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
        for (int i = 0; i < 3; i++)
        {
            AddSortableDocument(writer, i, $"tag-{i}", $"doc-{i}");
            writer.Commit();
        }
        return directory;
    }

    private static void AddGeoDocument(
        IndexWriter writer,
        string id,
        double rank,
        string tag,
        double latitude,
        double longitude,
        bool includePoint = true,
        string body = "match")
    {
        var document = CreateSortableDocument(id, rank, tag, body);
        if (includePoint)
            document.Add(new GeoPointField("location", latitude, longitude));
        writer.AddDocument(document);
    }

    private static void AddXYDocument(
        IndexWriter writer,
        string id,
        double rank,
        string tag,
        double x,
        double y,
        string body = "match")
    {
        var document = CreateSortableDocument(id, rank, tag, body);
        document.Add(new XYPointField("location", (float)x, (float)y));
        writer.AddDocument(document);
    }

    private MMapDirectory CreateDirectory(string name)
    {
        string path = Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new MMapDirectory(path);
    }

    private static void AddSortableDocument(IndexWriter writer, double rank, string tag, string id = "item")
        => writer.AddDocument(CreateSortableDocument(id, rank, tag));

    private static LeanDocument CreateSortableDocument(string id, double rank, string tag, string body = "match")
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        document.Add(new NumericField("rank", rank));
        document.Add(new StringField("tag", tag));
        document.Add(new StringField("id", id));
        return document;
    }

    private static string[] GetIds(IndexSearcher searcher, TopDocs results)
        => results.ScoreDocs.Select(hit => GetId(searcher, hit)).ToArray();

    private static string GetId(IndexSearcher searcher, ScoreDoc hit)
        => searcher.GetStoredFields(hit.DocId)["id"][0];

    private sealed record SpatialSortRow(string Id, double? GeoLongitude, int Rank, string Tag);
}
