using System.Text.Json;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using GitRepository = LibGit2Sharp.Repository;

namespace Rowles.LeanCorpus.DevOps.Repository;

internal sealed record SolutionInventoryValidationResult(
    bool IsValid,
    int TrackedProjectCount,
    int MasterProjectCount,
    int FilterCount,
    IReadOnlyList<string> Diagnostics);

internal sealed class SolutionInventoryValidator
{
    public async Task<SolutionInventoryValidationResult> ValidateAsync(
        CheckoutIdentity checkout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        try
        {
            return await ValidateAsync(checkout, ReadProjectPaths(checkout), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is LibGit2Sharp.LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Invalid([], 0, 0, 0, $"Could not enumerate tracked projects for solution validation: {exception.Message}");
        }
    }

    internal async Task<SolutionInventoryValidationResult> ValidateAsync(
        CheckoutIdentity checkout,
        IReadOnlyCollection<string> trackedProjectPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(trackedProjectPaths);

        var root = Path.GetFullPath(checkout.RepositoryRoot);
        var solutionPath = RepositoryPaths.GetSolutionPath(root);
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        if (!File.Exists(solutionPath))
        {
            return Invalid(diagnostics, trackedProjectPaths.Count, 0, 0,
                $"The canonical master solution is missing: '{solutionPath}'.");
        }

        var tracked = CreatePathInventory(trackedProjectPaths, "tracked project", diagnostics);
        var masterEntries = new List<string>();
        try
        {
            var model = await SolutionSerializers.SlnXml.OpenAsync(solutionPath, cancellationToken).ConfigureAwait(false);
            var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
            foreach (var project in model.SolutionProjects)
            {
                var relativePath = project.FilePath.Replace('\\', '/');
                if (!TryResolveRelativePath(root, solutionDirectory, relativePath, out var fullPath, out var relativeToRoot, out var pathError))
                {
                    diagnostics.Add($"Master solution project '{project.FilePath}' {pathError}.");
                    continue;
                }

                if (!relativeToRoot.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"Master solution entry '{project.FilePath}' is not a .csproj project.");
                    continue;
                }

                masterEntries.Add(relativeToRoot);
                if (!File.Exists(fullPath))
                {
                    diagnostics.Add($"Master solution project '{relativeToRoot}' is missing from the working tree.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or SolutionException)
        {
            var detail = exception.Message.Contains("Duplicate item", StringComparison.OrdinalIgnoreCase)
                ? $"The canonical master solution contains a duplicate project entry: {exception.Message}"
                : $"The canonical master solution could not be read: {exception.Message}";
            diagnostics.Add(detail);
        }

        var master = CreatePathInventory(masterEntries, "master solution project", diagnostics);
        foreach (var trackedPath in tracked.ExactPaths.Order(StringComparer.Ordinal))
        {
            if (master.ExactPaths.Contains(trackedPath))
            {
                continue;
            }

            if (master.CaseInsensitivePaths.TryGetValue(trackedPath, out var masterPath))
            {
                diagnostics.Add($"Master solution project path case mismatch: tracked '{trackedPath}', listed '{masterPath}'.");
            }
            else
            {
                diagnostics.Add($"Tracked project '{trackedPath}' is missing from the master solution.");
            }
        }

        foreach (var masterPath in master.ExactPaths.Order(StringComparer.Ordinal))
        {
            if (tracked.ExactPaths.Contains(masterPath))
            {
                continue;
            }

            if (!tracked.CaseInsensitivePaths.ContainsKey(masterPath))
            {
                diagnostics.Add($"Master solution project '{masterPath}' is not present in the tracked project inventory.");
            }
        }

        var masterDirectory = Path.GetDirectoryName(solutionPath)!;
        var filtersDirectory = Path.Combine(root, "src", "solutions");
        var filterPaths = Directory.Exists(filtersDirectory)
            ? Directory.EnumerateFiles(filtersDirectory, "*.slnf", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray()
            : [];
        var masterExactPaths = master.ExactPaths;
        var masterCaseInsensitivePaths = master.CaseInsensitivePaths;
        foreach (var filterPath in filterPaths)
        {
            ValidateFilter(root, solutionPath, masterDirectory, filterPath, masterExactPaths, masterCaseInsensitivePaths, diagnostics, cancellationToken);
        }

        return new SolutionInventoryValidationResult(
            diagnostics.Count == 0,
            tracked.ExactPaths.Count,
            masterEntries.Count,
            filterPaths.Length,
            diagnostics.ToArray());
    }

    private static PathInventory CreatePathInventory(IEnumerable<string> paths, string description, SortedSet<string> diagnostics)
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in paths)
        {
            var path = rawPath.Replace('\\', '/');
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            if (!path.StartsWith("src/", StringComparison.Ordinal) || !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                HasGeneratedDirectory(path))
            {
                continue;
            }

            if (!exact.Add(path))
            {
                diagnostics.Add($"Duplicate {description} path '{path}'.");
            }
            else if (!caseInsensitive.TryAdd(path, path))
            {
                diagnostics.Add($"Case-colliding {description} paths '{caseInsensitive[path]}' and '{path}'.");
            }
        }

        return new PathInventory(exact, caseInsensitive);
    }

    private static bool HasGeneratedDirectory(string path)
        => path.Split('/').Any(static part => part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) || part.Equals("artifacts", StringComparison.OrdinalIgnoreCase));

    private static void ValidateFilter(
        string root,
        string solutionPath,
        string masterDirectory,
        string filterPath,
        IReadOnlySet<string> masterPaths,
        IReadOnlyDictionary<string, string> masterPathsIgnoringCase,
        SortedSet<string> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filterName = Path.GetRelativePath(root, filterPath).Replace(Path.DirectorySeparatorChar, '/');
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(filterPath));
            var rootElement = document.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object ||
                !rootElement.TryGetProperty("solution", out var solution) || solution.ValueKind != JsonValueKind.Object ||
                !solution.TryGetProperty("path", out var masterPathElement) || masterPathElement.ValueKind != JsonValueKind.String ||
                !solution.TryGetProperty("projects", out var projectsElement) || projectsElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Solution filter '{filterName}' must contain solution.path and a solution.projects array.");
                return;
            }

            var rawMasterPath = masterPathElement.GetString();
            if (string.IsNullOrWhiteSpace(rawMasterPath))
            {
                diagnostics.Add($"Solution filter '{filterName}' has an invalid master path.");
            }
            else if (!TryResolveRelativePath(root, Path.GetDirectoryName(filterPath)!, rawMasterPath, out _, out var resolvedMasterPath, out var masterPathError))
            {
                diagnostics.Add($"Solution filter '{filterName}' has an invalid master path: {masterPathError}.");
            }
            else
            {
                var expectedMasterPath = Path.GetRelativePath(root, solutionPath).Replace(Path.DirectorySeparatorChar, '/');
                if (!string.Equals(resolvedMasterPath, expectedMasterPath, StringComparison.Ordinal))
                {
                    if (string.Equals(resolvedMasterPath, expectedMasterPath, StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostics.Add($"Solution filter '{filterName}' has a case mismatch in its canonical master solution path: '{rawMasterPath}'.");
                    }
                    else
                    {
                        diagnostics.Add($"Solution filter '{filterName}' must reference the canonical master solution '{expectedMasterPath}'.");
                    }
                }
            }

            var listed = new HashSet<string>(StringComparer.Ordinal);
            var listedIgnoringCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var projectElement in projectsElement.EnumerateArray())
            {
                if (projectElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(projectElement.GetString()))
                {
                    diagnostics.Add($"Solution filter '{filterName}' contains a project entry that is not a non-empty string.");
                    continue;
                }

                var rawProjectPath = projectElement.GetString()!;
                if (!TryResolveRelativePath(root, masterDirectory, rawProjectPath, out _, out var projectPath, out var projectPathError))
                {
                    diagnostics.Add($"Solution filter '{filterName}' project '{rawProjectPath}' {projectPathError}.");
                    continue;
                }

                if (!listed.Add(projectPath) || !listedIgnoringCase.Add(projectPath))
                {
                    diagnostics.Add($"Solution filter '{filterName}' contains duplicate project '{projectPath}'.");
                    continue;
                }

                if (!masterPaths.Contains(projectPath))
                {
                    if (masterPathsIgnoringCase.TryGetValue(projectPath, out var masterProjectPath))
                    {
                        diagnostics.Add($"Solution filter '{filterName}' project path case mismatch: listed '{projectPath}', master '{masterProjectPath}'.");
                    }
                    else
                    {
                        diagnostics.Add($"Solution filter '{filterName}' project '{projectPath}' is not in the master solution.");
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Solution filter '{filterName}' is invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Solution filter '{filterName}' could not be read: {exception.Message}");
        }
    }

    private static bool TryResolveRelativePath(
        string repositoryRoot,
        string baseDirectory,
        string rawPath,
        out string fullPath,
        out string relativeToRoot,
        out string error)
    {
        fullPath = string.Empty;
        relativeToRoot = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath) || Path.IsPathRooted(rawPath) ||
            rawPath.StartsWith('\\') || rawPath.StartsWith('/') ||
            (rawPath.Length >= 2 && char.IsAsciiLetter(rawPath[0]) && rawPath[1] == ':'))
        {
            error = "must be a relative path";
            return false;
        }

        try
        {
            var portablePath = rawPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(Path.Combine(baseDirectory, portablePath));
            relativeToRoot = Path.GetRelativePath(repositoryRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (relativeToRoot == ".." || relativeToRoot.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
            {
                error = "resolves outside the repository";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"is not a valid path: {exception.Message}";
            return false;
        }
    }

    private static string[] ReadProjectPaths(CheckoutIdentity checkout)
    {
        var root = Path.GetFullPath(checkout.RepositoryRoot);
        if (checkout.GitMetadataPath is not null)
        {
            using var repository = new GitRepository(root);
            return repository.Index
                .Select(static entry => entry.Path.Replace('\\', '/'))
                .Where(static path => path.StartsWith("src/", StringComparison.Ordinal) &&
                    path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !HasGeneratedDirectory(path))
                .ToArray();
        }

        var sourceRoot = Path.Combine(root, "src");
        if (!Directory.Exists(sourceRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(static path => !HasGeneratedDirectory(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static SolutionInventoryValidationResult Invalid(
        IEnumerable<string> previousDiagnostics,
        int trackedProjectCount,
        int masterProjectCount,
        int filterCount,
        string diagnostic)
    {
        var diagnostics = previousDiagnostics.Append(diagnostic).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new SolutionInventoryValidationResult(false, trackedProjectCount, masterProjectCount, filterCount, diagnostics);
    }

    private sealed record PathInventory(HashSet<string> ExactPaths, Dictionary<string, string> CaseInsensitivePaths);
}
