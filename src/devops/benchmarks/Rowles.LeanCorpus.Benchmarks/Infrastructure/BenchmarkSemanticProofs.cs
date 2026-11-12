using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// A versioned semantic proof for benchmark rows (ADR047): what was verified, against which
/// fixture and measured dataset, at which commit, and whether it passed. A benchmark class writes
/// one as a sidecar from its own process; the report builder attaches matching proofs to rows.
/// </summary>
internal sealed class BenchmarkSemanticProofReport
{
    public const int CurrentProofVersion = 1;

    public int ProofVersion { get; set; } = CurrentProofVersion;
    public string ProofId { get; set; } = string.Empty;
    public string FixtureId { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string[] Methods { get; set; } = [];
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
    public string DatasetContentSha256 { get; set; } = string.Empty;
    public string SourceCommit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public List<string> Reasons { get; set; } = [];
    public string DigestSha256 { get; set; } = string.Empty;
    public int VerificationCount { get; set; }
}

internal static class BenchmarkSemanticProofs
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";

    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    private const string DirectoryName = "semantic-proofs";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Writes one proof sidecar for <paramref name="benchmarkType"/> and the given methods and
    /// parameters, bound to the measured dataset and the source commit under test. Each call writes
    /// a new file, so concurrent benchmark processes never overwrite one another.
    /// </summary>
    public static void Record(
        Type benchmarkType,
        IReadOnlyList<string> methods,
        IReadOnlyDictionary<string, string> parameters,
        string proofId,
        string fixtureId,
        string datasetContentSha256,
        bool passed,
        IEnumerable<string> reasons,
        string digestSha256,
        int verificationCount)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);
        var proof = new BenchmarkSemanticProofReport
        {
            ProofId = proofId,
            FixtureId = fixtureId,
            TypeName = benchmarkType.Name,
            Methods = [.. methods.Order(StringComparer.Ordinal)],
            Parameters = parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            DatasetContentSha256 = datasetContentSha256,
            SourceCommit = Environment.GetEnvironmentVariable("BENCH_SOURCE_COMMIT") ?? string.Empty,
            State = passed ? Passed : Failed,
            Reasons = [.. reasons],
            DigestSha256 = digestSha256,
            VerificationCount = verificationCount
        };

        string directory = Path.Combine(RequireArtifactDirectory(), DirectoryName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, string.Concat(proofId, "-", Guid.NewGuid().ToString("N"), ".json"));
        string temporary = string.Concat(path, ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(proof, JsonOptions));
        File.Move(temporary, path);
    }

    /// <summary>Reads every proof sidecar under an artifact directory.</summary>
    public static IReadOnlyList<BenchmarkSemanticProofReport> ReadAll(string? artifactDirectory)
    {
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            return [];
        string directory = Path.Combine(Path.GetFullPath(artifactDirectory), DirectoryName);
        if (!Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(static path => JsonSerializer.Deserialize<BenchmarkSemanticProofReport>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Semantic proof sidecar '{path}' is empty."))
            .ToArray();
    }

    /// <summary>
    /// Combines the proofs that cover one row into the row's proof. Every matching proof must agree
    /// on its identity, dataset, commit and digest and must have passed; otherwise the combined proof
    /// fails with a named reason. No matching proof returns <see langword="null"/>.
    /// </summary>
    public static BenchmarkSemanticProofReport? ForRow(
        IReadOnlyList<BenchmarkSemanticProofReport> proofs,
        string typeName,
        string methodName,
        IReadOnlyDictionary<string, string> parameters)
    {
        string shortType = typeName.Split('.').Last();
        var matching = proofs.Where(proof =>
                string.Equals(proof.TypeName, shortType, StringComparison.Ordinal) &&
                proof.Methods.Contains(methodName, StringComparer.Ordinal) &&
                ParametersEqual(proof.Parameters, parameters))
            .ToArray();
        if (matching.Length == 0)
            return null;

        var first = matching[0];
        var combined = new BenchmarkSemanticProofReport
        {
            ProofVersion = first.ProofVersion,
            ProofId = first.ProofId,
            FixtureId = first.FixtureId,
            TypeName = first.TypeName,
            Methods = first.Methods,
            Parameters = first.Parameters,
            DatasetContentSha256 = first.DatasetContentSha256,
            SourceCommit = first.SourceCommit,
            DigestSha256 = first.DigestSha256,
            VerificationCount = matching.Sum(static proof => proof.VerificationCount),
            Reasons = matching.SelectMany(static proof => proof.Reasons).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
        };
        if (matching.Any(proof => proof.ProofVersion != first.ProofVersion || proof.ProofId != first.ProofId ||
                                  proof.FixtureId != first.FixtureId || proof.DatasetContentSha256 != first.DatasetContentSha256 ||
                                  proof.SourceCommit != first.SourceCommit || proof.DigestSha256 != first.DigestSha256))
            combined.Reasons.Add("ConflictingProofs");
        bool passed = combined.Reasons.Count == 0 && matching.All(static proof => proof.State == Passed) &&
                      combined.VerificationCount > 0;
        if (!passed && combined.Reasons.Count == 0)
            combined.Reasons.Add(combined.VerificationCount > 0 ? "ProofFailed" : "ProofHasNoVerification");
        combined.State = passed ? Passed : Failed;
        return combined;
    }

    /// <summary>The content hash of the generated <c>leancorpus-search</c> dataset with this record count, or empty.</summary>
    public static string SearchDatasetContentSha256(int documentCount)
    {
        var dataset = BenchmarkDatasetSidecars.ReadAll(RequireArtifactDirectory())
            .Where(evidence => string.Equals(evidence.Identity.ProfileId, "leancorpus-search", StringComparison.Ordinal) &&
                               evidence.Identity.RecordCount == documentCount)
            .ToArray();
        // No unique generated dataset (for example a reference-corpus run) leaves the proof unbound,
        // which fails qualification closed instead of failing the benchmark.
        return dataset.Length == 1 ? dataset[0].Identity.ContentSha256 : string.Empty;
    }

    /// <summary>Lower-case SHA-256 of a UTF-8 digest source.</summary>
    public static string Digest(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static bool ParametersEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    private static string RequireArtifactDirectory()
    {
        string? artifactRoot = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        return string.IsNullOrWhiteSpace(artifactRoot)
            ? throw new InvalidOperationException($"{ArtifactDirectoryVariable} must be set before a semantic proof is recorded.")
            : Path.GetFullPath(artifactRoot);
    }
}
