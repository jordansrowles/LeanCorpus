using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures token-budget enforcement where every mode indexes the same documents and tokens.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[InvocationCount(1)]
public class TokenBudgetBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private int _sameWorkBudget;
    private readonly List<string> _createdPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _sameWorkBudget = TokenBudgetBenchmarkSupport.PreflightAndWriteEvidence(
            nameof(TokenBudgetBenchmarks), DocumentCount, _documents);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach (var path in _createdPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _createdPaths.Clear();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_NoBudget_SameWork()
        => TokenBudgetBenchmarkSupport.IndexForBenchmark(
            _documents, 0, TokenBudgetPolicy.Truncate, _createdPaths);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_Budget_Truncate_SameWork()
        => TokenBudgetBenchmarkSupport.IndexForBenchmark(
            _documents, _sameWorkBudget, TokenBudgetPolicy.Truncate, _createdPaths);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_Budget_Reject_SameWork()
        => TokenBudgetBenchmarkSupport.IndexForBenchmark(
            _documents, _sameWorkBudget, TokenBudgetPolicy.Reject, _createdPaths);
}

/// <summary>
/// Records fixed 100-token truncation and rejection as separate policy contracts without ratios.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[InvocationCount(1)]
public class TokenBudgetPolicyVariantBenchmarks
{
    private const int FixedTokenBudget = 100;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private readonly List<string> _createdPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        TokenBudgetBenchmarkSupport.PreflightAndWriteEvidence(
            nameof(TokenBudgetPolicyVariantBenchmarks), DocumentCount, _documents);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach (var path in _createdPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _createdPaths.Clear();
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_Fixed100_Truncate()
        => TokenBudgetBenchmarkSupport.IndexForBenchmark(
            _documents, FixedTokenBudget, TokenBudgetPolicy.Truncate, _createdPaths);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_Fixed100_Reject()
        => TokenBudgetBenchmarkSupport.IndexForBenchmark(
            _documents, FixedTokenBudget, TokenBudgetPolicy.Reject, _createdPaths);
}

internal sealed record TokenBudgetModeOutcome(
    string Mode,
    string ComparisonGroup,
    int MaximumTokensPerDocument,
    string Policy,
    int SourceDocuments,
    long SourceBodyTokens,
    int AcceptedDocuments,
    int RejectedDocuments,
    int IndexedDocuments,
    long IndexedBodyTokens);

internal static class TokenBudgetBenchmarkSupport
{
    private const string FixtureRelativePath =
        ".config/devops/benchmark-correctness-fixtures/bval-10-token-budget-work-counts.json";
    private const string FixtureId = "bval-10-token-budget-work-counts";
    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static int PreflightAndWriteEvidence(string benchmarkClass, int documentCount, string[] documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkClass);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Length != documentCount || documentCount <= 0)
            throw new InvalidOperationException("Token-budget benchmark document count does not match its source dataset.");

        FixtureEvidence fixtureEvidence = ValidateFixture();
        int[] sourceTokenCounts = CountDocumentTokens(documents);
        int sameWorkBudget = sourceTokenCounts.Max();
        long sourceBodyTokens = sourceTokenCounts.Sum(static count => (long)count);
        var datasetIdentity = BenchmarkData.GetDatasetIdentity(documentCount);

        TokenBudgetModeOutcome[] outcomes =
        [
            Measure("Unlimited", "same-work", documents, 0, TokenBudgetPolicy.Truncate, sourceBodyTokens),
            Measure("SameWork_Truncate", "same-work", documents, sameWorkBudget, TokenBudgetPolicy.Truncate, sourceBodyTokens),
            Measure("SameWork_Reject", "same-work", documents, sameWorkBudget, TokenBudgetPolicy.Reject, sourceBodyTokens),
            Measure("Fixed100_Truncate", "separate-policy-variant", documents, 100, TokenBudgetPolicy.Truncate, sourceBodyTokens),
            Measure("Fixed100_Reject", "separate-policy-variant", documents, 100, TokenBudgetPolicy.Reject, sourceBodyTokens)
        ];

