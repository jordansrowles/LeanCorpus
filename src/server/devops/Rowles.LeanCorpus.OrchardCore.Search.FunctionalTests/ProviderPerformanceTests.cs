using System.Net.Http.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests;

[Trait("Area", "Performance")]
public sealed class ProviderPerformanceTests
{
    private const int DocumentCount = 2000;
    private const int CorpusSeed = 3901;
    private const int BatchSize = 100;
    private const int WarmupCount = 3;
    private const int MeasuredCount = 10;
    private const string SharedTerm = "orchardperfshared";
    private static readonly string[] Providers = ["Lucene", "LeanCorpus"];
    private static readonly string[] SampleWords =
    [
        "amber", "bravo", "copper", "delta", "ember", "fable", "garden",
        "harbor", "indigo", "juniper", "kestrel", "lantern", "meadow", "nectar",
    ];
    [Fact(Explicit = true)]
    public async Task Phase00_SeedSharedOrchardCorpus()
    {
        string repositoryRoot = FindRepositoryRoot();
        string outputPath = GetOutputPath(repositoryRoot);
        string statePath = GetRunStatePath(outputPath);
        string contentRoot = Path.Combine(Path.GetTempPath(), "leancorpus-orchard-performance", Guid.NewGuid().ToString("N"));
        var report = new ProviderPerformanceReport
        {
            StartedUtc = DateTimeOffset.UtcNow,
            OutputPath = outputPath,
            TargetDocumentCount = DocumentCount,
            CorpusSeed = CorpusSeed,
            Environment = await CaptureEnvironmentAsync(repositoryRoot),
            MeasurementNotes =
            [
                "The corpus is generated with fixed seed 3901 and contains 2,000 published SearchTestArticle items with short titles, multi-paragraph bodies, keywords, dates, integers, floating values and Unicode.",
                "The bulk setup persists content with YesSQL and explicitly creates Orchard Content indexing tasks. Every full build and rebuild then runs ContentIndexingService.ProcessRecordsAsync for that provider profile.",
                "Orchard Lucene 3.0.1 returns page IDs without a total count and uses the final search argument as an exclusive end offset; the full-build probe checks every Lucene ID and LeanCorpus count plus a returned ID page.",
                "Cold search is the first provider query after the final measured rebuild; operating-system caches are not flushed.",
                "Search timings measure ISearchService.SearchAsync inside the test host and exclude loopback transport time.",
                "Single and batch update timings include Orchard content mutation plus provider-specific task processing after the content transaction commits. Each provider consumes the same task stream independently.",
                "Full-build and rebuild measurements include physical replacement, 2,000 Orchard task replays through content and field handlers, provider writes, durable commits and cursor advancement.",
                "Index size is measured from the provider's persisted index directory. Working set is the test-host process working set after indexing. Allocated bytes are the process-wide allocation delta during that build or rebuild request.",
                "Index-open latency is measured with IIndexManager.ExistsAsync after restarting the Orchard test host over the persisted tenant and index data.",
                "Initial build and rebuild each use three complete runs per provider. Search and single-update latency use one cold, three warm-up and ten measured samples per provider and query where applicable.",
            ],
        };
        PerformanceRunState state = new(contentRoot, statePath, report);

        await RunPhaseAsync(state, async () =>
        {
            await WithHostAsync(state, async (app, http) =>
            {
                using IPlaywright playwright = await Playwright.CreateAsync();
                await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
                IPage page = await browser.NewPageAsync();
                await SearchLifecycleBrowserTests.InstallTestSiteAsync(page, http.BaseAddress!);

                Console.WriteLine($"Seeding {DocumentCount} Orchard performance items.");
                using HttpResponseMessage bulkResponse = await http.PostAsJsonAsync(
                    "/__test/articles/bulk",
                    new OrchardSearchTestHost.BulkArticleRequest(DocumentCount, SharedTerm),
                    TestContext.Current.CancellationToken);
                bulkResponse.EnsureSuccessStatusCode();
                OrchardSearchTestHost.BulkArticleResponse bulk = await bulkResponse.Content
                    .ReadFromJsonAsync<OrchardSearchTestHost.BulkArticleResponse>(TestContext.Current.CancellationToken)

                    ?? throw new InvalidOperationException("Orchard returned no bulk article IDs.");
                Assert.Equal(DocumentCount, bulk.Items.Length);
                state.Items = bulk.Items;
                await SaveRunStateAsync(state);
            });
        });
    }

