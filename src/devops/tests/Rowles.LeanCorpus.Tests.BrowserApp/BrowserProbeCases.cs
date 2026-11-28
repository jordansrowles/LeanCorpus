using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.BrowserApp;

/// <summary>
/// Probes of browser runtime assumptions: prebuilt payload loading, memory amplification,
/// main-thread blocking, writer exclusion and unsupported subsystems. Each case reports
/// measurements as JSON and fails only when a stated contract is broken.
/// </summary>
public static partial class BrowserCases
{
    /// <summary>Returns the current WebAssembly linear-memory size in bytes (a high-water mark).</summary>
    public static Func<double>? WasmBytes { get; set; }

    internal static readonly string[] ProbeNames =
        ["payload-small-cfs", "payload-large-cfs", "payload-large-loose", "payload-large-cfs-streamed", "payload-large-cfs-buffered", "writer-exclusion", "backup", "analysis", "brotli-read", "blocking", "memory-limit"];

    private static async Task<string> RunProbeAsync(string name, Uri baseAddress)
    {
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(5) };
        var report = new Report();
        return name switch
        {
            "payload-small-cfs" => await PayloadAsync(client, "small-cfs", report),
            "payload-large-cfs" => await PayloadAsync(client, "large-cfs", report),
            "payload-large-loose" => await PayloadAsync(client, "large-loose", report),
            "payload-large-cfs-streamed" => await PayloadAsync(client, "large-cfs", report, streaming: true),
            "payload-large-cfs-buffered" => await PayloadAsync(client, "large-cfs", report, streaming: false),
            "writer-exclusion" => WriterExclusion(report),
            "backup" => Backup(report),
            "analysis" => await AnalysisAsync(client, report),
            "brotli-read" => await BrotliReadAsync(client, report),
            "blocking" => await BlockingAsync(report),
            "memory-limit" => await MemoryLimitAsync(client, report),
            _ => throw new ArgumentException("Unknown probe: " + name)
        };
    }

    private static async Task<(string Path, PayloadQueryResult[] Expected, long Bytes)> LoadPayloadAsync(
        HttpClient client, string variant, Report report, bool? streaming = null)
    {
        report.Heap("beforeDownload");
        var stopwatch = Stopwatch.StartNew();
        var (files, expected) = BrowserPayload.ReadManifest(await client.GetByteArrayAsync($"payload/{variant}/manifest.json"));
        string path = Path.Combine("/payload", variant + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        long bytes = 0;
        foreach (var file in files)
        {
            string target = Path.Combine(path, file.Name);
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"payload/{variant}/files/{Uri.EscapeDataString(file.Name)}"))
            {
                if (streaming is { } enabled)
                    Microsoft.AspNetCore.Components.WebAssembly.Http.WebAssemblyHttpRequestMessageExtensions.SetBrowserResponseStreamingEnabled(request, enabled);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                using var body = await response.Content.ReadAsStreamAsync();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024);
                await body.CopyToAsync(output);
            }
            long length = new FileInfo(target).Length;
            Check(length == file.Length, $"{file.Name}: length {length} != {file.Length}");
            string hash;
            using (var input = File.OpenRead(target)) hash = Convert.ToHexString(SHA256.HashData(input));
            Check(hash == file.Sha256, $"{file.Name}: SHA-256 mismatch");
            bytes += length;
        }
        report.Add("streaming", streaming is null ? "default" : streaming.Value ? "enabled" : "disabled");
        report.Add("files", files.Length);
        report.Add("payloadBytes", bytes);
        report.Add("loadMs", stopwatch.Elapsed.TotalMilliseconds);
        report.Heap("afterLoad");
        return (path, expected, bytes);
    }

    private static async Task<string> PayloadAsync(HttpClient client, string variant, Report report, bool? streaming = null)
    {
        var (path, expected, bytes) = await LoadPayloadAsync(client, variant, report, streaming);
        using var measurement = FileSystemDiagnostics.BeginDetailedMeasurement();
        var directory = new MMapDirectory(path);
        var stopwatch = Stopwatch.StartNew();
        var searcher = new IndexSearcher(directory);
        report.Add("openMs", stopwatch.Elapsed.TotalMilliseconds);
        report.Heap("afterOpen");

        long mappingsBefore = FileSystemDiagnostics.GetSnapshot().MemoryMappedViewCreationCount;
        stopwatch.Restart();
        var actual = BrowserPayload.Evaluate(searcher);
        report.Add("firstEvaluateMs", stopwatch.Elapsed.TotalMilliseconds);
        report.Add("firstEvaluateMappings", FileSystemDiagnostics.GetSnapshot().MemoryMappedViewCreationCount - mappingsBefore);
        report.Heap("afterFirstEvaluate");
        var differences = BrowserPayload.Compare(expected, actual);

        const int repeats = 5;
        mappingsBefore = FileSystemDiagnostics.GetSnapshot().MemoryMappedViewCreationCount;
        stopwatch.Restart();
        for (int i = 0; i < repeats; i++) BrowserPayload.Evaluate(searcher);
        report.Add("repeatEvaluateMsAvg", stopwatch.Elapsed.TotalMilliseconds / repeats);
        report.Add("repeatMappingsPerEvaluate", (FileSystemDiagnostics.GetSnapshot().MemoryMappedViewCreationCount - mappingsBefore) / (double)repeats);

        var second = new IndexSearcher(directory);
        BrowserPayload.Evaluate(second);
        report.Heap("afterSecondSearcher");
        second.Dispose();
        searcher.Dispose();
        directory.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        report.Heap("afterDispose");
        report.Add("managedHeapBytes", GC.GetTotalMemory(forceFullCollection: true));
        report.Add("heapGrowthOverPayload", (report.HeapValue("afterSecondSearcher") - report.HeapValue("beforeDownload")) / bytes);
        Directory.Delete(path, recursive: true);

        Check(differences.Count == 0, "Browser results differ from the desktop builder: " + string.Join("; ", differences) + " " + report);
        return report.ToString();
    }

    private static string WriterExclusion(Report report)
    {
        string path = Path.Combine("/leancorpus-tests", "lock-" + Guid.NewGuid().ToString("N"));
        using var directory = new MMapDirectory(path);
        using var first = new IndexWriter(directory, Settings());
        first.AddDocument(Document("first"));
        report.Add("sameDirectoryInstance", TrySecondWriter(directory));
        using (var separate = new MMapDirectory(path))
            report.Add("separateDirectoryInstance", TrySecondWriter(separate));
        first.Commit();
        bool excluded = report.Text("sameDirectoryInstance") == "rejected" && report.Text("separateDirectoryInstance") == "rejected";
        Check(excluded, "A second IndexWriter opened while the first held write.lock: " + report);
        return report.ToString();
    }

    private static string TrySecondWriter(MMapDirectory directory)
    {
        try
        {
            using var second = new IndexWriter(directory, Settings());
            return "opened";
        }
        catch (WriteLockException)
        {
            return "rejected";
        }
    }

    private static string Backup(Report report)
    {
        string path = Path.Combine("/leancorpus-tests", "backup-source-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine("/leancorpus-tests", "backup-target-" + Guid.NewGuid().ToString("N"));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, Settings());
        writer.AddDocument(Document("before-backup"));
        writer.Commit();
        var snapshot = writer.CreateSnapshot();
        try
        {
            writer.BackupSnapshot(snapshot, target);
            report.Add("backup", "succeeded");
        }
        catch (Exception error)
        {
            report.Add("backup", error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            writer.ReleaseSnapshot(snapshot);
        }
        var leftovers = Directory.GetFileSystemEntries("/leancorpus-tests")
            .Where(entry => Path.GetFileName(entry).StartsWith(Path.GetFileName(target), StringComparison.Ordinal))
            .Select(entry => Path.GetFileName(entry) + (Directory.Exists(entry) ? "/(" + Directory.GetFileSystemEntries(entry, "*", SearchOption.AllDirectories).Length + " entries)" : ""))
            .ToArray();
        report.Add("leftovers", string.Join(",", leftovers));
        writer.AddDocument(Document("after-backup"));
        writer.Commit();
        report.Add("writerUsableAfterBackup", "yes");
        bool rejectedCleanly = report.Text("backup").StartsWith(nameof(PlatformNotSupportedException), StringComparison.Ordinal) && leftovers.Length == 0;
        Check(rejectedCleanly || report.Text("backup") == "succeeded", "Backup neither succeeded nor failed cleanly: " + report);
        return report.ToString();
    }

    private static async Task<string> AnalysisAsync(HttpClient client, Report report)
    {
        using var desktop = JsonDocument.Parse(await client.GetByteArrayAsync("expect/analysis"));
        var root = desktop.RootElement;
        var differences = new List<string>();
        foreach (string language in AnalyserFactory.SupportedLanguages)
        {
            string want = root.GetProperty("tokens:" + language).GetString()!;
            string got = BrowserPayload.Tokens(language, BrowserPayload.MixedSample);
            if (language != "ja" && want != got) differences.Add($"{language}: browser [{got}] desktop [{want}]");
        }
        report.Add("languagesCompared", AnalyserFactory.SupportedLanguages.Count);
        report.Add("unexpectedDifferences", string.Join("; ", differences));

        // Known limitation 1: the Chinese lexicon is located on disk beside the application. The browser
        // filesystem has none, so analysis silently uses the small built-in list under the same identity.
        report.Add("desktopZhLexiconWords", root.GetProperty("zhLexiconWords").GetInt32());
        report.Add("browserZhLexiconWords", ChineseLexicon.Default.Count);
        report.Add("desktopZhTokens", root.GetProperty("zhTokens").GetString()!);
        report.Add("browserZhTokens", BrowserPayload.Tokens("zh", BrowserPayload.ChineseSample));
        report.Add("zhAnalysisIdSame", root.GetProperty("zhAnalysisId").GetString() == AnalyserFactory.GetDefaultAnalysisId("zh") ? "yes" : "no");
        // Known limitation 2: the default Japanese analyser requires lexicons/japanese.jlc on disk.
        report.Add("desktopJaTokens", root.GetProperty("jaTokens").GetString()!);
        report.Add("browserJaTokens", BrowserPayload.Tokens("ja", BrowserPayload.JapaneseSample));

        Check(differences.Count == 0, "Browser analysis differs from desktop: " + report);
        bool zhLimitation = report.Text("browserZhTokens") != report.Text("desktopZhTokens")
            && report.Number("browserZhLexiconWords") < report.Number("desktopZhLexiconWords")
            && report.Text("zhAnalysisIdSame") == "yes";
        bool jaLimitation = report.Text("browserJaTokens").StartsWith("!FileNotFoundException", StringComparison.Ordinal);
        Check(zhLimitation && jaLimitation,
            "The documented CJK browser limitations changed; update the browser guide and this probe: " + report);
        return report.ToString();
    }

    private static async Task<string> BrotliReadAsync(HttpClient client, Report report)
    {
        var (path, expected, _) = await LoadPayloadAsync(client, "brotli", report);
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        var hits = searcher.Search(new TermQuery("body", "w3"), 5);
        report.Add("termHits", hits.TotalHits);
        report.Add("expectedTermHits", expected.First(result => result.Key == "term:w3").TotalHits);
        try
        {
            report.Add("storedField", searcher.GetStoredFields(hits.ScoreDocs[0].DocId)["id"][0]);
        }
        catch (Exception error)
        {
            report.Add("storedField", error.GetType().Name + ": " + error.Message);
        }
        report.Add("searchAfterStoredFailure", searcher.Search(new TermQuery("body", "w17"), 5).TotalHits);
        bool clean = !report.Text("storedField").Contains("Exception", StringComparison.Ordinal)
            || report.Text("storedField").StartsWith(nameof(PlatformNotSupportedException), StringComparison.Ordinal);
        Check(clean && report.Number("termHits") == report.Number("expectedTermHits"), "Brotli payload read failed without a clear capability error: " + report);
        return report.ToString();
    }

    private static async Task<string> BlockingAsync(Report report)
    {
        string path = Path.Combine("/leancorpus-tests", "blocking-" + Guid.NewGuid().ToString("N"));
        using var directory = new MMapDirectory(path);
        report.Heap("start");
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        const int documents = 5_000;
        double longest = 0;
        var total = Stopwatch.StartNew();
        var call = new Stopwatch();
        for (int i = 0; i < documents; i++)
        {
            var document = BrowserPayload.MakeDocument(i);
            call.Restart();
            await writer.AddDocumentAsync(document);
            longest = Math.Max(longest, call.Elapsed.TotalMilliseconds);
        }
        report.Add("documents", documents);
        report.Add("addTotalMs", total.Elapsed.TotalMilliseconds);
        report.Add("addLongestCallMs", longest);
        call.Restart();
        await writer.CommitAsync();
        report.Add("commitMs", call.Elapsed.TotalMilliseconds);
        report.Add("segmentsAfterCommit", writer.GetNrtSegments().Count);
        call.Restart();
        writer.ForceMerge(1);
        writer.Commit();
        report.Add("forceMergeMs", call.Elapsed.TotalMilliseconds);
        report.Heap("end");
        using var searcher = new IndexSearcher(directory);
        call.Restart();
        int hits = searcher.Search(new TermQuery("body", "w3"), 10).TotalHits;
        report.Add("firstQueryMs", call.Elapsed.TotalMilliseconds);
        Check(hits > 0, "Browser-built index returned no hits.");
        return report.ToString();
    }

    private static async Task<string> MemoryLimitAsync(HttpClient client, Report report)
    {
        var (path, expected, _) = await LoadPayloadAsync(client, "large-cfs", report);
        var directory = new MMapDirectory(path);
        var searchers = new List<IndexSearcher>();
        var trajectory = new StringBuilder();
        string outcome = "limit-not-reached";
        try
        {
            for (int i = 0; i < 160; i++)
            {
                var searcher = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaderBytes = long.MaxValue / 4 });
                searchers.Add(searcher);
                BrowserPayload.Evaluate(searcher);
                if (i % 5 == 0) trajectory.Append(i).Append(':').Append((Heap() / (1024 * 1024)).ToString("F0", CultureInfo.InvariantCulture)).Append("MiB ");
            }
        }
        catch (Exception error)
        {
            outcome = error.GetType().Name + ": " + error.Message;
        }
        report.Add("searchersOpened", searchers.Count);
        report.Add("outcome", outcome);
        report.Add("trajectory", trajectory.ToString());
        report.Heap("atLimit");
        foreach (var searcher in searchers) searcher.Dispose();
        searchers.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        using (var recovered = new IndexSearcher(directory))
        {
            var differences = BrowserPayload.Compare(expected, BrowserPayload.Evaluate(recovered));
            report.Add("recoveredQueries", differences.Count == 0 ? "match" : string.Join("; ", differences));
        }
        report.Heap("afterRecovery");
        directory.Dispose();
        Check(report.Text("recoveredQueries") == "match", "Runtime did not recover after memory pressure: " + report);
        return report.ToString();
    }

    private static double Heap() => WasmBytes?.Invoke() ?? -1;

    private sealed class Report
    {
        private readonly List<(string Key, object Value)> _values = [];

        public void Add(string key, object value) => _values.Add((key, value));
        public void Heap(string stage) => Add("wasmBytes:" + stage, BrowserCases.Heap());
        public double HeapValue(string stage) => Convert.ToDouble(_values.Last(value => value.Key == "wasmBytes:" + stage).Value, CultureInfo.InvariantCulture);
        public string Text(string key) => Convert.ToString(_values.Last(value => value.Key == key).Value, CultureInfo.InvariantCulture)!;
        public double Number(string key) => Convert.ToDouble(_values.Last(value => value.Key == key).Value, CultureInfo.InvariantCulture);

        public override string ToString()
        {
            using var output = new MemoryStream();
            using (var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
            {
                json.WriteStartObject();
                foreach (var (key, value) in _values)
                {
                    switch (value)
                    {
                        case int number: json.WriteNumber(key, number); break;
                        case long number: json.WriteNumber(key, number); break;
                        case double number: json.WriteNumber(key, Math.Round(number, 3)); break;
                        default: json.WriteString(key, Convert.ToString(value, CultureInfo.InvariantCulture)); break;
                    }
                }
                json.WriteEndObject();
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
