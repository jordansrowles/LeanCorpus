using Microsoft.Extensions.FileSystemGlobbing;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record AffectedBenchmarkSelection(
    IReadOnlyDictionary<string, IReadOnlyList<string>> BenchmarksByProject,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> MatchedGroups);

internal static class AffectedBenchmarkPlanner
{
    public static AffectedBenchmarkSelection Create(
        DevOpsConfiguration configuration,
        IEnumerable<string> changedPaths,
        string? area,
        string? group)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(changedPaths);

        var paths = changedPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Replace('\\', '/').TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var requestedAreas = SplitNames(area);
        var requestedGroups = SplitNames(group);
        var knownAreas = configuration.BenchmarkGroups.Values
            .Select(static item => item.Area)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownGroups = configuration.BenchmarkGroups.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownArea = requestedAreas.FirstOrDefault(name => !knownAreas.Contains(name));
        if (unknownArea is not null)
            throw new InvalidOperationException($"Unknown benchmark area '{unknownArea}'.");
        var unknownGroup = requestedGroups.FirstOrDefault(name => !knownGroups.Contains(name));
        if (unknownGroup is not null)
            throw new InvalidOperationException($"Unknown benchmark group '{unknownGroup}'.");

        var classesByProject = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var matchedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (groupName, groupConfiguration) in configuration.BenchmarkGroups.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if ((requestedAreas.Length > 0 && !requestedAreas.Contains(groupConfiguration.Area, StringComparer.OrdinalIgnoreCase)) ||
                (requestedGroups.Length > 0 && !requestedGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var matcher = new Matcher(StringComparison.Ordinal);
            foreach (var glob in groupConfiguration.Globs)
                matcher.AddInclude(glob.Replace('\\', '/'));

            if (!paths.Any(path => matcher.Match(path).HasMatches))
                continue;

            matchedGroups.Add(groupName);
            if (!classesByProject.TryGetValue(groupConfiguration.Project, out var classes))
            {
                classes = new HashSet<string>(StringComparer.Ordinal);
                classesByProject.Add(groupConfiguration.Project, classes);
            }

            classes.UnionWith(groupConfiguration.Benchmarks);
        }

        if (matchedGroups.Count == 0)
        {
            var selection = paths.Length == 0 ? "the working tree has no changed files" : "no configured benchmark group matched the changed files";
            throw new InvalidOperationException($"Refusing to run zero affected benchmarks because {selection}.");
        }

        var orderedClasses = classesByProject
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<string>)pair.Value.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return new AffectedBenchmarkSelection(
            orderedClasses,
            paths,
            matchedGroups.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    private static string[] SplitNames(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
