using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public static class VectorGroundTruth
{
    public static VectorGroundTruthResult Compute(
        IEnumerable<VectorRecord> records,
        VectorQueryCase query,
        int topK,
        Func<long, bool>? allowedOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(query);
        if (topK < 1)
            throw new ArgumentOutOfRangeException(nameof(topK));
        if (query.Vector.Length == 0)
            throw new ArgumentException("The query vector must have at least one component.", nameof(query));

        var scores = new List<(long Ordinal, double Cosine)>();
        foreach (var record in records)
        {
            if (allowedOrdinal is not null && !allowedOrdinal(record.Ordinal))
                continue;
            if (record.Vector.Length != query.Vector.Length)
                throw new ArgumentException("Record and query dimensions differ.", nameof(records));

            double dot = 0, recordNorm = 0, queryNorm = 0;
            for (var dimension = 0; dimension < query.Vector.Length; dimension++)
            {
                var left = (double)record.Vector[dimension];
                var right = (double)query.Vector[dimension];
                dot += left * right;
                recordNorm += left * left;
                queryNorm += right * right;
            }
            if (recordNorm == 0 || queryNorm == 0)
                throw new InvalidDataException("Cosine ground truth requires non-zero vectors.");
            scores.Add((record.Ordinal, dot / Math.Sqrt(recordNorm * queryNorm)));
        }

        var ranked = scores.OrderByDescending(static item => item.Cosine)
            .ThenBy(static item => item.Ordinal)
            .Take(topK).ToArray();
        return new VectorGroundTruthResult(
            query.Id, topK, "cosine",
            ranked.Select(static item => item.Ordinal).ToArray(),
            ranked.Select(static item => item.Cosine).ToArray());
    }
}
