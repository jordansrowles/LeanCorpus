using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Rowles.LeanCorpus.Search;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed class SuggesterQualityFixture
{
    private const string FixtureRelativePath = ".config/devops/benchmark-correctness-fixtures/bval-15-suggester-quality-ground-truth.json";

    public string FixtureId { get; init; } = string.Empty;
    public int MaximumEditDistance { get; init; }
    public int TopN { get; init; }
    public string QueryNormalisation { get; init; } = string.Empty;
    public SuggesterCorrectionCase[] CorrectionCases { get; init; } = [];
    public SuggesterNoSuggestionCase NoSuggestionCase { get; init; } = new();
    public SuggesterProbeCase[] Probes { get; init; } = [];

    public static SuggesterQualityFixture Load(string repositoryRoot)
    {
        string path = Path.GetFullPath(Path.Combine(repositoryRoot, FixtureRelativePath));
        if (!File.Exists(path))
            throw new FileNotFoundException("The BVAL-15 suggestion-quality fixture is missing.", path);

        var document = JsonSerializer.Deserialize<SuggesterQualityFixtureDocument>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("The BVAL-15 suggestion-quality fixture is empty.");
        if (document.SchemaVersion != 1 || document.FixtureId != "bval-15-suggester-quality-ground-truth" || !document.Deterministic)
            throw new InvalidDataException("The BVAL-15 suggestion-quality fixture identity is invalid.");
        if (document.Input.MaximumEditDistance != 2 || document.Input.TopN != 5)
            throw new InvalidDataException("The BVAL-15 suggestion-quality fixture must use maxEdits=2 and topN=5.");
        if (document.Input.CorrectionCases.Length == 0 || document.Input.Probes.Length != 3)
            throw new InvalidDataException("The BVAL-15 fixture must contain correction cases and the case, Unicode and compound probes.");
        if (document.Input.NoSuggestionCase.AcceptableCorrections.Length != 0 ||
            !document.Input.NoSuggestionCase.ExpectNoSuggestion || !document.Input.NoSuggestionCase.IncludeInTimedWorkload)
            throw new InvalidDataException("The BVAL-15 fixture's no-suggestion case must be empty and included in the timed query batch.");

        var correctionIds = new HashSet<string>(StringComparer.Ordinal);
        var correctionQueries = new HashSet<string>(StringComparer.Ordinal);
        foreach (SuggesterCorrectionCase item in document.Input.CorrectionCases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !correctionIds.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Query) || !correctionQueries.Add(item.Query) || item.AcceptableCorrections.Length == 0)
                throw new InvalidDataException("The BVAL-15 correction cases must have unique ids and queries with at least one acceptable correction.");
        }

        var probeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (SuggesterProbeCase item in document.Input.Probes)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !probeIds.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Query) || item.AcceptableCorrections.Length == 0 || !item.NormaliseBeforeSuggesting)
                throw new InvalidDataException("The BVAL-15 normalisation probes are invalid.");
            string query = SuggesterQualityNormalisation.Normalise(item.Query);
            if (item.AcceptableCorrections.Any(correction =>
                    LevenshteinDistance.Compute(query.AsSpan(), SuggesterQualityNormalisation.Normalise(correction).AsSpan()) > document.Input.MaximumEditDistance))
                throw new InvalidDataException($"The BVAL-15 normalisation probe '{item.Id}' exceeds the pinned edit-distance limit.");
        }
        if (!probeIds.SetEquals(["case-variant", "unicode-canonical-variant", "compound-variant"]))
            throw new InvalidDataException("The BVAL-15 fixture does not contain the required case, Unicode and compound probes.");

        return new SuggesterQualityFixture
        {
            FixtureId = document.FixtureId,
            MaximumEditDistance = document.Input.MaximumEditDistance,
            TopN = document.Input.TopN,
            QueryNormalisation = document.Input.QueryNormalisation,
            CorrectionCases = document.Input.CorrectionCases,
            NoSuggestionCase = document.Input.NoSuggestionCase,
            Probes = document.Input.Probes
        };
    }

    public void ValidateAgainstDataForgeWorkload(IReadOnlyList<(string Original, string Misspelled)> workload)
    {
        var expected = workload.Select(static pair => (Query: pair.Misspelled, Correction: pair.Original)).ToArray();
        var actual = CorrectionCases.Select(item =>
        {
            if (item.AcceptableCorrections.Length != 1)
                throw new InvalidDataException($"DataForge typo case '{item.Id}' must have exactly one pinned correction.");
            return (Query: item.Query, Correction: item.AcceptableCorrections[0]);
        }).ToArray();
        if (!expected.SequenceEqual(actual))
            throw new InvalidDataException("The BVAL-15 correction fixture no longer matches BenchmarkData.BuildMisspelledTerms().");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class SuggesterQualityFixtureDocument
    {
        public int SchemaVersion { get; init; }
        public string FixtureId { get; init; } = string.Empty;
        public bool Deterministic { get; init; }
        public SuggesterQualityFixtureInput Input { get; init; } = new();
    }

    private sealed class SuggesterQualityFixtureInput
    {
        public int MaximumEditDistance { get; init; }
        public int TopN { get; init; }
        public string QueryNormalisation { get; init; } = string.Empty;
        public SuggesterCorrectionCase[] CorrectionCases { get; init; } = [];
        public SuggesterNoSuggestionCase NoSuggestionCase { get; init; } = new();
        public SuggesterProbeCase[] Probes { get; init; } = [];
    }
}

