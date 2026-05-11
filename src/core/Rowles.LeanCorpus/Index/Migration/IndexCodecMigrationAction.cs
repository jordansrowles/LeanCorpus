namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Describes one planned codec migration action.
/// </summary>
public sealed record IndexCodecMigrationAction
{
    /// <summary>Gets the action kind.</summary>
    public required IndexCodecMigrationActionKind Kind { get; init; }

    /// <summary>Gets the source file path.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Gets the target file path, when the action writes to a separate path.</summary>
    public string? TargetPath { get; init; }

    /// <summary>Gets the human-readable action description.</summary>
    public required string Description { get; init; }

    /// <summary>Gets a value indicating whether the action can currently be executed.</summary>
    public required bool CanExecute { get; init; }

    /// <summary>Gets the reason the action cannot execute, when applicable.</summary>
    public string? ReasonCannotExecute { get; init; }

    /// <summary>Gets the related segment ID, when known.</summary>
    public string? SegmentId { get; init; }

    /// <summary>Gets the related file name, when known.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets the source codec version, when known.</summary>
    public byte? FromVersion { get; init; }

    /// <summary>Gets the target codec version, when known.</summary>
    public byte? ToVersion { get; init; }
}
