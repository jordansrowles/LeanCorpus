namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Result of a codec migration operation.
/// </summary>
public sealed record IndexCodecMigrationResult
{
    /// <summary>Gets a value indicating whether migration succeeded.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets a value indicating whether the call only reported planned actions.</summary>
    public required bool DryRun { get; init; }

    /// <summary>Gets the source index directory.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Gets the staging directory, when used.</summary>
    public string? StagingDirectory { get; init; }

    /// <summary>Gets actions executed or reported by the call.</summary>
    public required IReadOnlyList<IndexCodecMigrationAction> ExecutedActions { get; init; }

    /// <summary>Gets the post-migration validation result, when available.</summary>
    public IndexCheckResult? ValidationResult { get; init; }

    /// <summary>Gets migration issues.</summary>
    public required IReadOnlyList<IndexCheckIssue> Issues { get; init; }
}
