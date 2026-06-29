using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Suggestions;
using Rowles.LeanCorpus.Store;

var builder = Host.CreateApplicationBuilder(args);

// ── OpenTelemetry ────────────────────────────────────────────────────────────
// When run under Aspire, OTEL_EXPORTER_OTLP_ENDPOINT is injected automatically.
// We also export a periodic console reader so you can confirm metrics are firing
// even before they reach the dashboard.

// Required for plain-text gRPC over HTTP/2 (Aspire standalone dashboard default).
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// Surface OTel SDK diagnostic messages so silent export failures become visible.
Environment.SetEnvironmentVariable("OTEL_DIAGNOSTICS_LEVEL", "Warning");

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("LeanCorpus.Telemetry"))
    .WithTracing(tracing => tracing
        .AddSource("Rowles.LeanCorpus")
        .AddOtlpExporter()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("Rowles.LeanCorpus")
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, readerOptions) =>
        {
            // Default is 60s — shorten so the Aspire dashboard updates promptly
            readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5_000;
        })
        .AddConsoleExporter((_, readerOptions) =>
            readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000));

// Structured logs — pipe ILogger output through the OTel OTLP exporter so the
// Aspire dashboard's "Structured" log view picks them up.
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes           = true;
    logging.ParseStateValues        = true;
    logging.AddOtlpExporter();
});

// ── Demo worker ──────────────────────────────────────────────────────────────

builder.Services.AddHostedService<DemoWorker>();

var host = builder.Build();
await host.RunAsync();

// ── DemoWorker ───────────────────────────────────────────────────────────────

