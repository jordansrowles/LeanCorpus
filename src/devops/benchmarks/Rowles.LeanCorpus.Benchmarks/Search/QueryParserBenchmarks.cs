using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures valid query parsing and input-limit rejection as separate operations.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QueryParserBenchmarks
{
    private string _largeValidInput = string.Empty;
    private string _inputAtLimit = string.Empty;
    private string _inputAboveLimit = string.Empty;
    private QueryParser _compatibilityParser = null!;
    private QueryParser _boundedParser = null!;
    private int _guardLimitCharacters;

    [GlobalSetup]
    public void Setup()
    {
        QueryParserBoundaryFixture fixture = QueryParserBoundaryBenchmarkQuality.ValidateAndWriteEvidence();
        _largeValidInput = fixture.LargeValidInput;
        _inputAtLimit = fixture.InputAtLimit;
        _inputAboveLimit = fixture.InputAboveLimit;
        _guardLimitCharacters = fixture.GuardLimitCharacters;
        _compatibilityParser = new QueryParser("body", new KeywordAnalyser());
        _boundedParser = new QueryParser(
            "body",
            new KeywordAnalyser(),
            QueryParserOptions.Default with { MaxInputChars = _guardLimitCharacters });
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query CompatibilityParserParsesLargeValidInput() => _compatibilityParser.Parse(_largeValidInput);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query BoundedParserAcceptsAtLimit() => _boundedParser.Parse(_inputAtLimit);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool BoundedParserRejectsLimitPlusOne()
    {
        try
        {
            _boundedParser.Parse(_inputAboveLimit);
        }
        catch (QueryParseException exception) when (exception.Offset == _guardLimitCharacters)
        {
            return true;
        }

        return false;
    }
}

internal sealed record QueryParserBoundaryFixture(
    string FixtureSha256,
    int GuardLimitCharacters,
    string LargeValidInput,
    string InputAtLimit,
    string InputAboveLimit);

internal static class QueryParserBoundaryBenchmarkQuality
{
    private const string FixtureRelativePath =
        ".config/devops/benchmark-correctness-fixtures/bval-11-parser-success-versus-rejection.json";
    private const string FixtureId = "bval-11-parser-success-versus-rejection";
    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static QueryParserBoundaryFixture ValidateAndWriteEvidence()
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
            || !string.Equals(root.GetProperty("domain").GetString(), "query-parser-success-and-guard", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-11 fixture metadata is invalid: {FixtureRelativePath}.");
        }

        JsonElement input = root.GetProperty("input");
        JsonElement expected = root.GetProperty("expected");
        int guardLimitCharacters = input.GetProperty("configuredGuardLimitCharacters").GetInt32();
        JsonElement generator = input.GetProperty("queryGenerator");
        string prefix = generator.GetProperty("prefix").GetString() ?? string.Empty;
        string repeatedCharacter = generator.GetProperty("repeatedCharacter").GetString() ?? string.Empty;
        string suffix = generator.GetProperty("suffix").GetString() ?? string.Empty;
        bool lengthIncludesPrefix = generator.GetProperty("lengthIncludesPrefix").GetBoolean();
        FixtureCase[] cases = input.GetProperty("cases").EnumerateArray()
            .Select(static item => new FixtureCase(
                item.GetProperty("id").GetString() ?? string.Empty,
                item.GetProperty("inputLengthCharacters").GetInt32(),
                item.GetProperty("expectedOperation").GetString() ?? string.Empty))
            .ToArray();

        if (guardLimitCharacters <= 0
            || repeatedCharacter.Length != 1
            || !lengthIncludesPrefix
            || cases.Length != 3
            || !cases.Select(static item => item.Id).SequenceEqual(
                ["compatibility-parse", "guard-boundary-exact-limit", "guard-boundary-plus-one"], StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-11 fixture contract is invalid: {FixtureRelativePath}.");
        }

        FixtureCase largeValidCase = cases[0];
        FixtureCase exactLimitCase = cases[1];
        FixtureCase limitPlusOneCase = cases[2];
        if (largeValidCase.ExpectedOperation != "ParseValid"
            || largeValidCase.InputLengthCharacters <= guardLimitCharacters
            || exactLimitCase.InputLengthCharacters != guardLimitCharacters
            || exactLimitCase.ExpectedOperation != "ParseValid"
            || limitPlusOneCase.InputLengthCharacters != guardLimitCharacters + 1
            || limitPlusOneCase.ExpectedOperation != "RejectOversized"
            || !expected.GetProperty("validParseProducesEquivalentAst").GetBoolean()
            || !expected.GetProperty("oversizedInputIsRejectedAtConfiguredBoundary").GetBoolean()
            || !expected.GetProperty("parseAndRejectAreSeparateCases").GetBoolean()
            || expected.GetProperty("competitiveRatioAcrossOperationsAllowed").GetBoolean())
        {
            throw new InvalidOperationException($"BVAL-11 fixture boundary expectations are invalid: {FixtureRelativePath}.");
        }

        char repeated = repeatedCharacter[0];
        string largeValidInput = BuildInput(prefix, repeated, suffix, largeValidCase.InputLengthCharacters);
        string inputAtLimit = BuildInput(prefix, repeated, suffix, exactLimitCase.InputLengthCharacters);
        string inputAboveLimit = BuildInput(prefix, repeated, suffix, limitPlusOneCase.InputLengthCharacters);
        var compatibilityParser = new QueryParser("body", new KeywordAnalyser());
        var boundedParser = new QueryParser(
            "body",
            new KeywordAnalyser(),
            QueryParserOptions.Default with { MaxInputChars = guardLimitCharacters });

        Query largeValidAst = compatibilityParser.Parse(largeValidInput);
        Query compatibilityBoundaryAst = compatibilityParser.Parse(inputAtLimit);
        Query boundedBoundaryAst = boundedParser.Parse(inputAtLimit);
        if (largeValidAst is not TermQuery
            || compatibilityBoundaryAst is not TermQuery compatibilityTerm
            || boundedBoundaryAst is not TermQuery boundedTerm
            || !compatibilityBoundaryAst.Equals(boundedBoundaryAst))
        {
            throw new InvalidOperationException("BVAL-11 valid compatibility and bounded parses did not produce equivalent term-query ASTs.");
        }

        int? rejectionOffset = null;
        try
        {
            boundedParser.Parse(inputAboveLimit);
        }
        catch (QueryParseException exception)
        {
            rejectionOffset = exception.Offset;
        }

        if (rejectionOffset != guardLimitCharacters)
            throw new InvalidOperationException("BVAL-11 limit-plus-one input was not rejected at the configured character boundary.");

        object[] caseEvidence =
        [
            new { largeValidCase.Id, largeValidCase.InputLengthCharacters, largeValidCase.ExpectedOperation, ActualOperation = "ParseValid" },
            new
            {
                exactLimitCase.Id,
                exactLimitCase.InputLengthCharacters,
                exactLimitCase.ExpectedOperation,
                ActualOperation = "ParseValid",
                AstEquivalent = compatibilityBoundaryAst.Equals(boundedBoundaryAst),
                AstType = boundedBoundaryAst.GetType().Name,
                AstField = boundedTerm.Field,
                AstTermLength = boundedTerm.Term.Length
            },
            new
            {
                limitPlusOneCase.Id,
                limitPlusOneCase.InputLengthCharacters,
                limitPlusOneCase.ExpectedOperation,
                ActualOperation = "RejectOversized",
                RejectionOffset = rejectionOffset
            }
        ];
        WriteEvidence(new
        {
            FixtureId,
            FixtureRelativePath,
            FixtureSha256 = fixtureSha256,
            GuardLimitCharacters = guardLimitCharacters,
            ValidParseProducesEquivalentAst = true,
            ParseAndRejectAreSeparateCases = true,
            CompetitiveRatioAcrossOperationsAllowed = false,
            Cases = caseEvidence
        });

        return new QueryParserBoundaryFixture(
            fixtureSha256,
            guardLimitCharacters,
            largeValidInput,
            inputAtLimit,
            inputAboveLimit);
    }

    private static string BuildInput(string prefix, char repeatedCharacter, string suffix, int targetLength)
    {
        int repeatedCount = targetLength - prefix.Length - suffix.Length;
        if (repeatedCount < 0)
            throw new InvalidOperationException("BVAL-11 fixture input is shorter than its declared prefix and suffix.");
        return string.Concat(prefix, new string(repeatedCharacter, repeatedCount), suffix);
    }

    private static void WriteEvidence(object evidence)
    {
        string? artifactDirectory = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            throw new InvalidOperationException($"{ArtifactDirectoryVariable} is required for BVAL-11 evidence output.");

        string outputDirectory = Path.Combine(artifactDirectory, "parser");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "bval-11-parser-success-versus-rejection.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
    }

    private sealed record FixtureCase(string Id, int InputLengthCharacters, string ExpectedOperation);
}

/// <summary>Measures parsing time and allocations without including search execution.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QueryParserHotPathBenchmarks
{
    private QueryParser _parser = null!;
    private ComplexPhraseQueryParser _complexPhraseParser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new QueryParser("body", new StandardAnalyser());
        _complexPhraseParser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseSingleTerm() => _parser.Parse("quick");

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseBooleanQuery() => _parser.Parse("quick AND brown OR fox");

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseQuotedPhrase() => _parser.Parse("\"quick brown fox\"");

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseFlatComplexPhrase() =>
        _complexPhraseParser.Parse("\"quick (fast OR swift) brown\"");
}
