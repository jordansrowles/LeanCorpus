using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Benchmark.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class SustainedWorkloadTests
{
    [Theory]
    [InlineData("ingest-throughput")]
    [InlineData("search-readonly")]
    [InlineData("ingest-search-refresh")]
    public async Task Both_engines_complete_the_same_bounded_logical_work(string scenario)
    {
        var expected = new LeanCorpusSearchProfile()
            .Generate(new DataForgeGenerationOptions(42, 32, new Dictionary<string, string>()))
            .Select(static record => record.Id).Order(StringComparer.Ordinal).ToArray();
        var results = new List<WorkloadResult>();
        foreach (var engine in WorkloadProtocol.Engines)
        {
            var request = Request(engine, scenario);
            try
            {
                var result = await new WorkloadRunner().RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);
                Assert.Equal("Completed", result.Outcome);
                Assert.Equal(expected, result.VerifiedLogicalIds);
                Assert.Equal(expected, result.CommittedLogicalIds);
                Assert.Equal(request.SearchOperations, result.CompletedSearches);
                Assert.Equal(32 - result.PreloadedDocuments, result.CommittedDocuments);
                Assert.Equal(result.AdmittedDocuments, result.CommittedDocuments);
                Assert.Equal(0, result.FailedOperations);
                Assert.Equal(0, result.CancelledOperations);
                Assert.InRange(result.Counters["maxObservedQueueDepth"], 0, request.QueueCapacity);
                Assert.NotEmpty(result.Samples);
                Assert.Equal("Unqualified", result.Classification);
                results.Add(result);
            }
            finally { Remove(request.IndexPath); }
        }
        Assert.True(WorkloadProtocol.HasMatchedCompletedWork(results[0], results[1]));
        Assert.True(WorkloadProtocol.HasMatchedCompletedWork(results[0], results[1] with
        {
            Request = results[1].Request with { SourceDirty = true }
        }));
        Assert.False(WorkloadProtocol.HasMatchedCompletedWork(results[0], results[1] with { FailedOperations = 1 }));
        Assert.False(WorkloadProtocol.HasMatchedCompletedWork(results[0], results[1] with { VerifiedLogicalIds = [] }));
        Assert.False(WorkloadProtocol.HasMatchedCompletedWork(results[0], results[0]));
    }

    [Fact]
    public async Task Incorrect_search_result_fails_the_worker()
    {
        var request = Request("lucenenet", "search-readonly");
        try
        {
            var result = await new WorkloadRunner(value => new EngineWrapper(new LuceneWorkloadEngine(value), wrongSearchResult: true))
                .RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("Failed", result.Outcome);
            Assert.True(result.FailedOperations > 0);
            Assert.Equal(0, result.CompletedSearches);
            Assert.Equal(31, result.CancelledOperations);
            Assert.Contains("incorrect logical ID", result.Failure, StringComparison.Ordinal);
        }
        finally { Remove(request.IndexPath); }
    }

    [Fact]
    public async Task Cancellation_retains_committed_partial_evidence_without_claiming_completion()
    {
        var request = Request("lucenenet", "ingest-throughput") with
        {
            Concurrency = 1, RefreshEveryDocuments = 4
        };
        var partial = Path.Combine(Path.GetTempPath(), "workload-partial-" + Guid.NewGuid().ToString("N") + ".json");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var result = await new WorkloadRunner(value => new EngineWrapper(new LuceneWorkloadEngine(value),
                onAdd: count => { if (count == 9) cancellation.Cancel(); }))
                .RunAsync(request, partial, cancellation.Token);
            Assert.Equal("Cancelled", result.Outcome);
            Assert.Equal(8, result.CommittedDocuments);
            Assert.Equal(8, result.CommittedLogicalIds.Length);
            Assert.NotEqual(request.DocumentCount, result.VisibleDocuments);
            Assert.True(result.CancelledOperations > 0);
            Assert.Equal(24, result.CancelledOperations);
            Assert.True(File.Exists(partial));
            var checkpoint = System.Text.Json.JsonSerializer.Deserialize<WorkloadResult>(File.ReadAllBytes(partial), WorkloadProtocol.JsonOptions)!;
            Assert.Equal("Running", checkpoint.Outcome);
            Assert.Equal(8, checkpoint.CommittedDocuments);
            Assert.Equal(8, checkpoint.CommittedLogicalIds.Length);
        }
        finally
        {
            Remove(request.IndexPath);
            File.Delete(partial);
        }
    }

    [Fact]
    public async Task Deadline_returns_timed_out_partial_evidence()
    {
        var request = Request("lucenenet", "ingest-throughput") with { DurationMilliseconds = 1 };
        try
        {
            var result = await new WorkloadRunner(value => new EngineWrapper(new LuceneWorkloadEngine(value), sleepOnAdd: true))
                .RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("TimedOut", result.Outcome);
            Assert.True(result.CommittedDocuments < request.DocumentCount);
            Assert.True(result.CancelledOperations > 0);
            Assert.Empty(result.VerifiedLogicalIds);
        }
        finally { Remove(request.IndexPath); }
    }

    [Fact]
    public async Task Existing_index_data_is_never_reused_or_deleted()
    {
        var request = Request("lucenenet", "ingest-throughput");
        Directory.CreateDirectory(request.IndexPath);
        var sentinel = Path.Combine(request.IndexPath, "sentinel.txt");
        File.WriteAllText(sentinel, "preserve");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new WorkloadRunner().RunAsync(request));
            Assert.Equal("preserve", File.ReadAllText(sentinel));
        }
        finally { Remove(request.IndexPath); }
    }

    [Fact]
    public void Request_validation_rejects_mismatched_schema_and_budgets()
    {
        var request = Request("lucenenet", "ingest-throughput");
        Assert.Throws<InvalidDataException>(() => WorkloadProtocol.Validate(request with { SchemaVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => WorkloadProtocol.Validate(request with { Dataset = "unknown" }));
        Assert.Throws<InvalidDataException>(() => WorkloadProtocol.Validate(request with { SearchOperations = 1 }));
        Assert.Throws<InvalidDataException>(() => WorkloadProtocol.Validate(request with { MemoryBudgetMiB = 1 }));
        Assert.Throws<InvalidDataException>(() => WorkloadProtocol.Validate(request with { SourceCommit = "dirty" }));
    }

    private static WorkloadRequest Request(string engine, string scenario) => new()
    {
        Engine = engine, Scenario = scenario, DocumentCount = 32,
        SearchOperations = scenario == "ingest-throughput" ? 0 : 32,
        SourceCommit = new string('a', 40),
        IndexPath = Path.Combine(Path.GetTempPath(), "workload-" + Guid.NewGuid().ToString("N")),
        QueueCapacity = 1, Concurrency = 4, RefreshEveryDocuments = 8,
        MemoryBudgetMiB = 4096, DurationMilliseconds = 30_000
    };

    private static void Remove(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class EngineWrapper(IWorkloadEngine inner, Action<int>? onAdd = null,
        bool wrongSearchResult = false, bool sleepOnAdd = false) : IWorkloadEngine
    {
        private int added;
        public void Add(WorkloadDocument document)
        {
            if (sleepOnAdd) Thread.Sleep(25);
            inner.Add(document);
            onAdd?.Invoke(Interlocked.Increment(ref added));
        }
        public void Commit() => inner.Commit();
        public void Refresh() => inner.Refresh();
        public string? Find(string id) => wrongSearchResult ? "wrong" : inner.Find(id);
        public string[] ReadIds(int maximum) => inner.ReadIds(maximum);
        public int SegmentCount => inner.SegmentCount;
        public void Dispose() => inner.Dispose();
    }
}
