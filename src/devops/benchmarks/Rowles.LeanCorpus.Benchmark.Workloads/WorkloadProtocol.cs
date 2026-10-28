using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.Benchmark.Workloads;

internal sealed record WorkloadRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string Scenario { get; init; } = "ingest-throughput";
    public string Engine { get; init; } = "leancorpus";
    public string Dataset { get; init; } = "leancorpus-search";
    public int DatasetVersion { get; init; } = 1;
    public ulong Seed { get; init; } = 42;
    public int DocumentCount { get; init; } = 128;
    public int SearchOperations { get; init; }
    public int Concurrency { get; init; } = 2;
    public int QueueCapacity { get; init; } = 16;
    public int RefreshEveryDocuments { get; init; } = 16;
    public int DurationMilliseconds { get; init; } = 10_000;
    public int MemoryBudgetMiB { get; init; } = 512;
    public string Durability { get; init; } = "explicit-commit";
    public string SourceCommit { get; init; } = string.Empty;
    public bool SourceDirty { get; init; }
    public string IndexPath { get; init; } = string.Empty;
}

internal sealed record LatencySample(string Operation, int Ordinal, double Milliseconds);
internal sealed record LatencySummary(int Samples, double? P50, double? P95, double? P99);
internal sealed record WorkloadResult
{
    public int SchemaVersion { get; init; } = 1;
    public required WorkloadRequest Request { get; init; }
    public required string Outcome { get; init; }
    public string Classification { get; init; } = "Unqualified";
    public string CompletionBoundary { get; init; } = "explicit-commit-during-measured-interval";
    public string QueryContract { get; init; } = "uncached-positive-id-term; matched-preloaded-query-sequence";
    public string SamplePolicy { get; init; } = "deterministic-operation-stride; at-most-4096-per-kind";
    public string? DatasetSha256 { get; init; }
    public int PreloadedDocuments { get; init; }
    public int AdmittedDocuments { get; init; }
    public int CommittedDocuments { get; init; }
    public int VisibleDocuments { get; init; }
    public int CompletedSearches { get; init; }
    public int FailedOperations { get; init; }
    public int CancelledOperations { get; init; }
    public int ExplicitCommits { get; init; }
    public int Refreshes { get; init; }
    public string[] CommittedLogicalIds { get; init; } = [];
    public double MeasuredSeconds { get; init; }
    public double? DocumentsPerSecond { get; init; }
    public double? RequestsPerSecond { get; init; }
    public string[] VerifiedLogicalIds { get; init; } = [];
    public string[] CompletedSearchLogicalIds { get; init; } = [];
    public LatencySample[] Samples { get; init; } = [];
    public Dictionary<string, LatencySummary> Latencies { get; init; } = [];
    public Dictionary<string, double> Counters { get; init; } = [];
    public Dictionary<string, string> Unavailable { get; init; } = [];
    public Dictionary<string, string> Runtime { get; init; } = [];
    public string? Failure { get; init; }
}

