namespace Rowles.LeanCorpus.DevOps.Commands;

internal sealed record BuildCommandOptions(string Configuration, string? Project, string? Framework, string? RepositoryPath = null);

internal sealed record CleanCommandOptions(string Target, string? RepositoryPath = null);

internal sealed record DocsCommandOptions(
    string Action,
    bool SkipBenchmarks,
    bool SkipCoverage,
    string Framework = "net10.0",
    bool NoRestore = false,
    string? RepositoryPath = null);

internal sealed record ServerStartCommandOptions(
    string Framework,
    string Configuration,
    bool NoBuild,
    bool NoRestore,
    bool External,
    IReadOnlyList<string> ApplicationArguments,
    string? RepositoryPath = null);

internal sealed record ReportCommandOptions(string Group, int Top, string? Path, bool Strict, bool Json, string? RepositoryPath = null);

internal sealed record CoverageCommandOptions(
    string Framework,
    string Configuration,
    string Suite,
    bool Clean,
    bool IncludePerformance,
    bool GenerateReport,
    string? RepositoryPath = null);

internal sealed record PackCommandOptions(
    string Configuration,
    bool CompressionOnly,
    bool NoBuild,
    bool Clean,
    bool ValidatePackages,
    bool ValidateCompression,
    string? RepositoryPath = null);

internal sealed record DataCommandOptions(
    string Dataset,
    int BookCount,
    string? OutputDirectory,
    string PackageSource,
    string? RepositoryPath = null);

internal sealed record DataForgeCommandOptions(
    IReadOnlyList<string> Arguments,
    string? RepositoryPath = null);

internal sealed record DiagnosticsCommandOptions(
    IReadOnlyList<string> Arguments,
    string? RepositoryPath = null);

internal sealed record BenchmarkCommandOptions(
    string Suite,
    string Strategy,
    string Framework,
    int DocumentCount,
    string Dataset,
    string? ReferencePath,
    string? Area,
    string? Group,
    string? SourceCommit,
    string? SourceRef,
    string? SourceManifest,
    bool Controlled,
    bool CorpusOnly,
    bool GcDump,
    bool PrepareData,
    int BookCount,
    bool Dry,
    bool List,
    IReadOnlyList<string> Arguments,
    string? RepositoryPath = null);

internal sealed record BenchmarkRemoteCommandOptions(
    string Action,
    string? Host,
    string? RemotePath,
    string? Branch,
    string? SessionName,
    Guid? RunId,
    IReadOnlyList<string> BenchmarkArguments,
    string? RepositoryPath = null);
