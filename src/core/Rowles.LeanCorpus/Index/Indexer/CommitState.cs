namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Mutable holder for commit-related state shared between <see cref="IndexWriter"/>
/// and <see cref="CommitManager"/>.
/// </summary>
internal sealed class CommitState
{
    public long NextSequenceNumber;
    public long FlushSeqNoStart;
    public int NextSegmentOrdinal;
    public int CommitGeneration;
    public long ContentToken;
    public bool ContentChangedSinceCommit;

    public int PreparedGeneration = -1;
    public List<SegmentInfo>? PreparedSegments;
    public long PreparedContentToken;

    public readonly List<SegmentInfo> CommittedSegments = [];
    public readonly List<(string field, string term, bool isSoftDelete)> PendingDeletes = [];

    /// <summary>
    /// Files modified after this time are considered dirty for the next durable commit.
    /// Initialised to <see cref="DateTime.MinValue"/> so the first commit fsyncs every file.
    /// </summary>
    public DateTime LastCommitFsyncUtc = DateTime.MinValue;
}
