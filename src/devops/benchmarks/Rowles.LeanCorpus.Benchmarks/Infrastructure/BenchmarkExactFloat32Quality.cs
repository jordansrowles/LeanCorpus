using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record VectorExactFloat32ContractResult(
    string QueryId,
    int TopK,
    int ReturnedCount,
    int RequiredHigherRankCount,
    int ReturnedBoundaryTieCount,
    double? MaximumAbsoluteScoreError,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0;
}

internal static class BenchmarkExactFloat32Quality
{
    internal const double ScoreTolerance = 1e-5;
    internal const double BoundaryTieTolerance = 1e-6;

    internal static VectorExactFloat32ContractResult Evaluate(
        IReadOnlyList<VectorRecord> records,
        VectorQueryCase query,
        IReadOnlyList<ScoreDoc> returned,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(returned);
        if (topK < 1 || topK > records.Count)
            throw new ArgumentOutOfRangeException(nameof(topK));

        var truth = VectorGroundTruth.Compute(records, query, records.Count);
        var scores = truth.NeighbourOrdinals.Zip(truth.CosineValues)
            .ToDictionary(pair => pair.First, pair => pair.Second);
        double boundary = truth.CosineValues[topK - 1];
        long[] required = truth.NeighbourOrdinals
            .Where(id => scores[id] > boundary + BoundaryTieTolerance).ToArray();
        var failures = new List<string>();
        var ordinals = new HashSet<long>();
        int boundaryTies = 0;
        double? maximumError = null;
        if (returned.Count != topK)
            failures.Add($"Expected {topK} exact results; got {returned.Count}.");
        for (int index = 0; index < returned.Count; index++)
        {
            ScoreDoc hit = returned[index];
            if ((uint)hit.DocId >= (uint)records.Count)
            {
                failures.Add($"Document {hit.DocId} is outside the source corpus.");
                continue;
            }
            long ordinal = records[hit.DocId].Ordinal;
            if (!ordinals.Add(ordinal))
                failures.Add($"Duplicate document ordinal {ordinal}.");
            double score = scores[ordinal];
            if (score < boundary - BoundaryTieTolerance)
                failures.Add($"Document ordinal {ordinal} is below the exact boundary tie group.");
            if (Math.Abs(score - boundary) <= BoundaryTieTolerance)
                boundaryTies++;
            double error = Math.Abs(hit.Score - score);
            if (!double.IsFinite(error))
                failures.Add($"Document ordinal {ordinal} has a non-finite score.");
            else
            {
                maximumError = Math.Max(maximumError ?? 0, error);
                if (error > ScoreTolerance)
                    failures.Add($"Document ordinal {ordinal} score error {error:R} exceeds {ScoreTolerance:R}.");
            }
            if (index > 0 && hit.Score > returned[index - 1].Score)
                failures.Add("Exact results are not ordered by descending score.");
        }
        foreach (long ordinal in required)
            if (!ordinals.Contains(ordinal))
                failures.Add($"Required higher-ranked document ordinal {ordinal} is missing.");

        return new VectorExactFloat32ContractResult(query.Id, topK, returned.Count, required.Length,
            boundaryTies, maximumError, failures.ToArray());
    }

    internal static void Require(IEnumerable<VectorExactFloat32ContractResult> results)
    {
        string[] failures = results.SelectMany(result => result.FailedChecks.Select(failure =>
            $"Exact Float32 query {result.QueryId} K={result.TopK}: {failure}")).ToArray();
        if (failures.Length != 0)
            throw new InvalidOperationException(string.Join("; ", failures));
    }
}
