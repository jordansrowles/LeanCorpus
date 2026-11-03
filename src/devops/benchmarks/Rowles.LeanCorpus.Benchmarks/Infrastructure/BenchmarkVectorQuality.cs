using System.Text.Json;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record VectorQualityMeasurement(
    int TopK,
    int ReturnedCount,
    int RecallHits,
    double RecallAtK,
    double? MeanAbsoluteScoreError,
    double? MaxAbsoluteScoreError,
    double? MeanAbsoluteRankError,
    int? MaxAbsoluteRankError,
    double? MeanAbsoluteQuantisationScoreError,
    double? MaxAbsoluteQuantisationScoreError);

internal sealed record VectorQueryQualityResult(
    string QueryId,
    VectorQualityMeasurement[] Measurements);

internal sealed record VectorHeldOutQualitySummary(
    int QueryCount,
    double MeanRecallAt1,
    double MeanRecallAt10,
    double MeanRecallAt100,
    double MinimumRecallAt10,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0;
}

internal static class BenchmarkVectorQuality
{
    private const string FixtureRelativePath =
        ".config/devops/benchmark-correctness-fixtures/bval-09-vector-recall-ground-truth.json";
    private static readonly Lazy<double> MinimumRecall = new(LoadAndValidateFixture,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static double MinimumAcceptedRecall
    {
        get
        {
            ValidateFixture();
            return MinimumRecall.Value;
        }
    }

    public static void ValidateFixture() => _ = MinimumRecall.Value;

    public static VectorQualityMeasurement[] Measure(
        BenchmarkVectorDataset dataset,
        Func<int, ScoreDoc[]> searchAtK,
        bool quantised)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Measure(dataset, dataset.Query, searchAtK, quantised);
    }

    public static VectorQualityMeasurement[] Measure(
        BenchmarkVectorDataset dataset,
        VectorQueryCase query,
        Func<int, ScoreDoc[]> searchAtK,
        bool quantised)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(searchAtK);
        ValidateFixture();

        var exact = VectorGroundTruth.Compute(dataset.Records, query, dataset.Records.Length);
        var exactRankByOrdinal = new Dictionary<long, int>(exact.NeighbourOrdinals.Length);
        var exactScoreByOrdinal = new Dictionary<long, double>(exact.NeighbourOrdinals.Length);
        for (var i = 0; i < exact.NeighbourOrdinals.Length; i++)
        {
            exactRankByOrdinal.Add(exact.NeighbourOrdinals[i], i);
            exactScoreByOrdinal.Add(exact.NeighbourOrdinals[i], exact.CosineValues[i]);
        }

        var measurements = new List<VectorQualityMeasurement>(3);
        foreach (int topK in new[] { 1, 10, 100 }.Where(value => value <= dataset.Records.Length))
        {
            var expectedIds = exact.NeighbourOrdinals.Take(topK).ToHashSet();
            ScoreDoc[] returned = searchAtK(topK);
            if (returned.Length > topK)
                throw new InvalidOperationException(
                    $"Vector quality at K={topK} returned more than {topK} results: {returned.Length}.");

            var returnedOrdinals = new long[returned.Length];
            var scoreErrors = new double[returned.Length];
            var rankErrors = new int[returned.Length];
            for (var i = 0; i < returned.Length; i++)
            {
                int docId = returned[i].DocId;
                if ((uint)docId >= (uint)dataset.Records.Length)
                    throw new InvalidOperationException($"Vector result document ID {docId} is outside the source dataset.");

                long ordinal = dataset.Records[docId].Ordinal;
                if (!exactRankByOrdinal.TryGetValue(ordinal, out int exactRank)
                    || !exactScoreByOrdinal.TryGetValue(ordinal, out double exactScore))
                {
                    throw new InvalidOperationException($"Vector result ordinal {ordinal} is absent from exact ground truth.");
                }

                returnedOrdinals[i] = ordinal;
                scoreErrors[i] = Math.Abs(returned[i].Score - exactScore);
                rankErrors[i] = Math.Abs(i - exactRank);
                if (!double.IsFinite(scoreErrors[i]))
                    throw new InvalidOperationException($"Vector score error is not finite for ordinal {ordinal}.");
            }

            if (returnedOrdinals.Distinct().Count() != returnedOrdinals.Length)
                throw new InvalidOperationException($"Vector quality at K={topK} returned duplicate documents.");

            int recallHits = returnedOrdinals.Count(expectedIds.Contains);
            measurements.Add(new VectorQualityMeasurement(
                topK,
                returned.Length,
                recallHits,
                (double)recallHits / topK,
                scoreErrors.Length == 0 ? null : scoreErrors.Average(),
                scoreErrors.Length == 0 ? null : scoreErrors.Max(),
                rankErrors.Length == 0 ? null : rankErrors.Average(static error => (double)error),
                rankErrors.Length == 0 ? null : rankErrors.Max(),
                quantised && scoreErrors.Length > 0 ? scoreErrors.Average() : null,
                quantised && scoreErrors.Length > 0 ? scoreErrors.Max() : null));
        }