/// <summary>
/// Demonstrates LeanCorpus telemetry: distributed traces, metrics, slow-query logging,
/// search analytics, near-real-time refresh via <see cref="SearcherManager"/>,
/// index-size reporting, and spell suggestions via <see cref="DidYouMeanSuggester"/>.
/// </summary>
internal sealed class DemoWorker(IMeterFactory meterFactory, ILogger<DemoWorker> logger) : BackgroundService
{
    private static readonly (string Title, string Author, int Year, string Genre)[] Books =
    [
        ("The Rust Programming Language","Steve Klabnik",         2019, "programming"),
        ("Clean Code",                                        "Robert C. Martin",      2008, "programming"),
        ("Domain-Driven Design",                              "Eric Evans",            2003, "architecture"),
        ("Designing Data-Intensive Applications",             "Martin Kleppmann",      2017, "distributed"),
        ("The Pragmatic Programmer",                          "David Thomas",          1999, "programming"),
        ("Structure and Interpretation of Computer Programs", "Harold Abelson",        1996, "computer science"),
        ("Introduction to Algorithms",                        "Thomas H. Cormen",      2009, "algorithms"),
        ("Code Complete",                                     "Steve McConnell",       2004, "programming"),
        ("Refactoring",                                       "Martin Fowler",         1999, "programming"),
        ("Working Effectively with Legacy Code",              "Michael Feathers",      2004, "programming"),
        ("The Art of Computer Programming",                   "Donald Knuth",          1968, "algorithms"),
        ("Clean Architecture",                                "Robert C. Martin",      2017, "architecture"),
        ("Patterns of Enterprise Application Architecture",   "Martin Fowler",         2002, "architecture"),
        ("The Mythical Man-Month",                            "Frederick P. Brooks",   1975, "management"),
        ("A Philosophy of Software Design",                   "John Ousterhout",       2018, "design"),
        ("Database Internals",                                "Alex Petrov",           2019, "distributed"),
        ("Compilers: Principles, Techniques, and Tools",      "Alfred V. Aho",         1986, "compilers"),
        ("Operating System Concepts",                         "Abraham Silberschatz",  2018, "systems"),
        ("Computer Networks",                                 "Andrew S. Tanenbaum",   2011, "networks"),
        ("The Go Programming Language",                       "Alan A. A. Donovan",    2015, "programming"),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var indexPath   = Path.Combine(Path.GetTempPath(), "leancorpus-telemetry-demo");
        var slowLogPath = Path.Combine(Path.GetTempPath(), "leancorpus-slow.jsonl");
        Directory.CreateDirectory(indexPath);

        // Slow-query log: records any query that takes longer than 5 ms
        using var slowQueryLog = SlowQueryLog.ToFile(thresholdMs: 5, slowLogPath);
        var analytics          = new SearchAnalytics(capacity: 500);

        // Pass the DI-managed IMeterFactory so the meter participates in the OTel pipeline
        using var metrics = new MeterMetricsCollector(meterFactory);

        var dir          = new MMapDirectory(indexPath);
        var writerConfig = new IndexWriterConfig { Metrics = metrics };
        using var writer = new IndexWriter(dir, writerConfig);

        Console.WriteLine("Seeding index...");
        SeedIndex(writer);
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
        logger.LogInformation("Index seeded with {DocumentCount} documents at {IndexPath}", Books.Length, indexPath);
        logger.LogInformation("Sending telemetry to {OtlpEndpoint}", otlpEndpoint);
        logger.LogInformation("Slow query log path: {SlowLogPath}", slowLogPath);

        var searcherConfig = new IndexSearcherConfig
        {
            Metrics          = metrics,
            EnableQueryCache = true,
            SlowQueryLog     = slowQueryLog,
            SearchAnalytics  = analytics,
        };

        // SearcherManager polls for new commits every second and swaps in a fresh searcher
        using var manager = new SearcherManager(dir, new SearcherManagerConfig
        {
            RefreshInterval = TimeSpan.FromSeconds(1),
            SearcherConfig  = searcherConfig,
        });

        var queries   = BuildQueries();
        int iteration = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var query = queries[iteration % queries.Length];
            int hits  = manager.UsingSearcher(s => s.Search(query, topN: 5).TotalHits);

            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["iteration"] = iteration,
                ["queryType"] = query.GetType().Name,
            }))
            {
                logger.LogInformation(
                    "Search {Iteration:D4} {QueryDescription} returned {HitCount} hit(s)",
                    iteration, DescribeQuery(query), hits);
            }

            // Repeat every third query to generate cache hits
            if (iteration % 3 == 0)
                manager.UsingSearcher(s => s.Search(query, topN: 5).TotalHits);

            // ── Spell suggestion ─────────────────────────────────────────────────
            if (iteration % 15 == 0 && iteration > 0)
                PrintSuggestions(manager);

            // ── Commit a new document (triggers SearcherManager refresh) ─────────
            if (iteration % 10 == 0 && iteration > 0)
                AddDynamicDocument(writer, iteration);

            // ── Search analytics summary ─────────────────────────────────────────
            if (iteration % 25 == 0 && iteration > 0)
                PrintAnalytics(analytics);

            // ── Metrics snapshot ─────────────────────────────────────────────────
            if (iteration % 20 == 0 && iteration > 0)
                PrintMetricsSnapshot(metrics.GetSnapshot());

            // ── Index size report ────────────────────────────────────────────────
            if (iteration % 30 == 0 && iteration > 0)
                PrintIndexSize(indexPath);

            iteration++;
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        logger.LogInformation("Demo finished after {Iterations} iterations", iteration);
    }

    private void SeedIndex(IndexWriter writer)
    {
        foreach (var (title, author, year, genre) in Books)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title",  title));
            doc.Add(new TextField("author", author));
            doc.Add(new NumericField("year", year));
            doc.Add(new StringField("genre", genre));
            doc.Add(new StringField("id",    Guid.NewGuid().ToString("N")));
            writer.AddDocument(doc);
        }
        writer.Commit();
    }

    private void AddDynamicDocument(IndexWriter writer, int iteration)
    {
        var docId = $"dyn-{iteration}";
        var doc   = new LeanDocument();
        doc.Add(new TextField("title",  $"Dynamic Document {iteration}"));
        doc.Add(new TextField("author", "Demo Author"));
        doc.Add(new NumericField("year", 2024));
        doc.Add(new StringField("genre", "programming"));
        doc.Add(new StringField("id",    docId));
        writer.AddDocument(doc);
        writer.Commit();
        logger.LogInformation("Committed dynamic document {DocumentId}; SearcherManager will refresh shortly", docId);
    }

    private void PrintSuggestions(SearcherManager manager)
    {
        // "programing" is an intentional typo — the suggester should correct it to "programming"
        const string typo = "programing";
        var suggestions   = manager.UsingSearcher(s =>
            DidYouMeanSuggester.Suggest(s, "title", typo, maxEdits: 2, topN: 3));

        if (suggestions.Count > 0)
            logger.LogInformation("Did-you-mean suggestions for {Term}: {Suggestions}",
                typo, string.Join(", ", suggestions.Select(s => s.Term)));
    }

    private void PrintAnalytics(SearchAnalytics analytics)
    {
        var events = analytics.GetRecentEvents(8);
        if (events.Count == 0) return;

        using var scope = logger.BeginScope("RecentSearchEvents");
        foreach (var e in events)
        {
            logger.LogInformation(
                "Recent {QueryType} took {ElapsedMs:F2}ms returning {TotalHits} hits (cached={CacheHit})",
                e.QueryType, e.ElapsedMs, e.TotalHits, e.CacheHit);
        }
    }

    private void PrintMetricsSnapshot(MetricsSnapshot snap)
    {
        logger.LogInformation(
            "Metrics: searches={SearchCount} avg={SearchAvgMs:F2}ms max={SearchMaxMs}ms cacheHitRate={CacheHitRate:P0} commits={CommitCount} merges={MergeCount}",
            snap.SearchCount, snap.SearchAvgMs, snap.SearchMaxMs,
            snap.CacheHitRate, snap.CommitCount, snap.MergeCount);

        if (snap.LatencyHistogram is { } hist)
        {
            string[] labels  = ["<1ms", "<5ms", "<10ms", "<50ms", "<100ms", "<500ms", "<1s", ">=1s"];
            var      buckets = string.Join(" ", Enumerable.Range(0, hist.Length).Select(i => $"{labels[i]}={hist[i]}"));
            logger.LogInformation("Latency histogram: {Buckets}", buckets);
        }
    }

    private void PrintIndexSize(string indexPath)
    {
        var report = IndexSizeCalculator.Calculate(indexPath);
        logger.LogInformation("Index size {Size} across {SegmentCount} segment(s)",
            FormatBytes(report.TotalSizeBytes), report.Segments.Count);
    }

    /// <summary>
    /// Builds a representative set of queries covering term, phrase, fuzzy, prefix,
    /// range, and boolean query types.
    /// </summary>
    private static Query[] BuildQueries()
    {
        var boolProgrammingDesign = new BooleanQuery.Builder()
            .Add(new TermQuery("genre", "programming"), Occur.Must)
            .Add(new TermQuery("title", "design"),      Occur.Should)
            .Build();

        var boolAuthorOrTitle = new BooleanQuery.Builder()
            .Add(new TermQuery("author", "martin"),      Occur.Should)
            .Add(new TermQuery("title",  "refactoring"), Occur.Should)
            .Build();

        var boolArchitectureNotManagement = new BooleanQuery.Builder()
            .Add(new TermQuery("genre", "architecture"), Occur.Must)
            .Add(new TermQuery("genre", "management"),   Occur.MustNot)
            .Build();

        return
        [
            // Term queries
            new TermQuery("title",  "programming"),
            new TermQuery("title",  "design"),
            new TermQuery("author", "martin"),
            new TermQuery("genre",  "architecture"),
            new TermQuery("genre",  "distributed"),

            // Phrase queries
            new PhraseQuery("title", "clean",    "code"),
            new PhraseQuery("title", "data",     "intensive"),
            new PhraseQuery("title", "computer", "programming"),

            // Fuzzy queries (intentional typos)
            new FuzzyQuery("title",  "algorythms"),
            new FuzzyQuery("author", "fowlre"),

            // Prefix queries
            new PrefixQuery("title",  "comp"),
            new PrefixQuery("author", "martin"),

            // Numeric range queries
            new RangeQuery("year", 2010, 2025),
            new RangeQuery("year", 1990, 2005),
            new RangeQuery("year", 1960, 1990),

            // Boolean queries
            boolProgrammingDesign,
            boolAuthorOrTitle,
            boolArchitectureNotManagement,
        ];
    }

    private static string DescribeQuery(Query query) => query switch
    {
        TermQuery   tq => $"Term({tq.Field}:{tq.Term})",
        PhraseQuery pq => $"Phrase({pq.Field}:[{string.Join(" ", pq.Terms)}])",
        FuzzyQuery  fq => $"Fuzzy({fq.Field}:~{fq.Term})",
        PrefixQuery px => $"Prefix({px.Field}:{px.Prefix}*)",
        RangeQuery  rq => $"Range({rq.Field}:[{rq.Min}–{rq.Max}])",
        BooleanQuery   => "Boolean(…)",
        _              => query.GetType().Name,
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_024         => $"{bytes} B",
        < 1_048_576     => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F2} GB",
    };
}