    [Fact(Explicit = true)]
    public async Task Phase10_BuildAndRebuildLucene()
        => await RunProviderBuildPhaseAsync(await LoadRunStateAsync(), "Lucene");

    [Fact(Explicit = true)]
    public async Task Phase20_BuildAndRebuildLeanCorpus()
        => await RunProviderBuildPhaseAsync(await LoadRunStateAsync(), "LeanCorpus");

    [Fact(Explicit = true)]
    public async Task Phase30_SearchAndSingleDocumentUpdates()
    {
        PerformanceRunState state = await LoadRunStateAsync();
        OrchardSearchTestHost.ContentItemIdentity[] items = state.Items
            ?? throw new InvalidOperationException("The performance corpus phase did not produce article IDs.");

        await RunPhaseAsync(state, () => WithHostAsync(state, async (_, http) =>
        {
            foreach (string provider in Providers)
            {
                string profile = GetProfileName(provider);
                foreach ((string queryKind, string query) in new[]
                {
                    ("term", SharedTerm),
                    ("multi-term", $"{SharedTerm} deterministic"),
                })
                {
                    foreach ((string phase, int iteration) in GetSamplePlan())
                    {
                        OrchardSearchTestHost.PerformanceSearchSnapshot snapshot = await SearchAsync(http, provider, profile, query);
                        state.Report.SearchSamples.Add(new LatencySample(
                            provider, queryKind, phase, iteration, snapshot.ElapsedMilliseconds,
                            snapshot.Success, snapshot.ContentItemIds.Length, snapshot.ReportedTotalCount));
                        Assert.True(snapshot.Success, $"{provider} {queryKind} search failed.");
                        Assert.Equal(10, snapshot.ContentItemIds.Length);
                    }
                }
            }

            int sampleNumber = 0;
            foreach ((string phase, int iteration) in GetSamplePlan())
            {
                string term = $"orchardupdatesingle{SampleWords[sampleNumber]}";
                var update = new OrchardSearchTestHost.PerformanceUpdateRequest(
                    $"Orchard performance update {term}",
                    $"{SharedTerm} deterministic {term} updated Orchard performance content",
                    term);
                using HttpResponseMessage response = await http.PostAsJsonAsync(
                    $"/__test/performance/update/{items[0].ContentItemId}", update,
                    TestContext.Current.CancellationToken);
                response.EnsureSuccessStatusCode();
                OrchardSearchTestHost.PerformanceMutationSnapshot mutation = await response.Content
                    .ReadFromJsonAsync<OrchardSearchTestHost.PerformanceMutationSnapshot>(TestContext.Current.CancellationToken)
                    ?? throw new InvalidOperationException("Orchard returned no single-document update measurement.");

                foreach (string provider in Providers)
                {
                    string profile = GetProfileName(provider);
                    OrchardSearchTestHost.PerformanceProcessingSnapshot processing = await ProcessAsync(http, provider, profile);
                    OrchardSearchTestHost.PerformanceSearchSnapshot visibility = await SearchAsync(http, provider, profile, term);
                    bool expectedItemFound = visibility.ContentItemIds.Contains(items[0].ContentItemId, StringComparer.Ordinal);
                    state.Report.UpdateSamples.Add(new LatencySample(
                        provider, "single-update", phase, iteration,
                        mutation.ElapsedMilliseconds + processing.ElapsedMilliseconds,
                        visibility.Success && expectedItemFound, visibility.ContentItemIds.Length, null));
                    Assert.True(visibility.Success, $"{provider} search failed after the performance update.");
                    Assert.True(expectedItemFound, $"{provider} did not expose the updated term '{term}'.");
                }
                sampleNumber++;
            }
        }));
    }

