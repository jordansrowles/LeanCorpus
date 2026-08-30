using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Integration coverage for UTC date histogram facets.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class DateHistogramFacetTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public DateHistogramFacetTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Date Histogram Facets: Fixed Intervals Deduplicate Documents And Handle PreEpoch")]
    public void FixedIntervals_DeduplicateDocumentsAndHandlePreEpoch()
    {
        var path = SubDir(nameof(FixedIntervals_DeduplicateDocumentsAndHandlePreEpoch));
        var epochHour = DateTimeOffset.UnixEpoch;
        using (var writer = CreateWriter(path))
        {
            AddDocument(writer, "same-hour", epochHour.AddMinutes(5), epochHour.AddMinutes(50));
            AddDocument(writer, "next-hour", epochHour.AddHours(1));
            AddDocument(writer, "pre-epoch", epochHour.AddMinutes(-1));
            AddDocument(writer, "missing");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new DateHistogramFacetRequest("date", DateHistogramInterval.Hour, includeMissing: true)]);

        var histogram = Assert.Single(facets);
        var buckets = histogram.DateHistogramBuckets;
        Assert.NotNull(buckets);
        Assert.Equal(3, buckets.Count);
        Assert.Equal(1, buckets.Single(bucket => bucket.Start == epochHour.AddHours(-1)).Count);
        Assert.Equal(1, buckets.Single(bucket => bucket.Start == epochHour).Count);
        Assert.Equal(1, buckets.Single(bucket => bucket.Start == epochHour.AddHours(1)).Count);
        Assert.All(buckets, bucket => Assert.Equal(bucket.Start.AddHours(1), bucket.End));
        Assert.Equal(1, histogram.MissingCount);
    }

    [Fact(DisplayName = "Date Histogram Facets: UTC Calendar Boundaries Use ISO Weeks And Calendar Arithmetic")]
    public void CalendarIntervals_UseUtcIsoWeeksAndCalendarArithmetic()
    {
        var path = SubDir(nameof(CalendarIntervals_UseUtcIsoWeeksAndCalendarArithmetic));
        using (var writer = CreateWriter(path))
        {
            AddDocument(writer, "leap-day", new DateTimeOffset(2024, 2, 29, 18, 0, 0, TimeSpan.Zero));
            AddDocument(writer, "march", new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
            AddDocument(writer, "year-end", new DateTimeOffset(2024, 12, 31, 12, 0, 0, TimeSpan.Zero));
            AddDocument(writer, "year-start", new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var query = new TermQuery("body", "common");
        IReadOnlyList<DateHistogramBucket> GetBuckets(DateHistogramCalendarInterval interval)
        {
            var (_, results) = searcher.SearchWithFacetRequests(query, 1,
                [new DateHistogramFacetRequest("date", DateHistogramInterval.Calendar(interval))]);
            var buckets = Assert.Single(results).DateHistogramBuckets;
            Assert.NotNull(buckets);
            return buckets!;
        }

        var day = GetBuckets(DateHistogramCalendarInterval.Day);
        Assert.Contains(day, bucket => bucket.Start == new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)
            && bucket.End == new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var week = GetBuckets(DateHistogramCalendarInterval.Week);
        Assert.Contains(week, bucket => bucket.Start == new DateTimeOffset(2024, 2, 26, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains(week, bucket => bucket.Start == new DateTimeOffset(2024, 12, 30, 0, 0, 0, TimeSpan.Zero) && bucket.Count == 2);

        var month = GetBuckets(DateHistogramCalendarInterval.Month);
        Assert.Contains(month, bucket => bucket.Start == new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)
            && bucket.End == new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var quarter = GetBuckets(DateHistogramCalendarInterval.Quarter);
        Assert.Contains(quarter, bucket => bucket.Start == new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            && bucket.End == new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero));

        var year = GetBuckets(DateHistogramCalendarInterval.Year);
        Assert.Contains(year, bucket => bucket.Start == new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            && bucket.End == new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) && bucket.Count == 3);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriter CreateWriter(string path)
        => new(new MMapDirectory(path), new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance });

    private static void AddDocument(IndexWriter writer, string id, params DateTimeOffset[] values)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "common"));
        foreach (DateTimeOffset value in values)
            document.Add(new Int64Field("date", value.ToUnixTimeMilliseconds(), stored: false));
        writer.AddDocument(document);
    }
}
