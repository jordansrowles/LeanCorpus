namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Structured validation issue returned by <see cref="IndexValidator.Check(Store.MMapDirectory, IndexCheckOptions?)"/>.
/// </summary>
public sealed record IndexCheckIssue
{
    /// <summary>Gets the issue severity.</summary>
    public required IndexCheckSeverity Severity { get; init; }

    /// <summary>Gets the stable issue code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the issue message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the related file name, when the issue is file-specific.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets the related segment ID, when the issue is segment-specific.</summary>
    public string? SegmentId { get; init; }

    /// <summary>Gets a value indicating whether the issue can be repaired by future repair tooling.</summary>
    public bool IsRepairable { get; init; }

    /// <summary>Gets suggested repair or recovery actions for this issue.</summary>
    public IReadOnlyList<string> SuggestedActions { get; init; } = [];

    /// <inheritdoc />
    public override string ToString()
        => $"{Severity} {Code} {SegmentId ?? "-"} {FileName ?? "-"} {Message}";
}
