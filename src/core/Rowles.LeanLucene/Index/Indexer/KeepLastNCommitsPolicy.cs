namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>Keeps the last N commit generations, deleting older ones.</summary>
public sealed class KeepLastNCommitsPolicy : IIndexDeletionPolicy
{
    private readonly int _maxCommits;

    /// <summary>
    /// Initialises a new policy that retains the last <paramref name="maxCommits"/> commit generations.
    /// </summary>
    /// <param name="maxCommits">The number of recent commit generations to keep. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxCommits"/> is less than 1.</exception>
    public KeepLastNCommitsPolicy(int maxCommits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCommits, 1);
        _maxCommits = maxCommits;
    }

    /// <inheritdoc/>
    public void OnCommit(string directoryPath, int currentGeneration)
        => OnCommit(directoryPath, currentGeneration, new HashSet<string>(StringComparer.Ordinal));

    /// <inheritdoc/>
    public void OnCommit(string directoryPath, int currentGeneration, IReadOnlySet<string> protectedSegmentIds)
    {
        int threshold = currentGeneration - _maxCommits;
        if (threshold <= 0) return;

        foreach (var file in Directory.GetFiles(directoryPath, "segments_*"))
        {
            var name = Path.GetFileName(file);
            if (!CommitDeletionPolicy.TryParseCommitGeneration(name, out int gen) || gen > threshold)
                continue;

            if (CommitDeletionPolicy.ReferencesProtectedSegment(file, protectedSegmentIds))
                continue;

            File.Delete(file);
        }

        // Prune old stats files
        foreach (var file in Directory.GetFiles(directoryPath, "stats_*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!CommitDeletionPolicy.TryParseStatsGeneration(name, out int gen) || gen > threshold)
                continue;

            var commitFile = Path.Combine(directoryPath, $"segments_{gen}");
            if (CommitDeletionPolicy.ReferencesProtectedSegment(commitFile, protectedSegmentIds))
                continue;

            File.Delete(file);
        }
    }
}
