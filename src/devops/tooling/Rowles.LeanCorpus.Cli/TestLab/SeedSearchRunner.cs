using Rowles.LeanCorpus.Cli.Creation;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli.TestLab;

internal sealed record SeedSearchMatch(ulong Seed, string Observation);
internal sealed record SeedSearchResult(string Predicate, string Preset, ulong FirstSeed, ulong LastSeed,
    int DocumentCount, IReadOnlyList<SeedSearchMatch> Matches, int SeedsEvaluated, long MedianSizeBytes);

internal static class SeedSearchRunner
{
    private const int MaximumSeedCount = 1_000;

    internal static SeedSearchResult Search(ulong firstSeed, ulong lastSeed, string predicate, string preset,
        string query, string field, int documentCount, double threshold)
    {
        if (lastSeed < firstSeed || lastSeed - firstSeed >= MaximumSeedCount)
            throw new ArgumentOutOfRangeException(nameof(lastSeed), $"Seed ranges must be ascending and contain at most {MaximumSeedCount} seeds.");
        if (documentCount is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(documentCount), "Document count must be between 1 and 10,000.");
        predicate = predicate.Trim().ToLowerInvariant();
        if (predicate is not ("zero-results" or "segment-count" or "validation-failure" or "size-outlier"))
            throw new ArgumentException("Predicate must be zero-results, segment-count, validation-failure or size-outlier.", nameof(predicate));
        if (!double.IsFinite(threshold) || threshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be a positive finite number.");
        if (preset is "codec-current" or "codec-migration-required")
            throw new ArgumentException("Seed search requires a generative DataForge preset.", nameof(preset));

        string root = Path.Combine(Path.GetTempPath(), $"leancorpus-seed-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var observations = new List<(ulong Seed, string Observation, long SizeBytes)>();
        int evaluated = 0;
        try
        {
            for (ulong seed = firstSeed; ; seed++)
            {
                string indexPath = Path.Combine(root, "index");
                IndexCreationResult created = IndexCreator.CreatePreset(preset, indexPath, seed, documentCount, 32, "Clustered", "en");
                evaluated++;
                long sizeBytes;
                try
                {
                    using var inspector = new IndexInspector(created.IndexPath);
                    sizeBytes = inspector.GetSizeReport().TotalSizeBytes;
                    switch (predicate)
                    {
                        case "zero-results":
                        {
                            using var directory = new MMapDirectory(created.IndexPath);
                            using var searcher = new IndexSearcher(directory);
                            int hitCount = searcher.Search(query, field, 1).TotalHits;
                            if (hitCount == 0)
                                observations.Add((seed, $"query '{query}' returned no results", sizeBytes));
                            break;
                        }
                        case "segment-count":
                            if (inspector.Overview.SegmentCount >= threshold)
                                observations.Add((seed, $"segment count {inspector.Overview.SegmentCount} met threshold {threshold:G}", sizeBytes));
                            break;
                        case "validation-failure":
                        {
                            using var output = new StringWriter();
                            using var error = new StringWriter();
                            int exitCode = IndexCheckerCli.Run(["check", created.IndexPath], output, error);
                            if (exitCode != CliExitCodes.Success)
                                observations.Add((seed, $"check exited {exitCode}: {output.ToString().Trim()}", sizeBytes));
                            break;
                        }
                        case "size-outlier":
                            observations.Add((seed, string.Empty, sizeBytes));
                            break;
                    }
                }
                finally
                {
                    if (Directory.Exists(indexPath))
                        Directory.Delete(indexPath, recursive: true);
                }
                if (seed == lastSeed)
                    break;
            }

            long medianSize = Median(observations.Count > 0 && predicate == "size-outlier"
                ? observations.Select(static item => item.SizeBytes)
                : Enumerable.Empty<long>());
            IReadOnlyList<SeedSearchMatch> matches;
            if (predicate == "size-outlier")
            {
                double ratio = threshold;
                matches = observations
                    .Where(item => medianSize > 0 && (item.SizeBytes >= medianSize * ratio || item.SizeBytes <= medianSize / ratio))
                    .Select(item => new SeedSearchMatch(item.Seed, $"size {item.SizeBytes} bytes versus median {medianSize} bytes"))
                    .ToArray();
            }
            else
                matches = observations.Select(static item => new SeedSearchMatch(item.Seed, item.Observation)).ToArray();
            return new SeedSearchResult(predicate, preset, firstSeed, lastSeed, documentCount,
                Array.AsReadOnly(matches.ToArray()), evaluated, medianSize);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static long Median(IEnumerable<long> values)
    {
        long[] sorted = values.Order().ToArray();
        if (sorted.Length == 0)
            return 0;
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? sorted[middle - 1] + (sorted[middle] - sorted[middle - 1]) / 2
            : sorted[middle];
    }
}
