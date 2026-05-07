namespace Rowles.LeanLucene.Index;

/// <summary>
/// Result of an <see cref="IndexValidator.Validate"/> call.
/// </summary>
public sealed class IndexCheckResult
{
    /// <summary>Gets a value indicating whether the index is healthy (no issues found).</summary>
    public bool IsHealthy => Issues.Count == 0;

    /// <summary>Gets the list of issues found during validation.</summary>
    public IReadOnlyList<string> Issues => _issues;

    /// <summary>Gets the number of segments that were checked.</summary>
    public int SegmentsChecked { get; internal set; }

    /// <summary>Gets the total number of documents checked across all segments.</summary>
    public int DocumentsChecked { get; internal set; }

    private readonly List<string> _issues = [];
    internal void AddIssue(string issue) => _issues.Add(issue);
}
