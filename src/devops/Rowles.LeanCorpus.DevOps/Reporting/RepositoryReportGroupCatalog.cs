namespace Rowles.LeanCorpus.DevOps.Reporting;

internal enum RepositoryReportGroup
{
    All,
    Git,
    Files,
    Code
}

internal static class RepositoryReportGroupCatalog
{
    public static IReadOnlyList<string> Names { get; } = ["all", "git", "files", "code"];

    public static bool TryParse(string value, out RepositoryReportGroup group)
    {
        group = value.Trim().ToLowerInvariant() switch
        {
            "all" => RepositoryReportGroup.All,
            "git" => RepositoryReportGroup.Git,
            "files" => RepositoryReportGroup.Files,
            "code" => RepositoryReportGroup.Code,
            _ => (RepositoryReportGroup)(-1)
        };
        return Enum.IsDefined(group);
    }

    public static string GetName(RepositoryReportGroup group) => group switch
    {
        RepositoryReportGroup.All => "all",
        RepositoryReportGroup.Git => "git",
        RepositoryReportGroup.Files => "files",
        RepositoryReportGroup.Code => "code",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown repository report group.")
    };
}
