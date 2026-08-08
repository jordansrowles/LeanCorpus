namespace Rowles.LeanCorpus.Search.Ranking;

/// <summary>A graded relevance judgement supplied by an application-owned evaluation set.</summary>
public sealed record RelevanceJudgement(string QueryIdentity, string DocumentIdentity, int Grade, string? Tag = null);

/// <summary>Controls how unjudged documents contribute to offline metrics.</summary>
public enum UnjudgedDocumentPolicy { Ignore, TreatAsNonRelevant }

/// <summary>Aggregate metrics for a ranked query result.</summary>
public sealed record RankingMetricResult(double PrecisionAtK, double RecallAtK, double ReciprocalRank,
    double AveragePrecision, double Dcg, double Ndcg, int JudgedResults, int UnjudgedResults, int RelevantDocuments);

/// <summary>Calculates deterministic, judgement-aware information-retrieval metrics.</summary>
public static class RankingMetrics
{
    public static RankingMetricResult Calculate(IEnumerable<string> rankedDocumentIds,
        IEnumerable<RelevanceJudgement> judgements, int k, UnjudgedDocumentPolicy unjudgedPolicy = UnjudgedDocumentPolicy.Ignore)
    {
        ArgumentNullException.ThrowIfNull(rankedDocumentIds); ArgumentNullException.ThrowIfNull(judgements);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        var ranking = rankedDocumentIds.Take(k).ToArray();
        var grades = judgements.GroupBy(static j => j.DocumentIdentity, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.Max(static j => j.Grade), StringComparer.Ordinal);
        int relevant = grades.Values.Count(static g => g > 0), judged = 0, unjudged = 0, retrievedRelevant = 0;
        double reciprocal = 0, precisionSum = 0, dcg = 0;
        for (int i = 0; i < ranking.Length; i++)
        {
            bool has = grades.TryGetValue(ranking[i], out int grade);
            if (has) judged++; else unjudged++;
            bool isRelevant = has && grade > 0;
            if (isRelevant)
            {
                retrievedRelevant++;
                reciprocal = reciprocal == 0 ? 1d / (i + 1) : reciprocal;
                precisionSum += (double)retrievedRelevant / (i + 1);
            }
            if (has || unjudgedPolicy == UnjudgedDocumentPolicy.TreatAsNonRelevant)
                dcg += (Math.Pow(2, Math.Max(grade, 0)) - 1) / Math.Log2(i + 2);
        }
        double ideal = grades.Values.OrderByDescending(static g => g).Take(k).Select((grade, index) =>
            (Math.Pow(2, Math.Max(grade, 0)) - 1) / Math.Log2(index + 2)).Sum();
        int denominator = unjudgedPolicy == UnjudgedDocumentPolicy.Ignore ? judged : ranking.Length;
        return new RankingMetricResult(denominator == 0 ? 0 : (double)retrievedRelevant / denominator,
            relevant == 0 ? 0 : (double)retrievedRelevant / relevant, reciprocal,
            relevant == 0 ? 0 : precisionSum / relevant, dcg, ideal == 0 ? 0 : dcg / ideal,
            judged, unjudged, relevant);
    }
}

/// <summary>A privacy-safe feature value attached to a bounded ranking candidate.</summary>
public sealed record RankingFeature(string Name, string Version, double? Value, bool UsedForScoring, string Provenance);

/// <summary>A bounded feature record. Identifiers must be application-provided surrogates.</summary>
public sealed record RankingFeatureRecord(string RequestIdentity, string DocumentIdentity, int Rank,
    string ProfileFingerprint, string SchemaVersion, IReadOnlyList<RankingFeature> Features);

/// <summary>Receives feature records away from the scoring path.</summary>
public interface IRankingFeatureSink { bool TryWrite(RankingFeatureRecord record); }

/// <summary>No-op feature sink used when feature logging is disabled.</summary>
public sealed class NullRankingFeatureSink : IRankingFeatureSink
{
    public static NullRankingFeatureSink Instance { get; } = new(); private NullRankingFeatureSink() { }
    public bool TryWrite(RankingFeatureRecord record) => true;
}
