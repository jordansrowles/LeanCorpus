using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.LeanCorpus.Benchmark.Workloads;

internal sealed class WorkloadRunner(Func<WorkloadRequest, IWorkloadEngine>? engineFactory = null)
{
    public async Task<WorkloadResult> RunAsync(WorkloadRequest request, string? checkpointPath = null, CancellationToken cancellationToken = default)
    {
        WorkloadProtocol.Validate(request);
        if (Directory.Exists(request.IndexPath) && Directory.EnumerateFileSystemEntries(request.IndexPath).Any())
            throw new InvalidDataException("The workload requires a new empty index directory; existing data is never removed.");
        Directory.CreateDirectory(request.IndexPath);
        var samples = new SampleRecorder(request.DocumentCount + request.SearchOperations);
        var committedIds = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Dictionary<string, (int Ordinal, long Started)>(StringComparer.Ordinal);
        var searchIds = new ConcurrentDictionary<int, string>();
        var counters = new Dictionary<string, double>();
        var unavailable = new Dictionary<string, string>();
        var documents = new List<WorkloadDocument>();
        string? datasetHash = null;
        string? failure = null;
        var outcome = "Completed";
        var preload = 0;
        var admitted = 0;
        var committed = 0;
        var searches = 0;
        var failed = 0;
        var commits = 0;
        var refreshes = 0;
        var visible = 0;
        var peakQueue = 0;
        var verifiedIds = Array.Empty<string>();
        var clock = new Stopwatch();
        var gcBefore = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        long allocationBefore = 0;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = TimeSpan.Zero;
        var cpuAvailable = true;
        try { cpuBefore = process.TotalProcessorTime; }
        catch (Exception exception) { cpuAvailable = false; unavailable["cpuSeconds"] = exception.Message; }
        var runtime = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["runtimeIdentifier"] = RuntimeInformation.RuntimeIdentifier,
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["serverGc"] = GCSettings.IsServerGC.ToString().ToLowerInvariant(),
            ["processorCount"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["tieredPgoEnvironment"] = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "unset"
        };
        using var writes = new ReaderWriterLockSlim();
        using var deadline = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        IWorkloadEngine? engine = null;

        WorkloadResult Snapshot(string state) => new()
        {
            Request = request, Outcome = state, DatasetSha256 = datasetHash,
            PreloadedDocuments = preload, AdmittedDocuments = admitted, CommittedDocuments = committed,
            VisibleDocuments = visible, CompletedSearches = searches, FailedOperations = failed,
            CancelledOperations = state is "Cancelled" or "TimedOut" || state == "Failed" && clock.Elapsed > TimeSpan.Zero
                ? Math.Max(0, request.DocumentCount - preload - committed + request.SearchOperations - searches - failed) : 0,
            ExplicitCommits = commits, Refreshes = refreshes,
            CommittedLogicalIds = committedIds.Order(StringComparer.Ordinal).ToArray(),
            MeasuredSeconds = clock.Elapsed.TotalSeconds,
            DocumentsPerSecond = clock.Elapsed.TotalSeconds > 0 ? committed / clock.Elapsed.TotalSeconds : null,
            RequestsPerSecond = clock.Elapsed.TotalSeconds > 0 ? searches / clock.Elapsed.TotalSeconds : null,
            VerifiedLogicalIds = verifiedIds,
            CompletedSearchLogicalIds = searchIds.OrderBy(static pair => pair.Key).Select(static pair => pair.Value).ToArray(),
            Samples = samples.Values(), Latencies = samples.Summaries(), Counters = new(counters),
            Unavailable = new(unavailable), Runtime = new(runtime), Failure = failure
        };

        void CheckBudget()
        {
            token.ThrowIfCancellationRequested();
            lock (process)
            {
                process.Refresh();
                if (process.WorkingSet64 > (long)request.MemoryBudgetMiB * 1024 * 1024)
                    throw new InvalidOperationException("Observed worker RSS exceeds the requested memory budget.");
            }
        }

        void CommitPending()
        {
            writes.EnterWriteLock();
            try
            {
                if (pending.Count == 0) return;
                CheckBudget();
                engine!.Commit();
                var durable = Stopwatch.GetTimestamp();
                commits++;
                committed += pending.Count;
                foreach (var (id, item) in pending)
                {
                    committedIds.Add(id);
                    samples.Add("commit-completion", item.Ordinal, item.Started, durable);
                }
                engine.Refresh();
                var refreshed = Stopwatch.GetTimestamp();
                refreshes++;
                visible = committedIds.Count;
                foreach (var item in pending.Values)
                    samples.Add("visibility-lag", item.Ordinal, item.Started, refreshed);
                pending.Clear();
                if (checkpointPath is not null)
                    WorkloadProtocol.WriteResult(checkpointPath, Snapshot("Running"));
            }
            finally { writes.ExitWriteLock(); }
        }

        try
        {
            token.ThrowIfCancellationRequested();
            var profile = new LeanCorpusSearchProfile();
            var options = new DataForgeGenerationOptions(request.Seed, request.DocumentCount, new Dictionary<string, string>());
            using (var canonical = new CanonicalJsonWriter(Stream.Null))
            {
                long bytes = 0;
                foreach (var record in profile.Generate(options))
                {
                    token.ThrowIfCancellationRequested();
                    profile.CanonicalRecordWriter.Write(canonical, record);
                    canonical.WriteLine();
                    documents.Add(new WorkloadDocument(record.Id, record.Body));
                    bytes += (record.Id.Length + record.Body.Length) * 2L + 128;
                    if (bytes > (long)request.MemoryBudgetMiB * 1024 * 1024 / 2)
                        throw new InvalidDataException("The materialised DataForge fixture exceeds half the memory budget.");
                }
                datasetHash = Convert.ToHexStringLower(canonical.GetSha256());
            }
            engine = engineFactory?.Invoke(request) ?? (request.Engine == "leancorpus"
                ? new LeanWorkloadEngine(request) : new LuceneWorkloadEngine(request));
            preload = WorkloadProtocol.ExpectedPreload(request);
            foreach (var document in documents.Take(preload))
            {
                engine.Add(document);
                committedIds.Add(document.Id);
            }
            if (preload > 0)
            {
                engine.Commit();
                engine.Refresh();
                WorkloadProtocol.RequireLogicalIds(engine.ReadIds(documents.Count), committedIds);
                visible = preload;
            }
            allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            gcBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            if (cpuAvailable)
            {
                try { cpuBefore = process.TotalProcessorTime; }
                catch (Exception exception) { cpuAvailable = false; unavailable["cpuSeconds"] = exception.Message; }
            }
            clock.Start();
            deadline.CancelAfter(request.DurationMilliseconds);
            var queue = Channel.CreateBounded<(int Ordinal, WorkloadDocument Document)>(new BoundedChannelOptions(request.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, SingleWriter = true, SingleReader = request.Concurrency == 1
            });
            var producer = Task.Run(async () =>
            {
                try
                {
                    for (var i = preload; i < documents.Count; i++)
                    {
                        await queue.Writer.WriteAsync((i, documents[i]), token).ConfigureAwait(false);
                        peakQueue = Math.Max(peakQueue, queue.Reader.Count);
                    }
                    queue.Writer.TryComplete();
                }
                catch (Exception exception) { queue.Writer.TryComplete(exception); throw; }
            }, CancellationToken.None);
            var consumers = Enumerable.Range(0, request.Concurrency).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await foreach (var item in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    {
                        var started = Stopwatch.GetTimestamp();
                        int count;
                        writes.EnterReadLock();
                        try
                        {
                            engine.Add(item.Document);
                            lock (pending) pending.Add(item.Document.Id, (item.Ordinal, started));
                            count = Interlocked.Increment(ref admitted);
                            samples.Add("admission", item.Ordinal, started, Stopwatch.GetTimestamp());
                        }
                        finally { writes.ExitReadLock(); }
                        if (count % request.RefreshEveryDocuments == 0) CommitPending();
                    }
                }
                catch { await linked.CancelAsync().ConfigureAwait(false); throw; }
            }, CancellationToken.None)).ToArray();
            var queryOrdinal = -1;
            var queryWorkers = Enumerable.Range(0, request.Concurrency).Select(_ => Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        var ordinal = Interlocked.Increment(ref queryOrdinal);
                        if (ordinal >= request.SearchOperations) break;
                        if (ordinal % 128 == 0) CheckBudget();
                        // The query sequence never depends on engine ingestion speed or refresh races.
                        var id = documents[ordinal % preload].Id;
                        var started = Stopwatch.GetTimestamp();
                        var actual = engine.Find(id);
                        var ended = Stopwatch.GetTimestamp();
                        if (actual != id) throw new InvalidDataException("A positive term request returned an incorrect logical ID.");
                        searchIds[ordinal] = id;
                        samples.Add("search", ordinal, started, ended);
                        Interlocked.Increment(ref searches);
                    }
                }
                catch { linked.Cancel(); throw; }
            }, CancellationToken.None)).ToArray();
            try { await Task.WhenAll(consumers.Concat(queryWorkers).Append(producer)).ConfigureAwait(false); }
            catch
            {
                // Prefer an actual operation fault over sibling cancellation caused by that fault.
                var faults = consumers.Concat(queryWorkers).Append(producer)
                    .Where(static task => task.Exception is not null)
                    .SelectMany(static task => task.Exception!.Flatten().InnerExceptions)
                    .Where(static exception => exception is not OperationCanceledException).ToArray();
                if (faults.Length > 0) throw faults[0];
                throw;
            }
            CommitPending();
            clock.Stop();
            token.ThrowIfCancellationRequested();
            verifiedIds = engine.ReadIds(documents.Count).Order(StringComparer.Ordinal).ToArray();
            WorkloadProtocol.RequireLogicalIds(verifiedIds, documents.Select(static item => item.Id));
            if (committed != documents.Count - preload || searches != request.SearchOperations)
                throw new InvalidDataException("The workload did not complete its declared logical operation counts.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = cancellationToken.IsCancellationRequested ? "Cancelled" : "TimedOut";
            failure = "The workload stopped with partial measured-operation evidence.";
        }
        catch (Exception exception)
        {
            outcome = "Failed"; failed++; failure = exception.Message;
        }
        finally
        {
            clock.Stop();
            deadline.CancelAfter(Timeout.Infinite);
            counters["allocatedBytes"] = allocationBefore == 0 ? 0 : GC.GetTotalAllocatedBytes(precise: true) - allocationBefore;
            for (var generation = 0; generation < 3; generation++) counters[$"gcGen{generation}"] = GC.CollectionCount(generation) - gcBefore[generation];
            try
            {
                process.Refresh();
                if (cpuAvailable) counters["cpuSeconds"] = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
                counters["rssBytes"] = process.WorkingSet64;
                counters["peakRssBytes"] = process.PeakWorkingSet64;
            }
            catch (Exception exception) { unavailable["processCounters"] = exception.Message; }
            counters["maxObservedQueueDepth"] = peakQueue;
            counters["queueCapacity"] = request.QueueCapacity;
            try { counters["segmentCount"] = engine?.SegmentCount ?? 0; }
            catch (Exception exception) { unavailable["segmentCount"] = exception.Message; }
            try { counters["indexBytes"] = Directory.GetFiles(request.IndexPath, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length); }
            catch (Exception exception) { unavailable["indexBytes"] = exception.Message; }
            // Cleanup is outside the measured interval; interrupted results never qualify a comparison.
            try { engine?.Dispose(); }
            catch (Exception exception) { outcome = "Failed"; failed++; failure = "Worker cleanup failed: " + exception.Message; }
            try { counters["indexBytesAfterCleanup"] = Directory.GetFiles(request.IndexPath, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length); }
            catch (Exception exception) { unavailable["indexBytesAfterCleanup"] = exception.Message; }
            unavailable["diskFlushLatency"] = "Only the engine explicit commit boundary is observed; no device-level tracing.";
            unavailable["hardMemoryLimit"] = "RSS is checked at batch boundaries; OS-enforced resource qualification is separate.";
        }
        return Snapshot(outcome);
    }
}

internal sealed class SampleRecorder(int operations)
{
    private readonly object gate = new();
    private readonly List<LatencySample> samples = [];
    private readonly int stride = Math.Max(1, (operations + 4095) / 4096);
    public void Add(string kind, int ordinal, long start, long end)
    {
        if (ordinal % stride != 0) return;
        lock (gate) samples.Add(new LatencySample(kind, ordinal, Stopwatch.GetElapsedTime(start, end).TotalMilliseconds));
    }
    public LatencySample[] Values() { lock (gate) return samples.ToArray(); }
    public Dictionary<string, LatencySummary> Summaries() => Values().GroupBy(static sample => sample.Operation)
        .ToDictionary(static group => group.Key, static group =>
        {
            var values = group.Select(static sample => sample.Milliseconds).Order().ToArray();
            double Percentile(double p) => values[Math.Max(0, (int)Math.Ceiling(values.Length * p) - 1)];
            return new LatencySummary(values.Length, Percentile(.5), Percentile(.95), Percentile(.99));
        }, StringComparer.Ordinal);
}
