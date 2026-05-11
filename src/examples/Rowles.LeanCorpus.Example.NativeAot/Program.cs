using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

var rootPath = Path.Combine(Path.GetTempPath(), "leancorpus-aot-smoke-" + Guid.NewGuid().ToString("N"));

try
{
    RunPolicy(FieldCompressionPolicy.None, rootPath);
    RunPolicy(FieldCompressionPolicy.Deflate, rootPath);
    RunPolicy(FieldCompressionPolicy.Brotli, rootPath);

    Directory.Delete(rootPath, recursive: true);
    Console.WriteLine("LeanCorpus Native AOT smoke passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static void RunPolicy(FieldCompressionPolicy policy, string rootPath)
{
    var indexPath = Path.Combine(rootPath, policy.ToString());

    try
    {
        Directory.CreateDirectory(indexPath);
        using var directory = new MMapDirectory(indexPath);

        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            CompressionPolicy = policy,
            MaxBufferedDocs = 2,
            RamBufferSizeMB = 1,
        }))
        {
            foreach (var document in BuildDocuments())
                writer.AddDocument(document);
            writer.Commit();
        }

        var analyticsPath = Path.Combine(indexPath, "search-analytics.json");
        var slowLogPath = Path.Combine(indexPath, "slow-queries.jsonl");
        var analytics = new SearchAnalytics(capacity: 32);

        using (var slowLog = SlowQueryLog.ToFile(thresholdMs: 0, slowLogPath))
        {
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
            {
                SearchAnalytics = analytics,
                SlowQueryLog = slowLog,
            });

            AssertHits(searcher.Search(new TermQuery("status", "active"), 10), 3, "term query");
            AssertHits(searcher.Search(new PhraseQuery("title", "native", "aot"), 10), 1, "phrase query");
            AssertHits(searcher.Search(new PrefixQuery("title", "nat"), 10), 1, "prefix query");
            AssertHits(searcher.Search(new RegexpQuery("code", "^nat"), 10), 1, "regexp query");
            AssertHits(searcher.Search(new RangeQuery("year", 2024, 2026), 10), 2, "numeric range query");
            AssertHits(searcher.Search(new GeoDistanceQuery("location", 51.5074, -0.1278, 250_000), 10), 2, "geo distance query");
            AssertHits(searcher.Search(new GeoBoundingBoxQuery("location", 51.0, 52.0, -3.0, 0.5), 10), 2, "geo bounding-box query");
            var vectorResults = searcher.Search(new VectorQuery("embedding", [1f, 0f, 0f, 0f], topK: 2), 2);
            Assert(vectorResults.ScoreDocs.Length == 2, $"vector query returned {vectorResults.ScoreDocs.Length} scored document(s), expected 2.");

            var collapsed = searcher.SearchWithCollapse(new TermQuery("status", "active"), 10, new CollapseField("category"));
            Assert(collapsed.TotalHits == 2, $"collapsed search returned {collapsed.TotalHits} group(s), expected 2.");

            var storedResult = searcher.Search(new TermQuery("id", "doc-1"), 1);
            AssertHits(storedResult, 1, "stored-field lookup");
            var stored = searcher.GetStoredFields(storedResult.ScoreDocs[0].DocId);
            Assert(stored.TryGetValue("title", out var title) && title.Contains("native aot search"),
                "stored field 'title' was not readable after reopening the index.");
            Assert(stored.TryGetValue("location", out var location) && location.Contains("51.5074,-0.1278"),
                "stored geo field was not readable after reopening the index.");
        }

        using (var analyticsWriter = File.CreateText(analyticsPath))
            analytics.ExportJson(analyticsWriter);

        Assert(new FileInfo(analyticsPath).Length > 2, "search analytics JSON was not written.");
        Assert(new FileInfo(slowLogPath).Length > 0, "slow-query log was not written.");
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Native AOT smoke failed for index directory '{indexPath}'.", ex);
    }
}

static LeanDocument[] BuildDocuments()
{
    return
    [
        Document(
            id: "doc-1",
            title: "native aot search",
            body: "lean corpus supports compact native publishing",
            code: "native",
            status: "active",
            category: "docs",
            year: 2025,
            latitude: 51.5074,
            longitude: -0.1278,
            embedding: [1f, 0f, 0f, 0f]),
        Document(
            id: "doc-2",
            title: "fast corpus indexing",
            body: "stored fields and diagnostics stay available",
            code: "index",
            status: "active",
            category: "docs",
            year: 2024,
            latitude: 51.4545,
            longitude: -2.5879,
            embedding: [0.9f, 0.1f, 0f, 0f]),
        Document(
            id: "doc-3",
            title: "api filtering",
            body: "geo vector and collapse queries are covered",
            code: "api",
            status: "active",
            category: "api",
            year: 2023,
            latitude: 40.7128,
            longitude: -74.0060,
            embedding: [0f, 1f, 0f, 0f]),
        Document(
            id: "doc-4",
            title: "archived sample",
            body: "inactive documents remain searchable by explicit status",
            code: "archive",
            status: "archived",
            category: "archive",
            year: 2020,
            latitude: 48.8566,
            longitude: 2.3522,
            embedding: [0f, 0f, 1f, 0f]),
    ];
}

static LeanDocument Document(
    string id,
    string title,
    string body,
    string code,
    string status,
    string category,
    int year,
    double latitude,
    double longitude,
    float[] embedding)
{
    var document = new LeanDocument();
    document.Add(new StringField("id", id));
    document.Add(new TextField("title", title));
    document.Add(new TextField("body", body));
    document.Add(new StringField("code", code));
    document.Add(new StringField("status", status));
    document.Add(new StringField("category", category));
    document.Add(new NumericField("year", year));
    document.Add(new GeoPointField("location", latitude, longitude));
    document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(embedding)));
    return document;
}

static void AssertHits(TopDocs results, int expectedHits, string scenario)
    => Assert(results.TotalHits == expectedHits, $"{scenario} returned {results.TotalHits} hit(s), expected {expectedHits}.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