        TokenBudgetModeOutcome[] sameWorkOutcomes = outcomes
            .Where(static outcome => outcome.ComparisonGroup == "same-work")
            .ToArray();
        if (sameWorkOutcomes.Any(outcome => outcome.AcceptedDocuments != documentCount
                || outcome.RejectedDocuments != 0
                || outcome.IndexedDocuments != documentCount
                || outcome.IndexedBodyTokens != sourceBodyTokens))
        {
            throw new InvalidOperationException("Same-work token-budget variants do not index all source documents and tokens.");
        }

        WriteEvidence(benchmarkClass, "fixture", new
        {
            FixtureId,
            FixtureRelativePath,
            fixtureEvidence.FixtureSha256,
            fixtureEvidence.SourceDocuments,
            fixtureEvidence.SourceBodyTokens,
            fixtureEvidence.MaximumTokensPerDocument,
            fixtureEvidence.SameWorkComparisonAllowedAcrossModes,
            Modes = fixtureEvidence.Outcomes
        });

        WriteEvidence(benchmarkClass, "dataforge", new
        {
            FixtureId,
            fixtureEvidence.FixtureSha256,
            DatasetIdentity = datasetIdentity,
            DocumentCount = documentCount,
            SourceBodyTokens = sourceBodyTokens,
            SameWorkBudget = sameWorkBudget,
            SameWorkComparisonAllowed = true,
            CrossPolicyVariantComparisonAllowed = false,
            Modes = outcomes
        });

