namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Persisted marker describing an in-progress or recently completed index migration.
/// </summary>
public sealed record IndexMigrationMarker
{
    /// <summary>Gets the marker state.</summary>
    public required IndexMigrationState State { get; init; }

    /// <summary>Gets the source index directory.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Gets the staging directory.</summary>
    public required string StagingDirectory { get; init; }

    /// <summary>Gets the source commit generation at migration start.</summary>
    public int? SourceCommitGeneration { get; init; }

    /// <summary>Gets the marker creation time.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets the marker update time.</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Gets the planned migration actions.</summary>
    public required IReadOnlyList<IndexCodecMigrationAction> PlannedActions { get; init; }
}
