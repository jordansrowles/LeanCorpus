using System.Collections.Immutable;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal sealed record TestSuiteConfiguration(
    string Name,
    string Project,
    string Runner,
    ImmutableArray<string> Frameworks,
    string? DefaultFramework,
    bool Coverage,
    ImmutableArray<string> Capabilities,
    bool? IncludeInAll,
    bool? ExpandFrameworksByDefault,
    string? DefaultFilter,
    int? Order = null,
    bool DataForgeQualification = false)
{
    public bool RequiresBrowser { get; init; }
}

internal sealed record CodeAreaConfiguration(
    ImmutableArray<string> Globs,
    ImmutableArray<string> Targets);

internal sealed record BenchmarkStrategyConfiguration(
    string Description,
    int DocCount,
    ImmutableArray<string> Job);

internal sealed record BenchmarkProjectConfiguration(
    string DisplayName,
    string Project,
    ImmutableArray<string> Frameworks,
    ImmutableArray<string> Capabilities,
    bool IncludeInAll,
    bool IncludeWithExplicit);

internal sealed record BenchmarkSuiteConfiguration(
    string Description,
    string DisplayName,
    string Project,
    ImmutableArray<string> BenchmarkClasses,
    ImmutableArray<string> Aliases,
    ImmutableArray<string> Frameworks,
    ImmutableArray<string> Datasets,
    bool ExplicitOnly,
    bool ControlledCorpusOnly,
    bool CorpusOnly,
    bool RequiresGutenbergData,
    bool IsSelector,
    bool? IncludeInAll,
    bool? IncludeWithExplicit);

internal sealed record BenchmarkGroupConfiguration(
    string Area,
    ImmutableArray<string> Globs,
    string Project,
    ImmutableArray<string> Benchmarks);

internal sealed record DataForgeProfileRequirement(string Id, int Version);

internal sealed record BenchmarkMetricConfiguration(string Name, string Unit, string Role);

internal sealed record BenchmarkCaseConfiguration(
    string Id,
    string Project,
    string BenchmarkClass,
    ImmutableArray<string> BenchmarkMethods,
    ImmutableArray<string> MethodAliases,
    ImmutableDictionary<string, ImmutableArray<string>> ParameterValues,
    ImmutableDictionary<string, string> SemanticParameters,
    string EngineId,
    string Category,
    string Operation,
    string DatasetProfile,
    string WorkloadId,
    string AlgorithmFamily,
    string CachePolicy,
    string LifecyclePolicy,
    string? ComparisonGroup,
    string Classification,
    string ResultContract,
    string QualityProof,
    ImmutableArray<BenchmarkMetricConfiguration> Metrics,
    string EstimatedBudget);

internal sealed record BenchmarkCaseExclusionConfiguration(
    string Project,
    string BenchmarkClass,
    string BenchmarkMethod,
    ImmutableDictionary<string, ImmutableArray<string>> ParameterValues,
    string Reason,
    DateOnly ExpiresOn);

internal sealed record BenchmarkCaseManifestConfiguration(
    ImmutableArray<BenchmarkCaseConfiguration> CaseFamilies,
    ImmutableArray<BenchmarkCaseExclusionConfiguration> Exclusions);

internal sealed record BenchmarkCorrectnessAuditEntryConfiguration(
    string Id,
    string Priority,
    string Status,
    string ClaimClass,
    bool CompetitiveClaimAllowed,
    string SourceFile,
    string Symbol,
    string Defect,
    string RequiredProof,
    string FixtureId,
    string FixturePath,
    string FixtureSha256,
    string FixtureState);

internal sealed record BenchmarkCorrectnessAuditConfiguration(
    int SchemaVersion,
    int SpecRevision,
    string AuditedBranch,
    string AuditedCommit,
    bool AuditedTreeDirty,
    string AuditState,
    ImmutableArray<BenchmarkCorrectnessAuditEntryConfiguration> Gates);

internal sealed record DevOpsConfiguration(
    ImmutableDictionary<string, TestSuiteConfiguration> TestSuites,
    ImmutableDictionary<string, CodeAreaConfiguration> CodeAreas,
    ImmutableDictionary<string, BenchmarkProjectConfiguration> BenchmarkProjects,
    ImmutableDictionary<string, BenchmarkSuiteConfiguration> BenchmarkSuites,
    ImmutableDictionary<string, BenchmarkStrategyConfiguration> BenchmarkStrategies,
    ImmutableDictionary<string, ImmutableArray<string>> BenchmarkDatasets,
    ImmutableDictionary<string, BenchmarkGroupConfiguration> BenchmarkGroups)
{
    public string DefaultBenchmarkDataset { get; init; } = "synthetic";

    public ImmutableArray<string> TestAreas { get; init; } = [];

    public ImmutableDictionary<string, string> HistoricalBenchmarkSuiteLabels { get; init; }
        = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public ImmutableArray<DataForgeProfileRequirement> RequiredDataForgeSyntheticProfiles { get; init; } = [];

    public ImmutableArray<BenchmarkCaseConfiguration> BenchmarkCases { get; init; } = [];

    public ImmutableArray<BenchmarkCaseExclusionConfiguration> BenchmarkCaseExclusions { get; init; } = [];

    public BenchmarkCorrectnessAuditConfiguration BenchmarkCorrectnessAudit { get; init; }
        = new(0, 0, string.Empty, string.Empty, false, string.Empty, []);
}
