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
        var searcherOpenWorkingSet = 0L;

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
            using var searcher = OpenSearcher(options.Index, out searcherOpenMs, out searcherOpenWorkingSet);
            Console.WriteLine(
                $"Searcher opened in {searcherOpenMs:F0}ms, working set {searcherOpenWorkingSet / (1024 * 1024)}MB");

            var pathIdForFilter = "0";
            try
            {
                var sd = searcher.Search(new MatchAllDocsQuery(), 1);
                if (sd.ScoreDocs.Length > 0)
                {
                    var fields = searcher.GetStoredFields(sd.ScoreDocs[0].DocId);
                    if (fields.TryGetValue("path_id", out var vals) && vals.Count > 0)
                        pathIdForFilter = vals[0];
                }
            }
            catch { /* use default */ }

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
                var (p50, p99, totalHits) = RunScenario(searcher, query, warmup: 10, measured: 50, topN: 100);
                Console.WriteLine($"{name}: p50={p50:F2}ms p99={p99:F2}ms hits={totalHits}");
            }

            var broadResults = searcher.Search(new MatchAllDocsQuery(), 100);
            var storedSw = Stopwatch.StartNew();
            int storedCount = 0;
            foreach (var sd in broadResults.ScoreDocs)
            {
                searcher.GetStoredFields(sd.DocId);
                storedCount++;
            }
            storedSw.Stop();
            Console.WriteLine($"stored-retrieval: {storedCount} docs in {storedSw.Elapsed.TotalMilliseconds:F1}ms");
        }

        var metricsPath = Path.Combine(options.Output, $"metrics-{ts}.json");
        WriteMetricsJson(metricsPath, metrics, searcherOpenMs, searcherOpenWorkingSet);
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
                case "--no-compact": opts.Compact = false; break;
                case "--skip-index": opts.SkipIndex = true; break;
                case "--skip-search": opts.SkipSearch = true; break;
                case "--help" or "-h":
                    Console.WriteLine("Usage: LinuxKernelCodeSearch [options]");
                    Console.WriteLine("  --source <path>      Linux kernel clone (default: ./linux)");
                    Console.WriteLine("  --index <path>       Index directory (default: ./kernel-index)");
                    Console.WriteLine("  --output <path>      Telemetry output directory (default: ./output)");
                    Console.WriteLine("  --max-docs <n>       Max documents to index (default: 0 = all)");
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

    private static IndexSearcher OpenSearcher(string indexPath, out double elapsedMs, out long workingSet)
    {
        var dir = new MMapDirectory(indexPath);
        var sw = Stopwatch.StartNew();
        var searcher = new IndexSearcher(dir, new IndexSearcherConfig
        {
            EnableQueryCache = false,
        });
        sw.Stop();
        elapsedMs = sw.Elapsed.TotalMilliseconds;
        workingSet = Environment.WorkingSet;
        return searcher;
    }

    private static (double P50, double P99, int TotalHits) RunScenario(
        IndexSearcher searcher, Query query, int warmup, int measured, int topN)
    {
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
        return (p50, p99, totalHits);
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
        double searcherOpenMs, long searcherOpenWorkingSet)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("index_time_ms", metrics.IndexTimeMs);
        writer.WriteNumber("docs_indexed", metrics.DocsIndexed);
        writer.WriteNumber("final_segment_count", metrics.FinalSegmentCount);
        writer.WriteNumber("commit_time_ms", metrics.CommitTimeMs);
        writer.WriteNumber("index_size_bytes", metrics.IndexSizeBytes);
        writer.WriteNumber("searcher_open_ms", searcherOpenMs);
        writer.WriteNumber("searcher_open_working_set_bytes", searcherOpenWorkingSet);
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
    }

    internal record struct MetricsSnapshot
    {
        public double IndexTimeMs { get; init; }
        public long DocsIndexed { get; init; }
        public int FinalSegmentCount { get; init; }
        public double CommitTimeMs { get; init; }
        public long IndexSizeBytes { get; init; }
    }
}
