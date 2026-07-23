using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Example.LinuxKernelCodeSearch;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options is null) return 1;

        Directory.CreateDirectory(options.Output);
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        MetricsSnapshot metrics = default;
        var searcherOpenMs = 0.0;
        var workingSetBeforeOpen = 0L;
        var searcherOpenWorkingSet = 0L;
        var scenarioMetrics = new List<ScenarioMetrics>();

        if (!options.SkipIndex)
        {
            var fileMap = EnumerateFiles(options.Source);
            Console.WriteLine($"Enumerated {fileMap.Count} source files");
            if (fileMap.Count == 0) return 1;

            metrics = await IndexFilesAsync(fileMap, options.Index, options.MaxDocs);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.GetTotalMemory(true);

            if (options.Compact)
            {
                Console.WriteLine("Compacting...");
                var sw = Stopwatch.StartNew();
                using var w = new IndexWriter(new MMapDirectory(options.Index), new IndexWriterConfig
                {
                    DefaultAnalyser = new WhitespaceAnalyser(),
                });
                var merged = w.Compact();
                sw.Stop();
                Console.WriteLine($"  Merged to {merged} segments in {sw.Elapsed.TotalSeconds:F1}s");
            }
        }

        if (!options.SkipSearch)
        {
            if (options.SkipIndex)
            {
                metrics = metrics with
                {
                    FinalSegmentCount = CountSegmentsOnDisk(options.Index),
                    IndexSizeBytes = GetDirectorySize(options.Index),
                };
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            workingSetBeforeOpen = Environment.WorkingSet;
            using var searcher = OpenSearcher(options.Index, options.MaxCachedSegmentReaders,
                out searcherOpenMs, out searcherOpenWorkingSet);
            Console.WriteLine(
                $"Searcher opened in {searcherOpenMs:F0}ms, working set {searcherOpenWorkingSet / (1024 * 1024)}MB");

            const string pathIdForFilter = "0";

            var scenarios = new (string Name, Query Query)[]
            {
                ("term-symbol",       new TermQuery("content", "task_struct")),
                ("phrase-symbol",     new PhraseQuery("content", "struct", "task_struct")),
                ("wildcard-callsite", new WildcardQuery("content", "*kmalloc*")),
                ("fuzzy-typo",        new FuzzyQuery("content", "schedulr")),
                ("regex-grep",        new RegexpQuery("content", "BUG_ON.*")),
                ("boolean-filter",    new BooleanQuery.Builder()
                    .Add(new TermQuery("path_id", pathIdForFilter), Occur.Must)
                    .Add(new WildcardQuery("content", "*spin_lock*"), Occur.Must)
                    .Build()),
            };

            foreach (var (name, query) in scenarios)
            {
                if (options.Scenario is not null && !name.Equals(options.Scenario, StringComparison.Ordinal))
                    continue;
                var scenario = RunScenario(searcher, name, query,
                    options.WarmupIterations, options.MeasuredIterations, topN: 100);
                scenarioMetrics.Add(scenario);
                Console.WriteLine($"{name}: first={scenario.FirstQueryMs:F2}ms " +
                    $"p50={scenario.P50Ms:F2}ms p99={scenario.P99Ms:F2}ms hits={scenario.TotalHits}");
            }

            if (options.Scenario is null || options.Scenario.Equals("stored-retrieval", StringComparison.Ordinal))
            {
                var storedScenario = RunStoredFieldsScenario(searcher,
                    options.WarmupIterations, options.MeasuredIterations);
                scenarioMetrics.Add(storedScenario);
                Console.WriteLine($"stored-retrieval: first={storedScenario.FirstQueryMs:F2}ms " +
                    $"p50={storedScenario.P50Ms:F2}ms p99={storedScenario.P99Ms:F2}ms " +
                    $"hits={storedScenario.TotalHits}");
            }
        }

        var metricsPath = Path.Combine(options.Output, $"metrics-{ts}.json");
        WriteMetricsJson(metricsPath, metrics, options,
            workingSetBeforeOpen, searcherOpenMs, searcherOpenWorkingSet, scenarioMetrics);
        Console.WriteLine($"Telemetry written to {metricsPath}");

        return 0;
    }

    private static Options? ParseArgs(string[] args)
    {
        var opts = new Options();
        int i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--source":     opts.Source = args[++i]; break;
                case "--index":      opts.Index = args[++i]; break;
                case "--output":     opts.Output = args[++i]; break;
                case "--max-docs":   opts.MaxDocs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--max-cached-segment-readers": opts.MaxCachedSegmentReaders = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--scenario": opts.Scenario = args[++i]; break;
                case "--warmup": opts.WarmupIterations = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--measured": opts.MeasuredIterations = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--no-compact": opts.Compact = false; break;
                case "--skip-index": opts.SkipIndex = true; break;
                case "--skip-search": opts.SkipSearch = true; break;
                case "--help" or "-h":
                    Console.WriteLine("Usage: LinuxKernelCodeSearch [options]");
                    Console.WriteLine("  --source <path>      Linux kernel clone (default: ./linux)");
                    Console.WriteLine("  --index <path>       Index directory (default: ./kernel-index)");
                    Console.WriteLine("  --output <path>      Telemetry output directory (default: ./output)");
                    Console.WriteLine("  --max-docs <n>       Max documents to index (default: 0 = all)");
                    Console.WriteLine("  --max-cached-segment-readers <n>  Heavy reader cache capacity (default: 256)");
                    Console.WriteLine("  --scenario <name>    Run one query scenario (default: all)");
                    Console.WriteLine("  --warmup <n>         Warmup iterations per scenario (default: 10)");
                    Console.WriteLine("  --measured <n>       Measured iterations per scenario (default: 50)");
                    Console.WriteLine("  --no-compact         Skip Compact() after indexing");
                    Console.WriteLine("  --skip-index         Skip indexing, only search");
                    Console.WriteLine("  --skip-search        Skip search, only index");
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown flag: {args[i]}");
                    return null;
            }
            i++;
        }
        if (opts.MaxCachedSegmentReaders < 1 || opts.WarmupIterations < 0 || opts.MeasuredIterations < 1)
        {
            Console.Error.WriteLine("Cache size and measured iterations must be positive; warmup must not be negative.");
            return null;
        }
        return opts;
    }

    private static IReadOnlyDictionary<int, string> EnumerateFiles(string sourcePath)
    {
        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Source directory not found: {sourcePath}");
            return new Dictionary<int, string>();
        }

        var files = new List<string>();
        foreach (var ext in new[] { "*.c", "*.h" })
        {
            foreach (var f in Directory.EnumerateFiles(sourcePath, ext, SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourcePath, f);
                if (!rel.StartsWith("Documentation", StringComparison.Ordinal))
                    files.Add(f);
            }
        }
        files.Sort(StringComparer.Ordinal);

        var map = new Dictionary<int, string>();
        for (int j = 0; j < files.Count; j++)
            map[j] = files[j];
        return map;
    }

    private static async Task<MetricsSnapshot> IndexFilesAsync(
        IReadOnlyDictionary<int, string> fileMap, string indexPath, int maxDocs)
    {
        var indexDir = new MMapDirectory(indexPath);
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MergePolicy = NoMergePolicy.Instance,
            MaxBufferedDocs = 10_000,
            MaxQueuedDocs = 100_000,
            DurableCommits = false,
        };

        using var writer = new IndexWriter(indexDir, config);
        var indexSw = Stopwatch.StartNew();
        var pollSw = Stopwatch.StartNew();
        long docsIndexed = 0;

        foreach (var (pathId, filePath) in fileMap.OrderBy(kv => kv.Key))
        {
            if (maxDocs > 0 && docsIndexed >= maxDocs) break;

            foreach (var line in File.ReadLines(filePath))
            {
                if (maxDocs > 0 && docsIndexed >= maxDocs) break;

                var doc = new LeanDocument();
                doc.Add(new StringField("path_id",
                    pathId.ToString(CultureInfo.InvariantCulture),
                    stored: true, boost: 1.0f, storeDocValues: false,
                    indexOptions: FieldIndexOptions.DocsOnly));
                doc.Add(new StoredField("line", 0));
                doc.Add(new TextField("content", line, stored: true));

                writer.AddDocument(doc);
                docsIndexed++;

                if (docsIndexed % 10_000 == 0 && pollSw.Elapsed.TotalSeconds >= 1.0)
                {
                    var segCount = CountSegmentsOnDisk(indexPath);
                    Console.WriteLine($"  {docsIndexed} docs, {segCount} segments, " +
                        $"{docsIndexed / indexSw.Elapsed.TotalSeconds:F0} docs/s");
                    pollSw.Restart();
                }
            }
        }

        Console.WriteLine("Committing...");
        var commitSw = Stopwatch.StartNew();
        writer.Commit();
        commitSw.Stop();
        indexSw.Stop();

        var finalSegCount = CountSegmentsOnDisk(indexPath);
        Console.WriteLine($"Indexed {docsIndexed} docs in {indexSw.Elapsed.TotalSeconds:F1}s " +
            $"({docsIndexed / indexSw.Elapsed.TotalSeconds:F0} docs/s)");
        Console.WriteLine($"  Commit: {commitSw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Final segments: {finalSegCount}");
        Console.WriteLine($"  Index size: {FormatBytes(GetDirectorySize(indexPath))}");

        return new MetricsSnapshot
        {
            IndexTimeMs = indexSw.Elapsed.TotalMilliseconds,
            DocsIndexed = docsIndexed,
            FinalSegmentCount = finalSegCount,
            CommitTimeMs = commitSw.Elapsed.TotalMilliseconds,
            IndexSizeBytes = GetDirectorySize(indexPath),
        };
    }

    private static IndexSearcher OpenSearcher(string indexPath, int maxCachedSegmentReaders,
        out double elapsedMs, out long workingSet)
    {
        var dir = new MMapDirectory(indexPath);
        var sw = Stopwatch.StartNew();
        var searcher = new IndexSearcher(dir, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            MaxCachedSegmentReaders = maxCachedSegmentReaders,
        });
        sw.Stop();
        elapsedMs = sw.Elapsed.TotalMilliseconds;
        workingSet = Environment.WorkingSet;
        return searcher;
    }

    private static ScenarioMetrics RunScenario(
        IndexSearcher searcher, string name, Query query, int warmup, int measured, int topN)
    {
        var firstSw = Stopwatch.StartNew();
        var firstResult = searcher.Search(query, topN);
        firstSw.Stop();
        long workingSetAfterCold = Environment.WorkingSet;

        for (int i = 0; i < warmup; i++)
            searcher.Search(query, topN);

        var latencies = new double[measured];
        int totalHits = 0;
        for (int i = 0; i < measured; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = searcher.Search(query, topN);
            sw.Stop();
            latencies[i] = sw.Elapsed.TotalMilliseconds;
            totalHits = result.TotalHits;
        }

        Array.Sort(latencies);
        double p50 = latencies[measured / 2];
        double p99 = latencies[(int)(measured * 0.99)];
        return new ScenarioMetrics(name, firstSw.Elapsed.TotalMilliseconds, p50, p99,
            totalHits == 0 ? firstResult.TotalHits : totalHits,
            workingSetAfterCold, Environment.WorkingSet);
    }

    private static ScenarioMetrics RunStoredFieldsScenario(IndexSearcher searcher, int warmup, int measured)
    {
        static int ReadStoredFields(IndexSearcher current)
        {
            var results = current.Search(new MatchAllDocsQuery(), 100);
            foreach (var scoreDoc in results.ScoreDocs)
                current.GetStoredFields(scoreDoc.DocId);
            return results.ScoreDocs.Length;
        }

        var firstSw = Stopwatch.StartNew();
        int totalHits = ReadStoredFields(searcher);
        firstSw.Stop();
        long workingSetAfterCold = Environment.WorkingSet;
        for (int i = 0; i < warmup; i++)
            ReadStoredFields(searcher);

        var latencies = new double[measured];
        for (int i = 0; i < measured; i++)
        {
            var sw = Stopwatch.StartNew();
            ReadStoredFields(searcher);
            sw.Stop();
            latencies[i] = sw.Elapsed.TotalMilliseconds;
        }
        Array.Sort(latencies);
        return new ScenarioMetrics("stored-retrieval", firstSw.Elapsed.TotalMilliseconds,
            latencies[measured / 2], latencies[(int)(measured * 0.99)], totalHits,
            workingSetAfterCold, Environment.WorkingSet);
    }

    private static int CountSegmentsOnDisk(string indexPath)
    {
        try { return Directory.EnumerateFiles(indexPath, "*.seg").Count(); }
        catch { return 0; }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* skip locked files */ }
            }
            return total;
        }
        catch { return 0; }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes}B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1}KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1}MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2}GB",
    };

    private static void WriteMetricsJson(string path, MetricsSnapshot metrics,
        Options options, long workingSetBeforeOpen,
        double searcherOpenMs, long searcherOpenWorkingSet,
        IReadOnlyList<ScenarioMetrics> scenarios)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("index_time_ms", metrics.IndexTimeMs);
        writer.WriteNumber("docs_indexed", metrics.DocsIndexed);
        writer.WriteNumber("final_segment_count", metrics.FinalSegmentCount);
        writer.WriteNumber("commit_time_ms", metrics.CommitTimeMs);
        writer.WriteNumber("index_size_bytes", metrics.IndexSizeBytes);
        writer.WriteNumber("max_cached_segment_readers", options.MaxCachedSegmentReaders);
        writer.WriteNumber("warmup_iterations", options.WarmupIterations);
        writer.WriteNumber("measured_iterations", options.MeasuredIterations);
        writer.WriteNumber("working_set_before_open_bytes", workingSetBeforeOpen);
        writer.WriteNumber("searcher_open_ms", searcherOpenMs);
        writer.WriteNumber("searcher_open_working_set_bytes", searcherOpenWorkingSet);
        writer.WriteStartArray("scenarios");
        foreach (var scenario in scenarios)
        {
            writer.WriteStartObject();
            writer.WriteString("name", scenario.Name);
            writer.WriteNumber("first_query_ms", scenario.FirstQueryMs);
            writer.WriteNumber("p50_ms", scenario.P50Ms);
            writer.WriteNumber("p99_ms", scenario.P99Ms);
            writer.WriteNumber("total_hits", scenario.TotalHits);
            writer.WriteNumber("working_set_after_cold_bytes", scenario.WorkingSetAfterColdBytes);
            writer.WriteNumber("working_set_after_warm_bytes", scenario.WorkingSetAfterWarmBytes);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal sealed class Options
    {
        public string Source { get; set; } = "./linux";
        public string Index { get; set; } = "./kernel-index";
        public string Output { get; set; } = "./output";
        public bool Compact { get; set; } = true;
        public bool SkipIndex { get; set; }
        public bool SkipSearch { get; set; }
        public int MaxDocs { get; set; }
        public int MaxCachedSegmentReaders { get; set; } = 256;
        public string? Scenario { get; set; }
        public int WarmupIterations { get; set; } = 10;
        public int MeasuredIterations { get; set; } = 50;
    }

    internal record struct MetricsSnapshot
    {
        public double IndexTimeMs { get; init; }
        public long DocsIndexed { get; init; }
        public int FinalSegmentCount { get; init; }
        public double CommitTimeMs { get; init; }
        public long IndexSizeBytes { get; init; }
    }

    internal readonly record struct ScenarioMetrics(
        string Name,
        double FirstQueryMs,
        double P50Ms,
        double P99Ms,
        int TotalHits,
        long WorkingSetAfterColdBytes,
        long WorkingSetAfterWarmBytes);
}
