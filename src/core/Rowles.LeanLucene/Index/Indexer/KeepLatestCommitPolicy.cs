namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>Keeps only the latest commit, deleting all older segments_N and stats_N files.</summary>
public sealed class KeepLatestCommitPolicy : IIndexDeletionPolicy
{
    /// <inheritdoc/>
    public void OnCommit(string directoryPath, int currentGeneration)
        => OnCommit(directoryPath, currentGeneration, new HashSet<string>(StringComparer.Ordinal));

    /// <inheritdoc/>
    public void OnCommit(string directoryPath, int currentGeneration, IReadOnlySet<string> protectedSegmentIds)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "segments_*"))
        {
            var name = Path.GetFileName(file);
            if (!CommitDeletionPolicy.TryParseCommitGeneration(name, out int gen) || gen >= currentGeneration)
                continue;

            if (CommitDeletionPolicy.ReferencesProtectedSegment(file, protectedSegmentIds))
                continue;

            File.Delete(file);
        }

        // Prune old stats files
        foreach (var file in Directory.GetFiles(directoryPath, "stats_*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file); // "stats_N"
            if (!CommitDeletionPolicy.TryParseStatsGeneration(name, out int gen) || gen >= currentGeneration)
                continue;

            var commitFile = Path.Combine(directoryPath, $"segments_{gen}");
            if (CommitDeletionPolicy.ReferencesProtectedSegment(commitFile, protectedSegmentIds))
                continue;

            File.Delete(file);
        }
    }
}