internal static class WorkloadProtocol
{
    public static readonly string[] Scenarios = ["ingest-throughput", "search-readonly", "ingest-search-refresh"];
    public static readonly string[] Engines = ["leancorpus", "lucenenet"];
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void Validate(WorkloadRequest request)
    {
        if (request.SchemaVersion != 1 || !Scenarios.Contains(request.Scenario, StringComparer.Ordinal) ||
            !Engines.Contains(request.Engine, StringComparer.Ordinal) || request.Dataset != "leancorpus-search" || request.DatasetVersion != 1)
            throw new InvalidDataException("Unsupported workload schema, scenario, engine or dataset identity.");
        if (request.DocumentCount is < 2 or > 100_000 || request.Concurrency is < 1 or > 32 ||
            request.QueueCapacity is < 1 or > 4096 || request.SearchOperations is < 0 or > 1_000_000 ||
            request.RefreshEveryDocuments < 1 || request.DurationMilliseconds is < 1 or > 3_600_000 ||
            request.MemoryBudgetMiB is < 128 or > 65_536)
            throw new InvalidDataException("Workload counts, concurrency, queue, refresh and resource budgets must be bounded.");
        if ((request.Scenario == "ingest-throughput") != (request.SearchOperations == 0) || request.Durability != "explicit-commit")
            throw new InvalidDataException("Search operation count and explicit-commit durability must match the scenario.");
        if (request.SourceCommit.Length != 40 || !request.SourceCommit.All(Uri.IsHexDigit) || !Path.IsPathFullyQualified(request.IndexPath))
            throw new InvalidDataException("An exact source SHA and an absolute, new index directory are required.");
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static void WriteResult(string path, WorkloadResult result)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public static void RequireLogicalIds(IEnumerable<string> actual, IEnumerable<string> expected)
    {
        if (!actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("The workload returned incorrect, duplicate or missing logical IDs.");
    }

    // This establishes operation parity only, never measurement qualification or a speed ratio.
    public static bool HasMatchedCompletedWork(WorkloadResult left, WorkloadResult right)
    {
        var a = left.Request;
        var b = right.Request;
        return left.SchemaVersion == 1 && right.SchemaVersion == 1 &&
            left.Outcome == "Completed" && right.Outcome == "Completed" &&
            left.Classification == "Unqualified" && right.Classification == "Unqualified" &&
            a.Engine != b.Engine &&
            a.SourceCommit == b.SourceCommit && a.Scenario == b.Scenario && a.Dataset == b.Dataset && a.DatasetVersion == b.DatasetVersion &&
            a.Seed == b.Seed && a.DocumentCount == b.DocumentCount && a.SearchOperations == b.SearchOperations &&
            a.Concurrency == b.Concurrency && a.QueueCapacity == b.QueueCapacity && a.RefreshEveryDocuments == b.RefreshEveryDocuments &&
            a.Durability == b.Durability && a.MemoryBudgetMiB == b.MemoryBudgetMiB && a.DurationMilliseconds == b.DurationMilliseconds &&
            left.PreloadedDocuments == ExpectedPreload(a) && right.PreloadedDocuments == ExpectedPreload(b) &&
            left.AdmittedDocuments == a.DocumentCount - left.PreloadedDocuments &&
            right.AdmittedDocuments == b.DocumentCount - right.PreloadedDocuments &&
            left.CommittedDocuments == left.AdmittedDocuments && right.CommittedDocuments == right.AdmittedDocuments &&
            left.VisibleDocuments == a.DocumentCount && right.VisibleDocuments == b.DocumentCount &&
            left.CompletedSearches == a.SearchOperations && right.CompletedSearches == b.SearchOperations &&
            left.DatasetSha256 is { Length: 64 } && left.DatasetSha256 == right.DatasetSha256 &&
            left.CommittedDocuments == right.CommittedDocuments && left.PreloadedDocuments == right.PreloadedDocuments &&
            left.CompletedSearches == right.CompletedSearches && left.FailedOperations == 0 && right.FailedOperations == 0 &&
            left.CancelledOperations == 0 && right.CancelledOperations == 0 &&
            left.VerifiedLogicalIds.Length == a.DocumentCount && right.VerifiedLogicalIds.Length == b.DocumentCount &&
            left.VerifiedLogicalIds.Distinct(StringComparer.Ordinal).Count() == a.DocumentCount &&
            right.VerifiedLogicalIds.Distinct(StringComparer.Ordinal).Count() == b.DocumentCount &&
            left.CommittedLogicalIds.Length == left.CommittedDocuments + left.PreloadedDocuments &&
            right.CommittedLogicalIds.Length == right.CommittedDocuments + right.PreloadedDocuments &&
            left.CompletedSearchLogicalIds.Length == a.SearchOperations &&
            right.CompletedSearchLogicalIds.Length == b.SearchOperations &&
            left.VerifiedLogicalIds.SequenceEqual(right.VerifiedLogicalIds, StringComparer.Ordinal) &&
            left.CommittedLogicalIds.SequenceEqual(right.CommittedLogicalIds, StringComparer.Ordinal) &&
            left.CompletedSearchLogicalIds.SequenceEqual(right.CompletedSearchLogicalIds, StringComparer.Ordinal);
    }

    public static int ExpectedPreload(WorkloadRequest request) => request.Scenario switch
    {
        "search-readonly" => request.DocumentCount,
        "ingest-search-refresh" => Math.Max(1, Math.Min(16, request.DocumentCount / 4)),
        _ => 0
    };
}
