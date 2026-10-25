namespace Rowles.LeanCorpus.DevOps.Documentation;

internal static class RepositoryWebLinks
{
    public const string RepositoryUrl = "https://github.com/jordansrowles/LeanCorpus";

    public static string CommitUrl(string commit)
        => $"{RepositoryUrl}/commit/{NormalizeCommit(commit)}";

    public static string BlobUrlPrefix(string commit)
        => $"{RepositoryUrl}/blob/{NormalizeCommit(commit)}/";

    public static string BlobUrl(string commit, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return BlobUrlPrefix(commit) + relativePath;
    }

    public static string TreeUrl(string commit, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return $"{RepositoryUrl}/tree/{NormalizeCommit(commit)}/{relativePath}";
    }

    private static string NormalizeCommit(string commit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        return commit.ToLowerInvariant();
    }
}
