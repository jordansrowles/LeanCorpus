using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Ranking;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures ranking metric calculation and bounded maximum marginal relevance selection.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class RankingEvaluationBenchmarks
{
    [Params(25, 100, 500)]
    public int CandidateCount { get; set; }

    [Params(10, 25)]
    public int TopN { get; set; }

    private string[] _ranking = [];
    private RelevanceJudgement[] _judgements = [];
    private ScoreDoc[] _candidates = [];
    private float[,] _similarities = new float[0, 0];

    [GlobalSetup]
    public void Setup()
    {
        _ranking = Enumerable.Range(0, CandidateCount).Select(static i => $"document-{i}").ToArray();
        _judgements = Enumerable.Range(0, CandidateCount * 2)
            .Select(static i => new RelevanceJudgement("query", $"document-{i}", i % 5))
            .ToArray();
        _candidates = Enumerable.Range(0, CandidateCount)
            .Select(i => new ScoreDoc(i, CandidateCount - i))
            .ToArray();
        _similarities = new float[CandidateCount, CandidateCount];
        for (int left = 0; left < CandidateCount; left++)
            for (int right = 0; right < CandidateCount; right++)
                _similarities[left, right] = 1f / (1f + Math.Abs(left - right));
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public double CalculateMetrics()
    {
        var result = RankingMetrics.Calculate(_ranking, _judgements, Math.Min(TopN, CandidateCount));
        return result.Ndcg + result.AveragePrecision;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SelectMmr()
        => MaximumMarginalRelevance.Select(_candidates, Math.Min(TopN, CandidateCount), 0.7f,
            (left, right) => _similarities[left, right]).Count;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SelectMmr_WithMissingSimilarities()
        => MaximumMarginalRelevance.Select(_candidates, Math.Min(TopN, CandidateCount), 0.7f,
            (left, right) => ((left + right) & 15) == 0 ? null : _similarities[left, right],
            MissingSimilarityPolicy.ExcludeCandidate).Count;
}
