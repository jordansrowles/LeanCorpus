using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.Cli.TestLab;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli.Creation;

internal sealed record WorkloadQuery(int Ordinal, string Query, string Field, int Take);
internal sealed record WorkloadExpectedResult(int Ordinal, IReadOnlyList<int> DocumentIds);
internal sealed record WorkloadRecipe(int SchemaVersion, string Preset, ulong Seed, int DocumentCount,
    int QueryCount, string IndexRecipeIdentity, string WorkloadIdentity);
internal sealed record WorkloadCreationResult(string DirectoryPath, int DocumentCount, int QueryCount,
    string IndexRecipeIdentity, string WorkloadIdentity);
internal sealed record WorkloadQueryDifference(int Ordinal, string Query, IReadOnlyList<int> ExpectedDocumentIds,
    IReadOnlyList<int> ActualDocumentIds, string? Error);
internal sealed record WorkloadReplayResult(int QueryCount, int CompletedCount, int FailureCount,
    int ResultDifferenceCount, double TotalMilliseconds, double MeanMilliseconds, double P95Milliseconds,
    IReadOnlyList<WorkloadQueryDifference> Differences);
internal sealed record SearchRankDelta(int DocumentId, int FirstRank, int SecondRank, float FirstScore, float SecondScore);
internal sealed record SearchComparisonResult(string FirstQuery, string SecondQuery, int FirstHitCount,
    int SecondHitCount, int TopNOverlap, double TopNOverlapRatio, double FirstMilliseconds,
    double SecondMilliseconds, IReadOnlyList<SearchRankDelta> RankChanges);

internal static class WorkloadCreator
{
    private const int SchemaVersion = 1;
    private static readonly string[] QueryPool = [
        "said", "government", "market", "\"new york\"", "government AND public", "govern*",
        "quasarneedle", "zzzznomatch"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions JsonLinesOptions = new(JsonOptions) { WriteIndented = false };

    internal static WorkloadCreationResult Create(string outputPath, string preset, ulong seed,
        int documentCount, int queryCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(preset);
        if (documentCount is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(documentCount), "Document count must be between 1 and 1,000,000.");
        if (queryCount is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(queryCount), "Query count must be between 1 and 100,000.");

        string fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException($"Workload output '{fullPath}' already exists.");
        Directory.CreateDirectory(fullPath);
        try
        {
            string indexPath = Path.Combine(fullPath, "index");
            IndexCreationResult index = IndexCreator.CreatePreset(preset, indexPath, seed, documentCount,
                32, "Clustered", "en");
            CopyDirectory(Path.Combine(indexPath, ".leancorpus", "dataforge"), Path.Combine(fullPath, "dataset"));
            WorkloadQuery[] queries = CreateQueries(seed, queryCount);
            WorkloadExpectedResult[] expected = CaptureExpected(indexPath, queries);
            File.WriteAllLines(Path.Combine(fullPath, "queries.jsonl"), queries.Select(query => JsonSerializer.Serialize(query, JsonLinesOptions)));
            File.WriteAllText(Path.Combine(fullPath, "expected.json"), JsonSerializer.Serialize(expected, JsonOptions) + Environment.NewLine);

            string contentIdentity = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { preset, seed, documentCount, queries }, JsonOptions)));
            var recipe = new WorkloadRecipe(SchemaVersion, preset, seed, documentCount, queryCount,
                index.RecipeIdentity, contentIdentity);
            File.WriteAllText(Path.Combine(fullPath, "recipe.json"), JsonSerializer.Serialize(recipe, JsonOptions) + Environment.NewLine);
            return new WorkloadCreationResult(fullPath, documentCount, queryCount, index.RecipeIdentity, contentIdentity);
        }
        catch
        {
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            throw;
        }
    }

    internal static WorkloadReplayResult Replay(string workloadPath)
    {
        string fullPath = Path.GetFullPath(workloadPath);
        string indexPath = Path.Combine(fullPath, "index");
        string queryPath = Path.Combine(fullPath, "queries.jsonl");
        string expectedPath = Path.Combine(fullPath, "expected.json");
        if (!Directory.Exists(indexPath) || !File.Exists(queryPath) || !File.Exists(expectedPath))
            throw new DirectoryNotFoundException("The workload must contain index/, queries.jsonl and expected.json.");
        WorkloadQuery[] queries = File.ReadLines(queryPath)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<WorkloadQuery>(line, JsonOptions)
                ?? throw new InvalidDataException("A workload query line is empty or invalid."))
            .ToArray();
        WorkloadExpectedResult[] expected = JsonSerializer.Deserialize<WorkloadExpectedResult[]>(File.ReadAllText(expectedPath), JsonOptions)
            ?? throw new InvalidDataException("The expected workload results are empty or invalid.");
        var expectedByOrdinal = expected.ToDictionary(static item => item.Ordinal);
        var differences = new List<WorkloadQueryDifference>();
        var durations = new List<double>(queries.Length);
        int completed = 0;
        int failures = 0;
        using var directory = new MMapDirectory(indexPath);
        using var searcher = new IndexSearcher(directory);
        foreach (WorkloadQuery query in queries)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                TopDocs result = searcher.Search(query.Query, query.Field, query.Take,
                    new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser());
                durations.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                completed++;
                int[] actualIds = result.ScoreDocs.Select(static hit => hit.DocId).ToArray();
                int[] expectedIds = expectedByOrdinal.TryGetValue(query.Ordinal, out WorkloadExpectedResult? expectedResult)
                    ? expectedResult.DocumentIds.ToArray()
                    : [];
                if (!actualIds.SequenceEqual(expectedIds))
                    differences.Add(new WorkloadQueryDifference(query.Ordinal, query.Query, expectedIds, actualIds, null));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                durations.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                failures++;
                int[] expectedIds = expectedByOrdinal.TryGetValue(query.Ordinal, out WorkloadExpectedResult? expectedResult)
                    ? expectedResult.DocumentIds.ToArray()
                    : [];
                differences.Add(new WorkloadQueryDifference(query.Ordinal, query.Query, expectedIds, [], exception.Message));
            }
        }

        durations.Sort();
        double total = durations.Sum();
        double p95 = durations.Count == 0 ? 0 : durations[(int)Math.Ceiling(durations.Count * 0.95) - 1];
        return new WorkloadReplayResult(queries.Length, completed, failures, differences.Count,
            total, durations.Count == 0 ? 0 : total / durations.Count, p95, Array.AsReadOnly(differences.ToArray()));
    }

    private static WorkloadQuery[] CreateQueries(ulong seed, int count)
    {
        ulong state = seed ^ 0x9e3779b97f4a7c15UL;
        var queries = new WorkloadQuery[count];
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            string query = QueryPool[(int)(state % (ulong)QueryPool.Length)];
            queries[ordinal] = new WorkloadQuery(ordinal, query, "body", 10);
        }
        return queries;
    }

    private static WorkloadExpectedResult[] CaptureExpected(string indexPath, IReadOnlyList<WorkloadQuery> queries)
    {
        using var directory = new MMapDirectory(indexPath);
        using var searcher = new IndexSearcher(directory);
        return queries.Select(query =>
        {
            TopDocs result = searcher.Search(query.Query, query.Field, query.Take,
                new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser());
            return new WorkloadExpectedResult(query.Ordinal, Array.AsReadOnly(result.ScoreDocs.Select(static hit => hit.DocId).ToArray()));
        }).ToArray();
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"DataForge dataset '{sourcePath}' was not materialised.");
        Directory.CreateDirectory(destinationPath);
        foreach (string directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, directory)));
        foreach (string file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string destinationFile = Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: false);
        }
    }
}
