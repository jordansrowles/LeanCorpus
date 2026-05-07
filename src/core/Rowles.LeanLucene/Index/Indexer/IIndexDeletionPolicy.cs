namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Controls which old commit files and their referenced segments are pruned after a new commit.
/// </summary>
public interface IIndexDeletionPolicy
{
    /// <summary>
    /// Called after a new commit is written. The implementation should delete
    /// any commit files (and their segments) that are no longer needed.
    /// </summary>
    /// <param name="directoryPath">The index directory containing commit files.</param>
    /// <param name="currentGeneration">The generation that has just been committed.</param>
    void OnCommit(string directoryPath, int currentGeneration);

    /// <summary>
    /// Called after a new commit is written, with the set of segment IDs protected by held snapshots.
    /// </summary>
    /// <param name="directoryPath">The index directory containing commit files.</param>
    /// <param name="currentGeneration">The generation that has just been committed.</param>
    /// <param name="protectedSegmentIds">Segment IDs that must remain reachable while snapshots are held.</param>
    void OnCommit(string directoryPath, int currentGeneration, IReadOnlySet<string> protectedSegmentIds)
        => OnCommit(directoryPath, currentGeneration);
}
