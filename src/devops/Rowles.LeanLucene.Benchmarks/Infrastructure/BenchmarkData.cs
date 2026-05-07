using System.Globalization;
using System.Text.Json;

namespace Rowles.LeanLucene.Benchmarks;

internal static class BenchmarkData
{
    /// <summary>Default document count used by all benchmark suites when <c>BENCH_DOC_COUNT</c> is not set.</summary>
    public const int DefaultDocCount = 100_000;

    /// <summary>
    /// Returns the document count to use for benchmarks. When the BENCH_DOC_COUNT
    /// environment variable is set (e.g. via --doccount in benchmark.ps1), that
    /// value is used; otherwise the per-suite default is returned.
    /// </summary>
    public static IEnumerable<int> GetDocCounts(int defaultCount)
    {
        var env = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            return [n];
        return [defaultCount];
    }

    public static BenchmarkDataSourceReport[] GetLoadedDataSources()
        => RealDataPool.GetLoadedDataSources();

    /// <summary>Returns real-world document bodies from the data pool, wrapping round-robin if needed.</summary>
    public static string[] BuildDocuments(int count)
        => RealDataPool.GetBodies(count);

    /// <summary>Builds documents with a numeric "price" field for index sort benchmarks.</summary>
    public static (string Body, double Price)[] BuildDocumentsWithPrices(int count)
    {
        var bodies = RealDataPool.GetBodies(count);
        var rng = new Random(42);
        var docs = new (string Body, double Price)[count];
        for (int i = 0; i < count; i++)
            docs[i] = (bodies[i], Math.Round(rng.NextDouble() * 999 + 1, 2));
        return docs;
    }

    /// <summary>Builds parent-child document blocks for block join benchmarks.</summary>
    public static (string ParentTitle, string[] ChildBodies)[] BuildParentChildBlocks(int blockCount, int childrenPerBlock = 3)
    {
        var bodies = RealDataPool.GetBodies(blockCount * (childrenPerBlock + 1));
        var blocks = new (string ParentTitle, string[] ChildBodies)[blockCount];
        var idx = 0;
        for (int i = 0; i < blockCount; i++)
        {
            var parentBody = bodies[idx++];
            var dot = parentBody.IndexOf('.', StringComparison.Ordinal);
            var title = dot > 0 && dot <= 80
                ? parentBody[..dot]
                : parentBody[..Math.Min(80, parentBody.Length)];

            var children = new string[childrenPerBlock];
            for (int c = 0; c < childrenPerBlock; c++)
                children[c] = bodies[idx++];

            blocks[i] = (title, children);
        }
        return blocks;
    }

    /// <summary>Builds misspelled term variants for suggester benchmarks against real-corpus vocabulary.</summary>
    public static (string Original, string Misspelled)[] BuildMisspelledTerms()
    {
        return
        [
            ("government", "goverment"),
            ("president", "presiden"),
            ("market", "markts"),
            ("company", "compny"),
            ("million", "milion"),
            ("financial", "finanical"),
            ("reported", "reportd"),
            ("political", "politcal"),
            ("economic", "econmic"),
            ("hospital", "hosptal"),
            ("computer", "computr"),
            ("network", "netork"),
            ("message", "mesage"),
            ("article", "artcle"),
            ("because", "becuase"),
            ("people", "pepole"),
            ("national", "nationl"),
            ("through", "throgh"),
            ("without", "withut"),
            ("believe", "beleive"),
        ];
    }

    /// <summary>Builds JSON document strings for JSON mapping benchmarks.</summary>
    public static string[] BuildJsonDocuments(int count)
    {
        var bodies = RealDataPool.GetBodies(count);
        var rng = new Random(42);
        var docs = new string[count];
        for (int i = 0; i < count; i++)
        {
            var body = JsonSerializer.Serialize(bodies[i]);
            var price = Math.Round(rng.NextDouble() * 999 + 1, 2)
                            .ToString("F2", CultureInfo.InvariantCulture);
            docs[i] = $$$"""{"id":{{{i}}},"body":{{{body}}},"price":{{{price}}},"active":true}""";
        }
        return docs;
    }
}
