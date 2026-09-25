using System.Collections.Concurrent;
using System.Globalization;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class TextBenchmarkData
{
    public const int DefaultDocCount = 20_000;

    private static readonly ConcurrentDictionary<int, Lazy<string[]>> Documents = new();

    public static IEnumerable<int> GetDocCounts(int defaultCount)
    {
        var configured = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return [count];
        return [defaultCount];
    }

    public static string[] BuildDocuments(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Documents.GetOrAdd(count, static requestedCount =>
            new Lazy<string[]>(() => new LeanCorpusSearchProfile()
                    .Generate(new DataForgeGenerationOptions(42, requestedCount))
                    .Select(static record => record.Body).ToArray(),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static string[] BuildMultilingualInputs(string language, int count)
    {
        var profile = new RowlesTextMultilingualProfile();
        var options = new DataForgeGenerationOptions(42, count,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["language"] = language });
        return profile.Generate(options).Select(static record => record.Text).ToArray();
    }
}
