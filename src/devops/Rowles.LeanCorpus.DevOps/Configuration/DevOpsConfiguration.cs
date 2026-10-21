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
    string? DefaultFilter);

internal sealed record CodeAreaConfiguration(
    ImmutableArray<string> Globs,
    ImmutableArray<string> Targets);

internal sealed record BenchmarkStrategyConfiguration(
    string Description,
    int DocCount,
    ImmutableArray<string> Job);

internal sealed record BenchmarkGroupConfiguration(
    string Area,
    ImmutableArray<string> Globs,
    string Project,
    ImmutableArray<string> Benchmarks);

internal sealed record DevOpsConfiguration(
    ImmutableDictionary<string, TestSuiteConfiguration> TestSuites,
    ImmutableDictionary<string, CodeAreaConfiguration> CodeAreas,
    ImmutableDictionary<string, string> BenchmarkSuites,
    ImmutableDictionary<string, BenchmarkStrategyConfiguration> BenchmarkStrategies,
    ImmutableDictionary<string, ImmutableArray<string>> BenchmarkDatasets,
    ImmutableDictionary<string, BenchmarkGroupConfiguration> BenchmarkGroups);
