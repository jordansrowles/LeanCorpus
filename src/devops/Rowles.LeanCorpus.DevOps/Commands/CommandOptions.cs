namespace Rowles.LeanCorpus.DevOps.Commands;

internal sealed record BuildCommandOptions(string Configuration, string? Project, string? Framework, string? RepositoryPath = null);

internal sealed record CleanCommandOptions(string Target, string? RepositoryPath = null);

internal sealed record ServerStartCommandOptions(
    string Framework,
    string Configuration,
    bool NoBuild,
    bool NoRestore,
    bool External,
    IReadOnlyList<string> ApplicationArguments,
    string? RepositoryPath = null);

internal sealed record ReportCommandOptions(string Group, int Top, string? Path, bool Strict, bool Json, string? RepositoryPath = null);
