using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Cli.Creation;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli;

internal static partial class WorkbenchCommands
{
    private static void AddWorkloadCreationCommand(Command createCommand)
    {
        var path = new Argument<string>("workload-directory") { Description = "New directory for dataset, index, queries and expected results." };
        var preset = new Option<string>("--preset") { Description = "DataForge index preset (default search-small).", DefaultValueFactory = _ => "search-small" };
        var seed = new Option<ulong>("--seed") { Description = "Deterministic seed (default 42).", DefaultValueFactory = _ => 42UL };
        var count = new Option<int>("--count") { Description = "Generated document count (default 128).", DefaultValueFactory = _ => 128 };
        var queryCount = new Option<int>("--queries") { Description = "Generated query count (default 100).", DefaultValueFactory = _ => 100 };
        var json = Flag("--json", "Write the workload creation result as JSON.");
        var command = new Command("workload", "Create a deterministic dataset, index, query stream and expected results.");
        command.Add(path);
        command.Add(preset);
        command.Add(seed);
        command.Add(count);
        command.Add(queryCount);
        command.Add(json);
        command.SetAction(result => RunCreateWorkload(GetRequiredValue(result, path), result.GetValue(preset) ?? "search-small",
            result.GetValue(seed), result.GetValue(count), result.GetValue(queryCount), result.GetValue(json),
            result.InvocationConfiguration.Output));
        createCommand.Add(command);
    }

    private static void AddSearchResearchCommands(Command searchCommand)
    {
        var workloadPath = new Argument<string>("workload-directory") { Description = "Workload containing index/, queries.jsonl and expected.json." };
        var replayJson = Flag("--json", "Write replay results as JSON.");
        var replay = new Command("replay", "Replay deterministic workload queries and compare expected results.");
        replay.Add(workloadPath);
        replay.Add(replayJson);
        replay.SetAction(result => RunWorkloadReplay(GetRequiredValue(result, workloadPath), result.GetValue(replayJson),
            result.InvocationConfiguration.Output));
        searchCommand.Add(replay);

        var indexPath = new Argument<string>("index-path");
        var firstQuery = new Argument<string>("first-query");
        var secondQuery = new Argument<string>("second-query");
        var field = new Option<string>("--field") { Description = "Default query field (default body).", DefaultValueFactory = _ => "body" };
        var take = new Option<int>("--take") { Description = "Maximum results per query (default 10).", DefaultValueFactory = _ => 10 };
        var json = Flag("--json", "Write comparison results as JSON.");
        var compare = new Command("compare", "Compare two query strings against the same committed index.");
        compare.Add(indexPath);
        compare.Add(firstQuery);
        compare.Add(secondQuery);
        compare.Add(field);
        compare.Add(take);
        compare.Add(json);
        compare.SetAction(result => RunSearchComparison(GetRequiredValue(result, indexPath),
            GetRequiredValue(result, firstQuery), GetRequiredValue(result, secondQuery), result.GetValue(field) ?? "body",
            result.GetValue(take), result.GetValue(json), result.InvocationConfiguration.Output));
        searchCommand.Add(compare);
    }

    private static int RunCreateWorkload(string path, string preset, ulong seed, int count, int queryCount,
        bool json, TextWriter output)
    {
        WorkloadCreationResult result = WorkloadCreator.Create(path, preset, seed, count, queryCount);
        if (json)
            WriteJson(output, result);
        else
            output.WriteLine($"Created workload at {result.DirectoryPath}: {result.DocumentCount} documents, {result.QueryCount} queries, identity {result.WorkloadIdentity}.");
        return CliExitCodes.Success;
    }

    private static int RunWorkloadReplay(string path, bool json, TextWriter output)
    {
        WorkloadReplayResult result = WorkloadCreator.Replay(path);
        if (json)
            WriteJson(output, result);
        else
        {
            output.WriteLine($"Queries: {result.CompletedCount}/{result.QueryCount} completed; failures: {result.FailureCount}; result differences: {result.ResultDifferenceCount}");
            output.WriteLine($"Latency: total {result.TotalMilliseconds:F2} ms; mean {result.MeanMilliseconds:F2} ms; p95 {result.P95Milliseconds:F2} ms");
            foreach (var difference in result.Differences)
                output.WriteLine($"Query {difference.Ordinal} '{difference.Query}': expected [{string.Join(",", difference.ExpectedDocumentIds)}], actual [{string.Join(",", difference.ActualDocumentIds)}]{(difference.Error is null ? "" : $"; {difference.Error}")}");
        }
        return result.FailureCount == 0 && result.ResultDifferenceCount == 0
            ? CliExitCodes.Success
            : CliExitCodes.ValidationErrors;
    }

    private static int RunSearchComparison(string indexPath, string firstQuery, string secondQuery,
        string field, int take, bool json, TextWriter output)
    {
        if (take is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(take), "Result count must be between 1 and 10,000.");
        using var directory = new MMapDirectory(indexPath);
        using var searcher = new IndexSearcher(directory);
        var analyser = new StandardAnalyser();
        long firstStarted = Stopwatch.GetTimestamp();
        TopDocs first = searcher.Search(firstQuery, field, take, analyser);
        double firstMilliseconds = Stopwatch.GetElapsedTime(firstStarted).TotalMilliseconds;
        long secondStarted = Stopwatch.GetTimestamp();
        TopDocs second = searcher.Search(secondQuery, field, take, analyser);
        double secondMilliseconds = Stopwatch.GetElapsedTime(secondStarted).TotalMilliseconds;
        int[] firstIds = first.ScoreDocs.Select(static hit => hit.DocId).ToArray();
        int[] secondIds = second.ScoreDocs.Select(static hit => hit.DocId).ToArray();
        var firstRank = first.ScoreDocs.Select((hit, rank) => (hit.DocId, Rank: rank + 1, hit.Score))
            .ToDictionary(static item => item.DocId);
        var secondRank = second.ScoreDocs.Select((hit, rank) => (hit.DocId, Rank: rank + 1, hit.Score))
            .ToDictionary(static item => item.DocId);
        SearchRankDelta[] changes = firstRank.Keys.Intersect(secondRank.Keys).Order()
            .Select(id => new SearchRankDelta(id, firstRank[id].Rank, secondRank[id].Rank, firstRank[id].Score, secondRank[id].Score))
            .ToArray();
        int overlap = changes.Length;
        int union = firstIds.Concat(secondIds).Distinct().Count();
        var result = new SearchComparisonResult(firstQuery, secondQuery, first.TotalHits, second.TotalHits,
            overlap, union == 0 ? 1 : (double)overlap / union, firstMilliseconds, secondMilliseconds,
            Array.AsReadOnly(changes));
        if (json)
            WriteJson(output, result);
        else
        {
            output.WriteLine($"Hits: {result.FirstHitCount} vs {result.SecondHitCount}; top-{take} overlap: {overlap}/{union} ({result.TopNOverlapRatio:P1})");
            output.WriteLine($"Latency: {firstMilliseconds:F2} ms vs {secondMilliseconds:F2} ms");
            foreach (SearchRankDelta change in changes)
                output.WriteLine($"Document {change.DocumentId}: rank {change.FirstRank} -> {change.SecondRank}; score {change.FirstScore:G7} -> {change.SecondScore:G7}");
        }
        return CliExitCodes.Success;
    }
}
