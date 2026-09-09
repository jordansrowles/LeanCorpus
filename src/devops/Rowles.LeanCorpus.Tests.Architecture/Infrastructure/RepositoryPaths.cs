namespace Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

internal static class RepositoryPaths
{
    internal static readonly string Root = FindRoot();

    internal static string FromRoot(params string[] segments)
    {
        string path = Root;
        foreach (string segment in segments)
            path = Path.Combine(path, segment);

        return path;
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rowles.LeanCorpus.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LeanCorpus repository root.");
    }
}