internal sealed class SuggesterCorrectionCase
{
    public string Id { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string[] AcceptableCorrections { get; init; } = [];
}

internal sealed class SuggesterNoSuggestionCase
{
    public string Id { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string[] AcceptableCorrections { get; init; } = [];
    public bool ExpectNoSuggestion { get; init; }
    public bool IncludeInTimedWorkload { get; init; }
}

internal sealed class SuggesterProbeCase
{
    public string Id { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string[] AcceptableCorrections { get; init; } = [];
    public bool NormaliseBeforeSuggesting { get; init; }
}

internal sealed class SuggesterQualityEvidence
{
    public int SchemaVersion { get; init; } = 1;
    public string GateId { get; init; } = "BVAL-15";
    public string FixtureId { get; init; } = string.Empty;
    public string FixtureSha256 { get; init; } = string.Empty;
    public int DocumentCount { get; init; }
    public int MaximumEditDistance { get; init; }
    public int TopN { get; init; }
    public string QueryNormalisation { get; init; } = string.Empty;
    public string ClaimClass { get; init; } = "Directional";
    public SuggesterEngineQualityEvidence[] Engines { get; init; } = [];
}

internal sealed class SuggesterEngineQualityEvidence
{
    public string EngineId { get; init; } = string.Empty;
    public string BenchmarkMethod { get; init; } = string.Empty;
    public int CorrectionQueryCount { get; init; }
    public int TimedQueryCount { get; init; }
    public int CandidateCount { get; init; }
    public SuggesterQualityMetrics Metrics { get; init; } = new();
    public SuggesterQualityQueryResult[] QueryResults { get; init; } = [];
    public SuggesterQualityQueryResult[] ProbeResults { get; init; } = [];
    public SuggesterBenchmarkLatencyEvidence[] BenchmarkResults { get; set; } = [];
}

internal sealed class SuggesterQualityQueryResult
{
    public string Id { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string[] AcceptableCorrections { get; init; } = [];
    public bool IsNoSuggestionCase { get; init; }
    public bool ExpectNoSuggestion { get; init; }
    public int? FirstRelevantRank { get; init; }
    public bool? NoSuggestionSatisfied { get; init; }
    public SuggesterQualityCandidate[] Suggestions { get; init; } = [];
}

internal sealed class SuggesterQualityCandidate
{
    public int Rank { get; init; }
    public string Term { get; init; } = string.Empty;
    public string NormalisedTerm { get; init; } = string.Empty;
    public int EditDistance { get; init; }
    public bool IsAcceptableCorrection { get; init; }
}

internal sealed class SuggesterQualityMetrics
{
    public int CorrectionQueryCount { get; init; }
    public int TimedQueryCount { get; init; }
    public double AccuracyAt1 { get; init; }
    public double RecallAt5 { get; init; }
    public double MeanReciprocalRank { get; init; }
    public double Coverage { get; init; }
    public int CandidateCount { get; init; }
    public bool IntentionalNoSuggestionCaseCorrect { get; init; }
}

internal sealed class SuggesterBenchmarkLatencyEvidence
{
    public string ResultRowId { get; init; } = string.Empty;
    public string ResultState { get; init; } = string.Empty;
    public double? MeanNanoseconds { get; init; }
    public double? OperationsPerSecond { get; init; }
}

internal sealed class SuggesterQualityScorer
{
    public static SuggesterQualityMetrics ComputeMetrics(IReadOnlyList<SuggesterQualityQueryResult> queryResults)
    {
        ArgumentNullException.ThrowIfNull(queryResults);
        if (queryResults.Count == 0)
            throw new ArgumentException("At least one timed suggestion query is required.", nameof(queryResults));

        SuggesterQualityQueryResult[] correctionQueries = queryResults
            .Where(static result => result.AcceptableCorrections.Length > 0)
            .ToArray();
        if (correctionQueries.Length == 0)
            throw new ArgumentException("At least one correction query is required.", nameof(queryResults));

        int relevantAtOne = correctionQueries.Count(static result => result.FirstRelevantRank == 1);
        int relevantInTopFive = correctionQueries.Sum(static result => result.Suggestions
            .Take(5)
            .Count(static suggestion => suggestion.IsAcceptableCorrection));
        int acceptableCorrectionCount = correctionQueries.Sum(static result => result.AcceptableCorrections.Length);
        double reciprocalRankTotal = correctionQueries.Sum(static result =>
            result.FirstRelevantRank is int rank ? 1d / rank : 0d);
        SuggesterQualityQueryResult[] noSuggestionCases = queryResults
            .Where(static result => result.IsNoSuggestionCase)
            .ToArray();

        return new SuggesterQualityMetrics
        {
            CorrectionQueryCount = correctionQueries.Length,
            TimedQueryCount = queryResults.Count,
            AccuracyAt1 = (double)relevantAtOne / correctionQueries.Length,
            RecallAt5 = (double)relevantInTopFive / acceptableCorrectionCount,
            MeanReciprocalRank = reciprocalRankTotal / correctionQueries.Length,
            Coverage = (double)queryResults.Count(static result => result.Suggestions.Length > 0) / queryResults.Count,
            CandidateCount = queryResults.Sum(static result => result.Suggestions.Length),
            IntentionalNoSuggestionCaseCorrect = noSuggestionCases.Length == 1 && noSuggestionCases[0].Suggestions.Length == 0
        };
    }
}

internal static class SuggesterQualityNormalisation
{
    public static string Normalise(string term) => term.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}

internal static class SuggesterQualityEvidenceCollector
{
    private const string FilePrefix = "suggester-quality-";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void WriteForCurrentRun(SuggesterQualityEvidence evidence)
    {
        string? artifactDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            return;

        Directory.CreateDirectory(artifactDirectory);
        string path = Path.Combine(artifactDirectory,
            $"{FilePrefix}{evidence.DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}.json");
        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(evidence, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static int AttachToReport(string runDirectory, BenchmarkRunReport report)
    {
        if (!Directory.Exists(runDirectory))
            return 0;

        var paths = Directory.EnumerateFiles(runDirectory, $"{FilePrefix}*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
            return 0;

        var reportsByCount = new Dictionary<int, (SuggesterQualityEvidence Evidence, string Fingerprint)>();
        foreach (string path in paths)
        {
            var evidence = JsonSerializer.Deserialize<SuggesterQualityEvidence>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidDataException($"Suggestion quality evidence '{Path.GetFileName(path)}' is empty.");
            if (evidence.SchemaVersion != 1 || evidence.GateId != "BVAL-15" || evidence.Engines.Length != 3)
                throw new InvalidDataException($"Suggestion quality evidence '{Path.GetFileName(path)}' has an unsupported identity.");

            string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(evidence, JsonOptions)));
            if (reportsByCount.TryGetValue(evidence.DocumentCount, out var existing))
            {
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    throw new InvalidDataException($"Conflicting BVAL-15 quality evidence was produced for {evidence.DocumentCount} documents.");
                continue;
            }
            reportsByCount.Add(evidence.DocumentCount, (evidence, fingerprint));
        }

        foreach (var item in reportsByCount.Values.OrderBy(static item => item.Evidence.DocumentCount))
        {
            foreach (SuggesterEngineQualityEvidence engine in item.Evidence.Engines)
            {
                engine.BenchmarkResults = report.Suites
                    .SelectMany(static suite => suite.Benchmarks)
                    .Where(benchmark => benchmark.MethodName == engine.BenchmarkMethod &&
                        benchmark.Parameters.TryGetValue("DocumentCount", out string? value) &&
                        int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int count) &&
                        count == item.Evidence.DocumentCount)
                    .Select(static benchmark => new SuggesterBenchmarkLatencyEvidence
                    {
                        ResultRowId = benchmark.ResultRowId,
                        ResultState = benchmark.ResultState,
                        MeanNanoseconds = benchmark.Statistics?.MeanNanoseconds,
                        OperationsPerSecond = benchmark.Statistics?.OperationsPerSecond
                    })
                    .OrderBy(static result => result.ResultRowId, StringComparer.Ordinal)
                    .ToArray();
            }
            report.SuggesterQuality.Add(item.Evidence);
        }

        return report.SuggesterQuality.Count;
    }

    public static void DeleteSidecars(string runDirectory)
    {
        if (!Directory.Exists(runDirectory))
            return;
        foreach (string path in Directory.EnumerateFiles(runDirectory, $"{FilePrefix}*.json", SearchOption.TopDirectoryOnly))
            File.Delete(path);
    }
}
