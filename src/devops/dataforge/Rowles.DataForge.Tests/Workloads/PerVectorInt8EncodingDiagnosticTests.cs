using System.Text.Json;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Search.Simd;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class PerVectorInt8EncodingDiagnosticTests
{
    [Fact]
    public void CompareCandidateInt8EncodingsAgainstFrozenUniformOracle()
    {
        BenchmarkVectorDataset dataset = BenchmarkVectorData.Get(
            10_000, 64, VectorDistribution.Uniform, 42, 2025, 4);
        VectorRecord[] normalisedRecords = dataset.Records.Select(record =>
        {
            float[] vector = record.Vector.ToArray();
            SimdVectorOps.NormaliseInPlace(vector);
            return record with { Vector = vector };
        }).ToArray();
        float[][] global = DecodeGlobal(normalisedRecords);
        float[][] globalSymmetric = DecodeGlobalSymmetric(normalisedRecords);
        float[][] rowAffine = DecodePerVectorAffine(normalisedRecords);
        float[][] rowSymmetric = DecodePerVectorSymmetric(normalisedRecords);

        var results = new
        {
            corpusSeed = 42,
            querySeed = 2025,
            recordCount = dataset.Records.Length,
            dimension = dataset.Records[0].Vector.Length,
            candidates = new[]
            {
                Measure("current-global-affine", dataset, global),
                Measure("global-zero-centred-v3-compatible", dataset, globalSymmetric),
                Measure("per-vector-affine", dataset, rowAffine),
                Measure("per-vector-zero-centred", dataset, rowSymmetric)
            }
        };

        string? diagnosticDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(diagnosticDirectory))
            diagnosticDirectory = RepositoryTemporaryStorage.GetAreaRoot("dataforge");
        Directory.CreateDirectory(diagnosticDirectory);
        File.WriteAllText(
            Path.Combine(diagnosticDirectory, "leancorpus-per-vector-int8-diagnostic.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(4, results.candidates.Length);
    }

    private static CandidateResult Measure(string name, BenchmarkVectorDataset dataset, float[][] decoded)
    {
        var queries = new List<QueryResult>(dataset.Queries.Length);
        foreach (VectorQueryCase query in dataset.Queries)
        {
            long[] expected = VectorGroundTruth.Compute(dataset.Records, query, 100).NeighbourOrdinals;
            var ranked = new (long Ordinal, double Score)[decoded.Length];
            for (int document = 0; document < decoded.Length; document++)
                ranked[document] = (dataset.Records[document].Ordinal, Cosine(decoded[document], query.Vector));
            Array.Sort(ranked, static (left, right) =>
            {
                int scoreOrder = right.Score.CompareTo(left.Score);
                return scoreOrder != 0 ? scoreOrder : left.Ordinal.CompareTo(right.Ordinal);
            });

            queries.Add(new QueryResult(
                query.Id,
                Recall(expected, ranked, 1),
                Recall(expected, ranked, 10),
                Recall(expected, ranked, 100),
                ranked[0].Ordinal,
                expected[0]));
        }

        return new CandidateResult(name,
            queries.Average(static query => query.RecallAt1),
            queries.Average(static query => query.RecallAt10),
            queries.Average(static query => query.RecallAt100),
            queries.ToArray());
    }

    private static double Recall(long[] expected, (long Ordinal, double Score)[] actual, int topK)
    {
        var expectedSet = expected.Take(topK).ToHashSet();
        int hits = actual.Take(topK).Count(item => expectedSet.Contains(item.Ordinal));
        return (double)hits / topK;
    }

    private static float[][] DecodeGlobal(VectorRecord[] records)
    {
        float min = records.SelectMany(static record => record.Vector).Min();
        float max = records.SelectMany(static record => record.Vector).Max();
        float alpha = (max - min) / 255f;
        return records.Select(record => record.Vector.Select(value =>
        {
            byte code = (byte)Math.Clamp((value - min) / alpha + 0.5f, 0f, 255f);
            return min + alpha * code;
        }).ToArray()).ToArray();
    }

    private static float[][] DecodePerVectorAffine(VectorRecord[] records)
        => records.Select(record =>
        {
            float min = record.Vector.Min();
            float max = record.Vector.Max();
            float alpha = (max - min) / 255f;
            if (alpha == 0f)
                return record.Vector.ToArray();
            return record.Vector.Select(value =>
            {
                byte code = (byte)Math.Clamp((value - min) / alpha + 0.5f, 0f, 255f);
                return min + alpha * code;
            }).ToArray();
        }).ToArray();

    private static float[][] DecodeGlobalSymmetric(VectorRecord[] records)
    {
        float maximumMagnitude = records.SelectMany(static record => record.Vector)
            .Max(static value => MathF.Abs(value));
        float alpha = maximumMagnitude / 127f;
        float min = -128f * alpha;
        return records.Select(record => record.Vector.Select(value =>
        {
            byte code = (byte)Math.Clamp((value - min) / alpha + 0.5f, 0f, 255f);
            return min + alpha * code;
        }).ToArray()).ToArray();
    }

    private static float[][] DecodePerVectorSymmetric(VectorRecord[] records)
        => records.Select(record =>
        {
            float maximumMagnitude = record.Vector.Max(static value => MathF.Abs(value));
            float scale = maximumMagnitude / 127f;
            if (scale == 0f)
                return record.Vector.ToArray();
            return record.Vector.Select(value =>
            {
                int code = (int)Math.Clamp(MathF.Round(value / scale, MidpointRounding.AwayFromZero), -127f, 127f);
                return code * scale;
            }).ToArray();
        }).ToArray();

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (int component = 0; component < left.Length; component++)
        {
            dot += (double)left[component] * right[component];
            leftNorm += (double)left[component] * left[component];
            rightNorm += (double)right[component] * right[component];
        }
        return dot / Math.Sqrt(leftNorm * rightNorm);
    }

    private sealed record CandidateResult(
        string Name,
        double MeanRecallAt1,
        double MeanRecallAt10,
        double MeanRecallAt100,
        QueryResult[] Queries);

    private sealed record QueryResult(
        string QueryId,
        double RecallAt1,
        double RecallAt10,
        double RecallAt100,
        long ActualTopOne,
        long ExpectedTopOne);
}
