using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Search;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares the production postings read-session path with the former
/// per-primitive OperationDrain path in representative resident queries.
/// </summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[OperationsPerSecond]
[Config(typeof(OperationDrainBenchmarkConfig))]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[KeepBenchmarkFiles]
public class OperationDrainQueryBenchmarks
{
    private const int TopN = 100;

    public static IEnumerable<int> DocCounts =>
        BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("CommonTerm", "Conjunction4Terms", "PhraseWithPositions")]
    public string Workload { get; set; } = "CommonTerm";

    private IndexSearcher? _searcher;
    private Query? _query;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _searcher = SharedStandardIndex.LeanSearcher;
        _query = BuildQuery(Workload);

        var session = RunValidation(PostingsReadBenchmarkMode.ReadSession);
        var primitive = RunValidation(PostingsReadBenchmarkMode.PerPrimitive);
        AssertEquivalent(session.Results, primitive.Results);

        if (session.Metrics.DecodedBlockCount < 2 || primitive.Metrics.DecodedBlockCount < 2)
        {
            throw new InvalidOperationException(
                $"The {Workload} workload decoded fewer than two postings blocks.");
        }

        Console.WriteLine(
            $"OperationDrain validation: workload={Workload}; " +
            $"read-session enters/query={session.Metrics.OperationDrainEnterCount}; " +
            $"per-primitive enters/query={primitive.Metrics.OperationDrainEnterCount}; " +
            $"decoded-blocks/query={session.Metrics.DecodedBlockCount}; " +
            $"hits={session.Results.TotalHits}.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        PostingsReadBenchmarkControl.SetMode(PostingsReadBenchmarkMode.ReadSession);
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ReadSession()
    {
        PostingsReadBenchmarkControl.SetMode(PostingsReadBenchmarkMode.ReadSession);
        return _searcher!.Search(_query!, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int PerPrimitiveReads()
    {
        PostingsReadBenchmarkControl.SetMode(PostingsReadBenchmarkMode.PerPrimitive);
        return _searcher!.Search(_query!, TopN).TotalHits;
    }

    private (TopDocs Results, PostingsReadBenchmarkMetrics Metrics) RunValidation(
        PostingsReadBenchmarkMode mode)
    {
        PostingsReadBenchmarkControl.SetMode(mode);
        PostingsReadBenchmarkControl.StartRecording();
        try
        {
            var results = _searcher!.Search(_query!, TopN);
            return (results, PostingsReadBenchmarkControl.StopRecording());
        }
        catch
        {
            PostingsReadBenchmarkControl.StopRecording();
            throw;
        }
    }

    private static void AssertEquivalent(TopDocs expected, TopDocs actual)
    {
        if (expected.TotalHits != actual.TotalHits ||
            expected.IsPartial != actual.IsPartial ||
            expected.ScoreDocs.Length != actual.ScoreDocs.Length)
        {
            throw new InvalidOperationException(
                "Read-session and per-primitive searches returned different result shapes.");
        }

        for (int i = 0; i < expected.ScoreDocs.Length; i++)
        {
            if (expected.ScoreDocs[i].DocId != actual.ScoreDocs[i].DocId ||
                expected.ScoreDocs[i].Score != actual.ScoreDocs[i].Score)
            {
                throw new InvalidOperationException(
                    $"Read-session and per-primitive searches differ at result {i}.");
            }
        }
    }

    private static Query BuildQuery(string workload)
    {
        return workload switch
        {
            "CommonTerm" => new TermQuery("body", "said"),
            "Conjunction4Terms" => BuildConjunction(),
            "PhraseWithPositions" => new PhraseQuery("body", "president", "company"),
            _ => throw new InvalidOperationException(
                $"Unknown OperationDrain workload '{workload}'.")
        };
    }

    private static Query BuildConjunction()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "president"), Occur.Must);
        builder.Add(new TermQuery("body", "company"), Occur.Must);
        builder.Add(new TermQuery("body", "reported"), Occur.Must);
        builder.Add(new TermQuery("body", "financial"), Occur.Must);
        return builder.Build();
    }
}

public sealed class OperationDrainBenchmarkConfig : ManualConfig
{
    public OperationDrainBenchmarkConfig() => AddColumn(StatisticColumn.P95);
}