        return measurements.ToArray();
    }

    public static void RequireHeldOutGroup(
        IEnumerable<VectorQueryQualityResult> queryResults,
        string context)
    {
        VectorHeldOutQualitySummary summary = EvaluateHeldOutGroup(queryResults, context);
        if (!summary.Passed)
            throw new InvalidOperationException(string.Join("; ", summary.FailedChecks) + ".");
    }

    public static VectorHeldOutQualitySummary EvaluateHeldOutGroup(
        IEnumerable<VectorQueryQualityResult> queryResults,
        string context)
    {
        ArgumentNullException.ThrowIfNull(queryResults);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        VectorQueryQualityResult[] results = queryResults.ToArray();
        if (results.Length != 4)
            throw new InvalidOperationException($"{context} requires exactly four held-out queries; got {results.Length}.");
        if (results.Any(static result => string.IsNullOrWhiteSpace(result.QueryId))
            || results.Select(static result => result.QueryId).Distinct(StringComparer.Ordinal).Count() != results.Length)
            throw new InvalidOperationException($"{context} requires four distinct, named held-out queries.");

        foreach (VectorQueryQualityResult result in results)
        {
            if (result.Measurements is null)
                throw new InvalidOperationException($"{context} query {result.QueryId} has no quality measurements.");
            int[] topKs = result.Measurements.Select(static measurement => measurement.TopK)
                .Order().ToArray();
            if (!topKs.SequenceEqual([1, 10, 100]))
                throw new InvalidOperationException(
                    $"{context} query {result.QueryId} must contain exactly recall@1, recall@10 and recall@100 measurements.");
            foreach (VectorQualityMeasurement measurement in result.Measurements)
            {
                if (!double.IsFinite(measurement.RecallAtK)
                    || measurement.RecallAtK < 0d || measurement.RecallAtK > 1d
                    || measurement.ReturnedCount != measurement.TopK
                    || measurement.RecallHits < 0 || measurement.RecallHits > measurement.TopK
                    || Math.Abs(measurement.RecallAtK - (double)measurement.RecallHits / measurement.TopK) > 1e-12)
                {
                    throw new InvalidOperationException(
                        $"{context} query {result.QueryId} has an invalid recall@{measurement.TopK} measurement.");
                }
            }
        }

        double meanRecallAt1 = MeanRecall(results, 1);
        double meanRecallAt10 = MeanRecall(results, 10);
        double meanRecallAt100 = MeanRecall(results, 100);
        double minimumRecallAt10 = results.Min(static result =>
            result.Measurements.Single(static measurement => measurement.TopK == 10).RecallAtK);
        var failedChecks = new List<string>();
        AddFailure(meanRecallAt1, 0.90d, $"{context} mean recall@1");
        AddFailure(meanRecallAt10, 0.90d, $"{context} mean recall@10");
        AddFailure(meanRecallAt100, 0.90d, $"{context} mean recall@100");
        foreach (VectorQueryQualityResult result in results)
        {
            double recallAt10 = result.Measurements.Single(static measurement => measurement.TopK == 10).RecallAtK;
            AddFailure(recallAt10, 0.80d, $"{context} query {result.QueryId} minimum recall@10");
        }

        return new VectorHeldOutQualitySummary(results.Length, meanRecallAt1, meanRecallAt10,
            meanRecallAt100, minimumRecallAt10, failedChecks.ToArray());

        void AddFailure(double recall, double floor, string check)
        {
            if (recall + 1e-12 < floor)
                failedChecks.Add($"{check}={recall.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"is below the predeclared {floor.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)} floor");
        }
    }

    public static void RequireMinimumRecall(
        IEnumerable<VectorQualityMeasurement> measurements,
        string context)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        double floor = MinimumAcceptedRecall;
        foreach (VectorQualityMeasurement measurement in measurements)
        {
            RequireRecall(measurement.RecallAtK, floor, $"{context} recall@{measurement.TopK}");
        }
    }

    private static void RequireRecall(double recall, double floor, string context)
    {
        if (recall + 1e-12 < floor)
        {
            throw new InvalidOperationException(
                $"{context}={recall.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)} "
                + $"is below the predeclared {floor.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)} floor.");
        }
    }

    private static double MeanRecall(IReadOnlyList<VectorQueryQualityResult> results, int topK)
        => results.Average(result => result.Measurements.Single(measurement => measurement.TopK == topK).RecallAtK);

    private static double LoadAndValidateFixture()
    {
        string absolutePath = Path.Combine(
            BenchmarkHelpers.RepositoryRoot,
            FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(absolutePath));
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(), "bval-09-vector-recall-ground-truth", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean()
            || !string.Equals(root.GetProperty("domain").GetString(), "approximate-vector-recall", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-09 fixture metadata is invalid: {FixtureRelativePath}.");
        }

        JsonElement input = root.GetProperty("input");
        JsonElement expected = root.GetProperty("expected");
        if (!string.Equals(input.GetProperty("metric").GetString(), "cosine", StringComparison.Ordinal))
            throw new InvalidOperationException("BVAL-09 fixture must use cosine similarity.");

        float[] query = input.GetProperty("queryVector").EnumerateArray()
            .Select(static value => value.GetSingle()).ToArray();
        var fixtureVectors = input.GetProperty("vectors").EnumerateArray()
            .Select(static (item, ordinal) => (
                Id: item.GetProperty("id").GetString()!,
                Vector: item.GetProperty("value").EnumerateArray().Select(static value => value.GetSingle()).ToArray(),
                Ordinal: ordinal))
            .ToArray();
        int k = input.GetProperty("k").GetInt32();
        int[] requiredRecallAtK = expected.GetProperty("requiredRecallAtK").EnumerateArray()
            .Select(static value => value.GetInt32()).ToArray();
        string[] expectedIds = expected.GetProperty("exactTopKIds").EnumerateArray()
            .Select(static value => value.GetString()!).ToArray();
        string[] approximateIds = input.GetProperty("approximateReturnedIds").EnumerateArray()
            .Select(static value => value.GetString()!).ToArray();
        double declaredRecall = expected.GetProperty("recallAtK").GetDouble();
        double minimumAcceptedRecall = expected.GetProperty("minimumAcceptedRecall").GetDouble();

        if (query.Length == 0 || fixtureVectors.Length < k || k < 1
            || fixtureVectors.Any(item => item.Vector.Length != query.Length)
            || fixtureVectors.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count() != fixtureVectors.Length
            || expectedIds.Length != k || approximateIds.Length == 0
            || !(minimumAcceptedRecall > 0d && minimumAcceptedRecall <= 1d)
            || !requiredRecallAtK.SequenceEqual([1, 10, 100])
            || string.IsNullOrWhiteSpace(expected.GetProperty("scoreError").GetString())
            || string.IsNullOrWhiteSpace(expected.GetProperty("rankError").GetString())
            || string.IsNullOrWhiteSpace(expected.GetProperty("quantisationError").GetString())
            || expected.GetProperty("comparisonQualifies").GetBoolean())
        {
            throw new InvalidOperationException($"BVAL-09 fixture contract is invalid: {FixtureRelativePath}.");
        }

        string[] computedExactIds = fixtureVectors
            .Select(item => (item.Id, item.Ordinal, Score: Cosine(query, item.Vector)))
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Ordinal)
            .Take(k)
            .Select(static item => item.Id)
            .ToArray();
        if (!computedExactIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("BVAL-09 fixture exact top-K IDs do not match cosine ground truth.");

        var idToOrdinal = fixtureVectors.ToDictionary(static item => item.Id, static item => item.Ordinal, StringComparer.Ordinal);
        if (approximateIds.Any(id => !idToOrdinal.ContainsKey(id)))
            throw new InvalidOperationException("BVAL-09 negative-control results contain an unknown vector ID.");

        var exactOrdinals = expectedIds.Select(id => (long)idToOrdinal[id]).ToHashSet();
        int recallHits = approximateIds.Select(id => (long)idToOrdinal[id]).Distinct().Count(exactOrdinals.Contains);
        double computedRecall = (double)recallHits / k;
        if (Math.Abs(computedRecall - declaredRecall) > 1e-12 || computedRecall >= minimumAcceptedRecall)
            throw new InvalidOperationException("BVAL-09 negative control does not reproduce the declared below-floor recall.");

        try
        {
            RequireRecall(computedRecall, minimumAcceptedRecall, "BVAL-09 one-of-ten negative control recall");
        }
        catch (InvalidOperationException)
        {
            return minimumAcceptedRecall;
        }

        throw new InvalidOperationException("BVAL-09 recall gate accepted its below-floor negative control.");
    }

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0d, leftNorm = 0d, rightNorm = 0d;
        for (var i = 0; i < left.Length; i++)
        {
            double l = left[i];
            double r = right[i];
            dot += l * r;
            leftNorm += l * l;
            rightNorm += r * r;
        }

        if (leftNorm == 0d || rightNorm == 0d)
            throw new InvalidOperationException("BVAL-09 fixture cosine vectors must be non-zero.");
        return dot / Math.Sqrt(leftNorm * rightNorm);
    }
}