        return sameWorkBudget;
    }

    public static int IndexForBenchmark(
        string[] documents,
        int maximumTokensPerDocument,
        TokenBudgetPolicy policy,
        List<string> iterationPaths)
        => Index(documents, maximumTokensPerDocument, policy, collectStatistics: false, iterationPaths).AcceptedDocuments;

    private static FixtureEvidence ValidateFixture()
    {
        string fixturePath = Path.Combine(
            BenchmarkHelpers.RepositoryRoot,
            FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] fixtureBytes = File.ReadAllBytes(fixturePath);
        string fixtureSha256 = Convert.ToHexString(SHA256.HashData(fixtureBytes)).ToLowerInvariant();
        using JsonDocument fixture = JsonDocument.Parse(fixtureBytes);
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(), FixtureId, StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean()
            || !string.Equals(root.GetProperty("domain").GetString(), "token-budget-accepted-work", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-10 fixture metadata is invalid: {FixtureRelativePath}.");
        }

        JsonElement input = root.GetProperty("input");
        JsonElement expected = root.GetProperty("expected");
        int maximumTokensPerDocument = input.GetProperty("maximumTokensPerDocument").GetInt32();
        bool sameWorkComparisonAllowedAcrossModes = expected.GetProperty("sameWorkComparisonAllowedAcrossModes").GetBoolean();
        JsonElement[] fixtureDocuments = input.GetProperty("documents").EnumerateArray().ToArray();
        string[] declaredModes = input.GetProperty("modes").EnumerateArray()
            .Select(static mode => mode.GetString() ?? string.Empty)
            .ToArray();
        if (maximumTokensPerDocument <= 0
            || fixtureDocuments.Length == 0
            || !declaredModes.SequenceEqual(["Unlimited", "Truncate", "Reject"], StringComparer.Ordinal)
            || sameWorkComparisonAllowedAcrossModes)
        {
            throw new InvalidOperationException($"BVAL-10 fixture contract is invalid: {FixtureRelativePath}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var fixtureBodies = new string[fixtureDocuments.Length];
        var fixtureAnalyser = new StandardAnalyser();
        var tokenSink = new CountingTokenSink();
        long sourceBodyTokens = 0;
        int maximumObservedTokenCount = 0;
        for (int i = 0; i < fixtureDocuments.Length; i++)
        {
            JsonElement document = fixtureDocuments[i];
            string id = document.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("BVAL-10 fixture document ID is null.");
            int tokenCount = document.GetProperty("tokenCount").GetInt32();
            string tokenPattern = document.GetProperty("tokenPattern").GetString()
                ?? throw new InvalidOperationException("BVAL-10 fixture token pattern is null.");
            if (string.IsNullOrWhiteSpace(id)
                || !ids.Add(id)
                || tokenCount <= maximumTokensPerDocument
                || !tokenPattern.Contains("{index:D3}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"BVAL-10 fixture document contract is invalid for '{id}'.");
            }

            fixtureBodies[i] = string.Join(' ', Enumerable.Range(0, tokenCount)
                .Select(index => tokenPattern.Replace(
                    "{index:D3}", index.ToString("D3", CultureInfo.InvariantCulture), StringComparison.Ordinal)));
            fixtureAnalyser.Analyse(fixtureBodies[i].AsSpan(), tokenSink);
            if (tokenSink.Count != tokenCount)
            {
                throw new InvalidOperationException(
                    $"BVAL-10 fixture document '{id}' declares {tokenCount} tokens but StandardAnalyser emits {tokenSink.Count}.");
            }

            sourceBodyTokens += tokenSink.Count;
            maximumObservedTokenCount = Math.Max(maximumObservedTokenCount, tokenSink.Count);
            tokenSink.Reset();
        }

        if (maximumObservedTokenCount <= maximumTokensPerDocument)
            throw new InvalidOperationException("BVAL-10 fixture does not exercise its token budget.");

        var outcomes = new List<TokenBudgetModeOutcome>(declaredModes.Length);
        foreach (string mode in declaredModes)
        {
            (int budget, TokenBudgetPolicy policy) = mode switch
            {
                "Unlimited" => (0, TokenBudgetPolicy.Truncate),
                "Truncate" => (maximumTokensPerDocument, TokenBudgetPolicy.Truncate),
                "Reject" => (maximumTokensPerDocument, TokenBudgetPolicy.Reject),
                _ => throw new InvalidOperationException($"Unsupported BVAL-10 fixture mode '{mode}'.")
            };
            TokenBudgetModeOutcome outcome = Measure(
                mode, "fixture-contract", fixtureBodies, budget, policy, sourceBodyTokens);
            outcomes.Add(outcome);

            int expectedAcceptedDocuments = expected.GetProperty("acceptedDocuments").GetProperty(mode).GetInt32();
            long expectedAcceptedTokens = expected.GetProperty("acceptedTokens").GetProperty(mode).GetInt64();
            int expectedRejectedDocuments = expected.GetProperty("rejectedDocuments").GetProperty(mode).GetInt32();
            if (outcome.AcceptedDocuments != expectedAcceptedDocuments
                || outcome.IndexedBodyTokens != expectedAcceptedTokens
                || outcome.RejectedDocuments != expectedRejectedDocuments
                || outcome.IndexedDocuments != expectedAcceptedDocuments)
            {
                throw new InvalidOperationException(
                    $"BVAL-10 {mode} fixture result was docs={outcome.AcceptedDocuments}, "
                    + $"rejected={outcome.RejectedDocuments}, tokens={outcome.IndexedBodyTokens}; "
                    + $"expected docs={expectedAcceptedDocuments}, rejected={expectedRejectedDocuments}, tokens={expectedAcceptedTokens}.");
            }
        }

        return new FixtureEvidence(
            fixtureSha256,
            fixtureBodies.Length,
            sourceBodyTokens,
            maximumObservedTokenCount,
            sameWorkComparisonAllowedAcrossModes,
            outcomes.ToArray());
    }

    private static TokenBudgetModeOutcome Measure(
        string mode,
        string comparisonGroup,
        string[] documents,
        int maximumTokensPerDocument,
        TokenBudgetPolicy policy,
        long sourceBodyTokens)
    {
        TokenBudgetModeOutcome result = Index(
            documents,
            maximumTokensPerDocument,
            policy,
            collectStatistics: true,
            iterationPaths: null);
        if (result.IndexedDocuments != result.AcceptedDocuments)
        {
            throw new InvalidOperationException(
                $"Token-budget mode {mode} accepted {result.AcceptedDocuments} documents but indexed {result.IndexedDocuments} live documents.");
        }

        return result with
        {
            Mode = mode,
            ComparisonGroup = comparisonGroup,
            MaximumTokensPerDocument = maximumTokensPerDocument,
            Policy = policy.ToString(),
            SourceDocuments = documents.Length,
            SourceBodyTokens = sourceBodyTokens
        };
    }

    private static TokenBudgetModeOutcome Index(
        string[] documents,
        int maximumTokensPerDocument,
        TokenBudgetPolicy policy,
        bool collectStatistics,
        List<string>? iterationPaths)
    {
        string path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        iterationPaths?.Add(path);
        int acceptedDocuments = 0;
        int rejectedDocuments = 0;
        try
        {
            using (var directory = new MMapDirectory(path))
            using (var writer = new IndexWriter(directory, CreateConfig(maximumTokensPerDocument, policy)))
            {
                for (int i = 0; i < documents.Length; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new LeanStringField("id", i.ToString(CultureInfo.InvariantCulture)));
                    document.Add(new LeanTextField("body", documents[i]));

                    try
                    {
                        writer.AddDocument(document);
                        acceptedDocuments++;
                    }
                    catch (TokenBudgetExceededException) when (maximumTokensPerDocument > 0 && policy == TokenBudgetPolicy.Reject)
                    {
                        rejectedDocuments++;
                    }
                }

                writer.Commit();
            }

            int indexedDocuments = acceptedDocuments;
            long indexedBodyTokens = -1;
            if (collectStatistics)
            {
                using var searcher = new IndexSearcher(new MMapDirectory(path));
                indexedDocuments = searcher.Stats.LiveDocCount;
                indexedBodyTokens = searcher.Stats.GetFieldLengthSum("body");
            }

            return new TokenBudgetModeOutcome(
                string.Empty,
                string.Empty,
                maximumTokensPerDocument,
                policy.ToString(),
                documents.Length,
                0,
                acceptedDocuments,
                rejectedDocuments,
                indexedDocuments,
                indexedBodyTokens);
        }
        finally
        {
            if (iterationPaths is null)
                BenchmarkHelpers.DeleteDirectory(path);
        }
    }

    private static int[] CountDocumentTokens(string[] documents)
    {
        var analyser = new StandardAnalyser();
        var sink = new CountingTokenSink();
        var counts = new int[documents.Length];
        for (int i = 0; i < documents.Length; i++)
        {
            analyser.Analyse(documents[i].AsSpan(), sink);
            counts[i] = sink.Count;
            sink.Reset();
        }

        return counts;
    }

    private static IndexWriterConfig CreateConfig(int maximumTokensPerDocument, TokenBudgetPolicy policy)
        => new()
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            DefaultAnalyser = new StandardAnalyser(),
            MaxTokensPerDocument = maximumTokensPerDocument,
            TokenBudgetPolicy = policy
        };

    private static void WriteEvidence(string benchmarkClass, string name, object evidence)
    {
        string? artifactDirectory = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            throw new InvalidOperationException($"{ArtifactDirectoryVariable} is required for BVAL-10 evidence output.");

        string outputDirectory = Path.Combine(artifactDirectory, "token-budget");
        Directory.CreateDirectory(outputDirectory);
        string fileName = $"{benchmarkClass}-{name}.json";
        string outputPath = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
    }

    private sealed record FixtureEvidence(
        string FixtureSha256,
        int SourceDocuments,
        long SourceBodyTokens,
        int MaximumTokensPerDocument,
        bool SameWorkComparisonAllowedAcrossModes,
        TokenBudgetModeOutcome[] Outcomes);
}