    [Fact(Explicit = true)]
    public async Task Phase40_BatchMutationsAndIndexRestart()
    {
        PerformanceRunState state = await LoadRunStateAsync();
        OrchardSearchTestHost.ContentItemIdentity[] allItems = state.Items
            ?? throw new InvalidOperationException("The performance corpus phase did not produce article IDs.");

        await RunPhaseAsync(state, async () =>
        {
            await WithHostAsync(state, async (_, http) =>
            {
                for (int batchRun = 1; batchRun <= 3; batchRun++)
                {
                    OrchardSearchTestHost.ContentItemIdentity[] items = allItems.Skip((batchRun - 1) * BatchSize).Take(BatchSize).ToArray();
                    string term = $"orchardbatchupdate{batchRun}";
                    var request = new OrchardSearchTestHost.PerformanceBatchMutationRequest(
                        items.Select(item => item.ContentItemId).ToArray(), SharedTerm, term);
                    using HttpResponseMessage updateResponse = await http.PostAsJsonAsync(
                        "/__test/performance/update-batch", request, TestContext.Current.CancellationToken);
                    updateResponse.EnsureSuccessStatusCode();
                    OrchardSearchTestHost.PerformanceMutationSnapshot mutation = await updateResponse.Content
                        .ReadFromJsonAsync<OrchardSearchTestHost.PerformanceMutationSnapshot>(TestContext.Current.CancellationToken)
                        ?? throw new InvalidOperationException("Orchard returned no batch update measurement.");
                    Assert.Equal(BatchSize, mutation.ItemCount);

                    foreach (string provider in Providers)
                    {
                        string profile = GetProfileName(provider);
                        OrchardSearchTestHost.PerformanceProcessingSnapshot processing = await ProcessAsync(http, provider, profile);
                        OrchardSearchTestHost.PerformanceSearchSnapshot visibility = await SearchAsync(http, provider, profile, term, BatchSize);
                        HashSet<string> expectedBatchIds = items.Select(item => item.ContentItemId).ToHashSet(StringComparer.Ordinal);
                        bool foundBatchItems = visibility.ContentItemIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedBatchIds);
                        double elapsed = mutation.ElapsedMilliseconds + processing.ElapsedMilliseconds;
                        state.Report.BatchUpdateRuns.Add(new ThroughputRun(
                            provider, batchRun, BatchSize, elapsed, BatchSize / (elapsed / 1000d),
                            visibility.Success && foundBatchItems));
                        Assert.True(visibility.Success && foundBatchItems,
                            $"{provider} exposed {visibility.ContentItemIds.Length} results for the batch term, expected all {BatchSize} updated items.");
                    }

                    using HttpResponseMessage deleteResponse = await http.PostAsJsonAsync(
                        "/__test/performance/delete-batch", request, TestContext.Current.CancellationToken);
                    deleteResponse.EnsureSuccessStatusCode();
                    OrchardSearchTestHost.PerformanceMutationSnapshot deletion = await deleteResponse.Content
                        .ReadFromJsonAsync<OrchardSearchTestHost.PerformanceMutationSnapshot>(TestContext.Current.CancellationToken)
                        ?? throw new InvalidOperationException("Orchard returned no batch delete measurement.");
                    Assert.Equal(BatchSize, deletion.ItemCount);

                    foreach (string provider in Providers)
                    {
                        string profile = GetProfileName(provider);
                        OrchardSearchTestHost.PerformanceProcessingSnapshot processing = await ProcessAsync(http, provider, profile);
                        OrchardSearchTestHost.PerformanceSearchSnapshot visibility = await SearchAsync(http, provider, profile, term);
                        double elapsed = deletion.ElapsedMilliseconds + processing.ElapsedMilliseconds;
                        state.Report.DeleteRuns.Add(new ThroughputRun(
                            provider, batchRun, BatchSize, elapsed, BatchSize / (elapsed / 1000d),
                            visibility.Success && visibility.ContentItemIds.Length == 0));
                        Assert.True(visibility.Success && visibility.ContentItemIds.Length == 0, $"{provider} retained a deleted batch item.");
                    }
                }

                foreach (string provider in Providers)
                    await AssertCursorDrainedAsync(http, provider, GetProfileName(provider));
            });

            long hostStarted = Stopwatch.GetTimestamp();
            (WebApplication restartedApp, Uri restartedAddress) = await OrchardSearchTestHost.StartIsolatedAsync(state.ContentRoot);
            double hostStartupMilliseconds = Stopwatch.GetElapsedTime(hostStarted).TotalMilliseconds;
            using HttpClient restartedHttp = CreateClient(restartedAddress);
            try
            {
                foreach (string provider in Providers)
                {
                    string profile = GetProfileName(provider);
                    using HttpResponseMessage openResponse = await restartedHttp.GetAsync(
                        $"/__test/performance/open/{provider}/{profile}", TestContext.Current.CancellationToken);
                    openResponse.EnsureSuccessStatusCode();
                    OrchardSearchTestHost.PerformanceIndexOpenSnapshot opened = await openResponse.Content
                        .ReadFromJsonAsync<OrchardSearchTestHost.PerformanceIndexOpenSnapshot>(TestContext.Current.CancellationToken)
                        ?? throw new InvalidOperationException($"Orchard returned no {provider} index-open measurement.");
                    Assert.True(opened.Exists, $"The {provider} index did not reopen after host restart.");
                    state.Report.StartupRuns.Add(new StartupRun(provider, hostStartupMilliseconds, opened.ElapsedMilliseconds, opened.Exists));
                }
            }
            finally
            {
                await OrchardSearchTestHost.StopIsolatedAsync(restartedApp);
            }
        }, complete: true);
    }

    private static async Task RunProviderBuildPhaseAsync(PerformanceRunState state, string provider)
    {
        OrchardSearchTestHost.ContentItemIdentity[] items = state.Items
            ?? throw new InvalidOperationException("The performance corpus phase did not produce article IDs.");
        string profile = GetProfileName(provider);

        await RunPhaseAsync(state, () => WithHostAsync(state, async (_, http) =>
        {
            for (int iteration = 1; iteration <= 3; iteration++)
            {
                Console.WriteLine($"Starting {provider} initial build {iteration}/3.");
                OrchardSearchTestHost.RebuildSnapshot build = await BuildAsync(http, provider, profile, items);
                state.Report.InitialBuilds.Add(new InitialBuildRun(
                    provider, iteration, build.ElapsedMilliseconds, build.LastTaskId,
                    build.AllocatedBytes, build.WorkingSetBytes, build.IndexSizeBytes));
                await AssertCursorDrainedAsync(http, provider, profile);
            }

            for (int iteration = 1; iteration <= 3; iteration++)
            {
                Console.WriteLine($"Starting {provider} rebuild {iteration}/3.");
                OrchardSearchTestHost.RebuildSnapshot rebuild = await BuildAsync(http, provider, profile, items);
                state.Report.Rebuilds.Add(new RebuildRun(
                    provider, iteration, rebuild.ElapsedMilliseconds, rebuild.LastTaskId,
                    rebuild.AllocatedBytes, rebuild.WorkingSetBytes, rebuild.IndexSizeBytes));
                await AssertCursorDrainedAsync(http, provider, profile);
            }
        }));
    }

    private static async Task RunPhaseAsync(PerformanceRunState state, Func<Task> action, bool complete = false)
    {
        state.Report.Status = "running";
        await SaveReportAsync(state.Report);
        try
        {
            await action();
            if (complete)
            {
                state.Report.Status = "passed";
                state.Report.CompletedUtc = DateTimeOffset.UtcNow;
                state.Report.SearchSummaries = Summarise(state.Report.SearchSamples);
                state.Report.UpdateSummaries = Summarise(state.Report.UpdateSamples);
                state.Report.InitialBuildSummaries = SummariseBuilds(state.Report.InitialBuilds.Select(run => (run.Provider, run.ElapsedMilliseconds)));
                state.Report.RebuildSummaries = SummariseBuilds(state.Report.Rebuilds.Select(run => (run.Provider, run.ElapsedMilliseconds)));
            }
        }
        catch (Exception exception)
        {
            state.Report.Status = "failed";
            state.Report.Failure = $"{exception.GetType().Name}: {exception.Message}";
            state.Report.CompletedUtc = DateTimeOffset.UtcNow;
            throw;
        }
        finally
        {
            await SaveReportAsync(state.Report);
            if (state.Report.Status == "passed")
            {
                if (Directory.Exists(state.ContentRoot))
                    await OrchardSearchTestHost.DeleteContentRootAsync(state.ContentRoot);
                if (File.Exists(state.StatePath))
                    File.Delete(state.StatePath);
            }
            else if (Directory.Exists(state.ContentRoot))
                Console.WriteLine($"Orchard performance content root retained at {state.ContentRoot}");
        }
    }

    private static async Task WithHostAsync(PerformanceRunState state, Func<WebApplication, HttpClient, Task> action)
    {
        (WebApplication app, Uri address) = await OrchardSearchTestHost.StartIsolatedAsync(state.ContentRoot);
        using HttpClient http = CreateClient(address);
        try
        {
            await action(app, http);
        }
        finally
        {
            await OrchardSearchTestHost.StopIsolatedAsync(app);
        }
    }

    private static async Task<PerformanceRunState> LoadRunStateAsync()
    {
        string outputPath = GetOutputPath(FindRepositoryRoot());
        string statePath = GetRunStatePath(outputPath);
        if (!File.Exists(statePath))
            throw new InvalidOperationException("Run Phase00_SeedSharedOrchardCorpus first, then run the remaining explicit phases one at a time.");

        await using var stateStream = File.OpenRead(statePath);
        PersistedPerformanceRunState persisted = await JsonSerializer.DeserializeAsync<PersistedPerformanceRunState>(
            stateStream, cancellationToken: TestContext.Current.CancellationToken)
            ?? throw new InvalidDataException("The Orchard performance run state is empty.");
        await using var reportStream = File.OpenRead(outputPath);
        ProviderPerformanceReport report = await JsonSerializer.DeserializeAsync<ProviderPerformanceReport>(
            reportStream, ReportSerializerOptions, TestContext.Current.CancellationToken)
            ?? throw new InvalidDataException("The Orchard performance report is empty.");
        PerformanceRunState state = new(persisted.ContentRoot, statePath, report)
        {
            Items = persisted.Items,
        };
        if (state.Report.Status == "failed")
            throw new InvalidOperationException("An earlier explicit performance phase failed; rerun Phase00_SeedSharedOrchardCorpus to start a fresh run.");
        return state;
    }

    private static string GetOutputPath(string repositoryRoot)
        => Path.GetFullPath(Environment.GetEnvironmentVariable("LEANCORE_ORCHARD_PERF_OUTPUT")
            ?? Path.Combine(repositoryRoot, "docs", "server", "evidence", "orchard-core-search-provider-runs.json"));

    private static string GetRunStatePath(string outputPath)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outputPath))).ToLowerInvariant()[..16];
        return Path.Combine(Path.GetTempPath(), "leancorpus-orchard-performance", $"{key}.state.json");
    }

    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    private sealed record PersistedPerformanceRunState(
        string ContentRoot,
        OrchardSearchTestHost.ContentItemIdentity[] Items);

    private static async Task SaveRunStateAsync(PerformanceRunState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(state.StatePath)!);
        await using var stream = File.Create(state.StatePath);
        await JsonSerializer.SerializeAsync(stream,
            new PersistedPerformanceRunState(
                state.ContentRoot,
                state.Items ?? throw new InvalidOperationException("The Orchard performance corpus has no saved item IDs.")),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class PerformanceRunState(string contentRoot, string statePath, ProviderPerformanceReport report)
    {
        public string ContentRoot { get; } = contentRoot;
        public string StatePath { get; } = statePath;
        public ProviderPerformanceReport Report { get; } = report;
        public OrchardSearchTestHost.ContentItemIdentity[]? Items { get; set; }
    }

    private static async Task<OrchardSearchTestHost.RebuildSnapshot> BuildAsync(
        HttpClient http,
        string provider,
        string profile,
        OrchardSearchTestHost.ContentItemIdentity[] items)
    {
        using HttpResponseMessage rebuildResponse = await http.PostAsync(
            $"/__test/performance/rebuild/{provider}/{profile}",
            content: null,
            TestContext.Current.CancellationToken);
        rebuildResponse.EnsureSuccessStatusCode();
        OrchardSearchTestHost.PerformanceRebuildSnapshot rebuild = await rebuildResponse.Content
            .ReadFromJsonAsync<OrchardSearchTestHost.PerformanceRebuildSnapshot>(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"Orchard returned no {provider} index replacement measurement.");

        using HttpResponseMessage tasksResponse = await http.PostAsJsonAsync(
            "/__test/performance/tasks",
            new OrchardSearchTestHost.PerformanceTaskRequest(items.Select(item => item.ContentItemId).ToArray()),
            TestContext.Current.CancellationToken);
        tasksResponse.EnsureSuccessStatusCode();

        using HttpResponseMessage response = await http.PostAsJsonAsync(
            $"/__test/performance/process/{provider}/{profile}",
            new OrchardSearchTestHost.PerformanceBuildRequest(
                DocumentCount,
            items.Select(item => item.ContentItemId).ToArray(),
            SharedTerm),
            TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Orchard {provider} performance task replay returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        OrchardSearchTestHost.RebuildSnapshot snapshot = await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.RebuildSnapshot>(TestContext.Current.CancellationToken)

            ?? throw new InvalidOperationException($"Orchard returned no {provider} full-build measurement.");
        Assert.Equal(DocumentCount, snapshot.SearchableDocumentCount);
        return snapshot with
        {
            ElapsedMilliseconds = rebuild.ElapsedMilliseconds + snapshot.ElapsedMilliseconds,
            AllocatedBytes = rebuild.AllocatedBytes + snapshot.AllocatedBytes,
        };
    }

    private static HttpClient CreateClient(Uri address)
        => new() { BaseAddress = address, Timeout = TimeSpan.FromMinutes(5) };

    private static async Task<OrchardSearchTestHost.PerformanceSearchSnapshot> SearchAsync(
        HttpClient http,
        string provider,
        string profile,
        string term,
        int size = 10)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            $"/__test/performance/search/{provider}/{profile}",
            new OrchardSearchTestHost.PerformanceSearchRequest(term, 0, size),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.PerformanceSearchSnapshot>(TestContext.Current.CancellationToken)

            ?? throw new InvalidOperationException($"Orchard returned no {provider} search measurement.");
    }

    private static async Task<OrchardSearchTestHost.PerformanceProcessingSnapshot> ProcessAsync(
        HttpClient http,
        string provider,
        string profile)
    {
        using HttpResponseMessage response = await http.PostAsync(
            $"/__test/indexing/process/{provider}/{profile}",
            content: null,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.PerformanceProcessingSnapshot>(TestContext.Current.CancellationToken)

            ?? throw new InvalidOperationException($"Orchard returned no {provider} indexing-task processing measurement.");
    }

    private static async Task AssertCursorDrainedAsync(HttpClient http, string provider, string profile)
    {
        OrchardSearchTestHost.IndexingCursorSnapshot cursor = await GetCursorAsync(http, provider, profile);
        Assert.False(cursor.HasPendingContentTasks, $"Orchard left pending tasks after the {provider} performance build.");
    }

    private static async Task<OrchardSearchTestHost.IndexingCursorSnapshot> GetCursorAsync(HttpClient http, string provider, string profile)
        => await http.GetFromJsonAsync<OrchardSearchTestHost.IndexingCursorSnapshot>(
            $"/__test/indexes/{provider}/{profile}/cursor",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"Orchard returned no {provider} cursor.");

    private static IEnumerable<(string Phase, int Iteration)> GetSamplePlan()
    {
        yield return ("cold", 0);
        for (int iteration = 1; iteration <= WarmupCount; iteration++)
            yield return ("warmup", iteration);
        for (int iteration = 1; iteration <= MeasuredCount; iteration++)
            yield return ("measured", iteration);
    }

    private static string GetProfileName(string provider)
        => provider.Equals("Lucene", StringComparison.Ordinal) ? "Search-Lucene" : "Search-LeanCorpus";

    private static Dictionary<string, LatencySummary> Summarise(IEnumerable<LatencySample> samples)
        => samples.GroupBy(sample => $"{sample.Provider}:{sample.QueryKind}", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new LatencySummary(
                    group.Count(sample => sample.Phase == "cold"),
                    group.Count(sample => sample.Phase == "warmup"),
                    group.Count(sample => sample.Phase == "measured"),
                    SummariseValues(group.Where(sample => sample.Phase == "measured").Select(sample => sample.ElapsedMilliseconds))),
                StringComparer.Ordinal);

    private static Dictionary<string, BuildSummary> SummariseBuilds(IEnumerable<(string Provider, double ElapsedMilliseconds)> samples)
        => samples.GroupBy(sample => sample.Provider, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new BuildSummary(
                    group.Count(),
                    SummariseValues(group.Select(sample => sample.ElapsedMilliseconds))),
                StringComparer.Ordinal);

    private static LatencyStatistics SummariseValues(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        if (sorted.Length == 0)
            return new LatencyStatistics(null, null, null, null, null);
        double median = sorted.Length % 2 == 0
            ? (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2d
            : sorted[sorted.Length / 2];
        int p95Index = Math.Clamp((int)Math.Ceiling(sorted.Length * 0.95d) - 1, 0, sorted.Length - 1);
        return new LatencyStatistics(sorted[0], median, sorted[p95Index], sorted[^1], sorted.Length);
    }

    private static async Task SaveReportAsync(ProviderPerformanceReport report)
    {
        string? directory = Path.GetDirectoryName(report.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(report, ReportSerializerOptions);
        await File.WriteAllTextAsync(report.OutputPath, json, CancellationToken.None);
    }

    private static async Task<PerformanceEnvironment> CaptureEnvironmentAsync(string repositoryRoot)
    {
        string[] status = await RunCommandAsync(repositoryRoot, "git", "status", "--short");
        string[] sdk = await RunCommandAsync(repositoryRoot, "dotnet", "--version");
        string[] root = await RunCommandAsync(repositoryRoot, "git", "rev-parse", "--show-toplevel");
        string[] branch = await RunCommandAsync(repositoryRoot, "git", "branch", "--show-current");
        string[] commit = await RunCommandAsync(repositoryRoot, "git", "rev-parse", "HEAD");
        return new PerformanceEnvironment(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            AppContext.TargetFrameworkName ?? "unknown",
            sdk.FirstOrDefault() ?? "unknown",
            GetCpuModel(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            GC.GetTotalMemory(forceFullCollection: false),
            Stopwatch.Frequency,
            System.Runtime.GCSettings.IsServerGC,
            root.FirstOrDefault() ?? repositoryRoot,
            branch.FirstOrDefault() ?? "unknown",
            commit.FirstOrDefault() ?? "unknown",
            status.Length > 0,
            status);
    }

    private static async Task<string[]> RunCommandAsync(string workingDirectory, string executable, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start())
            return [];
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0
            ? output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    private static string GetCpuModel()
    {
        if (File.Exists("/proc/cpuinfo"))
        {
            string? line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(value => value.StartsWith("model name", StringComparison.Ordinal));
            if (line is not null)
            {
                int separator = line.IndexOf(':');
                if (separator >= 0)
                    return line[(separator + 1)..].Trim();
            }
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown";
    }

    private static string FindRepositoryRoot()
    {
        foreach (string startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(startingPath));
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the LeanCorpus Git repository root for performance provenance.");
    }

    private sealed class ProviderPerformanceReport
    {
        public string Status { get; set; } = "running";
        public string OutputPath { get; set; } = string.Empty;
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset CompletedUtc { get; set; }
        public int TargetDocumentCount { get; set; }
        public int CorpusSeed { get; set; }
        public string[] MeasurementNotes { get; set; } = [];
        public PerformanceEnvironment? Environment { get; set; }
        public List<InitialBuildRun> InitialBuilds { get; } = [];
        public List<RebuildRun> Rebuilds { get; } = [];
        public List<LatencySample> SearchSamples { get; } = [];
        public List<LatencySample> UpdateSamples { get; } = [];
        public List<ThroughputRun> BatchUpdateRuns { get; } = [];
        public List<ThroughputRun> DeleteRuns { get; } = [];
        public List<StartupRun> StartupRuns { get; } = [];
        public Dictionary<string, LatencySummary> SearchSummaries { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, LatencySummary> UpdateSummaries { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, BuildSummary> InitialBuildSummaries { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, BuildSummary> RebuildSummaries { get; set; } = new(StringComparer.Ordinal);
        public string? Failure { get; set; }
    }

    private sealed record PerformanceEnvironment(
        string OsDescription,
        string OsArchitecture,
        string ProcessArchitecture,
        string Runtime,
        string TargetFramework,
        string SdkVersion,
        string CpuModel,
        int ProcessorCount,
        long AvailableMemoryBytes,
        long ManagedHeapBytesAtCapture,
        long StopwatchFrequency,
        bool ServerGc,
        string RepositoryRoot,
        string Branch,
        string Commit,
        bool Dirty,
        string[] GitStatus);

    private sealed record RebuildRun(
        string Provider,
        int Iteration,
        double ElapsedMilliseconds,
        long LastTaskId,
        long AllocatedBytes,
        long WorkingSetBytes,
        long IndexSizeBytes);

    private sealed record InitialBuildRun(
        string Provider,
        int Iteration,
        double ElapsedMilliseconds,
        long LastTaskId,
        long AllocatedBytes,
        long WorkingSetBytes,
        long IndexSizeBytes);

    private sealed record LatencySample(
        string Provider,
        string QueryKind,
        string Phase,
        int Iteration,
        double ElapsedMilliseconds,
        bool Success,
        int ResultCount,
        long? ReportedTotalCount);

    private sealed record LatencySummary(int ColdSamples, int WarmupSamples, int MeasuredSamples, LatencyStatistics Measured);

    private sealed record BuildSummary(int RunCount, LatencyStatistics ElapsedMilliseconds);

    private sealed record ThroughputRun(
        string Provider,
        int Iteration,
        int ItemCount,
        double ElapsedMilliseconds,
        double ItemsPerSecond,
        bool Success);

    private sealed record StartupRun(
        string Provider,
        double HostStartupMilliseconds,
        double IndexOpenMilliseconds,
        bool Exists);

    private sealed record LatencyStatistics(double? MinMilliseconds, double? MedianMilliseconds, double? P95Milliseconds, double? MaxMilliseconds, int? SampleCount);
}
