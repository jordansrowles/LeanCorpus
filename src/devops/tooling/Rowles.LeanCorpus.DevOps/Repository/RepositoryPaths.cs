namespace Rowles.LeanCorpus.DevOps.Repository;

internal static class RepositoryPaths
{
    public const string SolutionFileName = "Rowles.LeanCorpus.slnx";
    public const string SolutionRelativePath = "src/Rowles.LeanCorpus.slnx";
    private const string SdkMarker = "global.json";
    private const string BuildPropsMarker = "Directory.Build.props";

    public static string GetSolutionPath(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Path.Combine(Path.GetFullPath(repositoryRoot), SolutionRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static CheckoutIdentity Resolve(string? explicitRepositoryPath, string? workingDirectory, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitRepositoryPath))
        {
            var explicitlySelected = ResolveFromDirectory(explicitRepositoryPath);
            if (explicitlySelected is null)
            {
                throw new InvalidOperationException(
                    $"The explicit repository path '{Path.GetFullPath(explicitRepositoryPath)}' is not a LeanCorpus checkout. " +
                    $"A checkout must contain {SdkMarker}, {BuildPropsMarker} and {SolutionRelativePath} markers.");
            }

            return explicitlySelected;
        }

        var callerCheckout = ResolveFromDirectory(workingDirectory);
        var executableCheckout = ResolveFromExecutable(executablePath);
        if (callerCheckout is not null && executableCheckout is not null &&
            !PathsEqual(callerCheckout.RepositoryRoot, executableCheckout.RepositoryRoot))
        {
            throw new InvalidOperationException(
                "The caller working directory and DevOps executable resolve to different LeanCorpus checkouts. " +
                $"Caller: '{callerCheckout.RepositoryRoot}'. Executable: '{executableCheckout.RepositoryRoot}'. " +
                "Select an explicit repository path to continue.");
        }

        return callerCheckout ?? executableCheckout ?? throw new InvalidOperationException(
            "Could not locate a LeanCorpus checkout from the caller working directory or DevOps executable. " +
            $"A checkout must contain {SdkMarker}, {BuildPropsMarker} and {SolutionRelativePath} markers. Select an explicit repository path.");
    }

    private static CheckoutIdentity? ResolveFromExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(executablePath);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        return ResolveFromDirectory(directory);
    }

    private static CheckoutIdentity? ResolveFromDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        var current = new DirectoryInfo(fullPath);
        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            var solutionPath = GetSolutionPath(current.FullName);
            var sdkPath = Path.Combine(current.FullName, SdkMarker);
            var buildPropsPath = Path.Combine(current.FullName, BuildPropsMarker);
            var gitKind = Directory.Exists(gitPath)
                ? CheckoutGitMarkerKind.Directory
                : IsGitFile(gitPath)
                    ? CheckoutGitMarkerKind.File
                    : (CheckoutGitMarkerKind?)null;

            if (File.Exists(solutionPath) && File.Exists(sdkPath) && File.Exists(buildPropsPath))
            {
                return new CheckoutIdentity(current.FullName, gitKind is null ? null : gitPath, gitKind);
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsGitFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(path);
            var firstLine = reader.ReadLine();
            return firstLine is not null && firstLine.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(firstLine["gitdir:".Length..]);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
