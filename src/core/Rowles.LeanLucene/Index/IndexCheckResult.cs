namespace Rowles.LeanLucene.Index;

/// <summary>
/// Result of an <see cref="IndexValidator.Check(Store.MMapDirectory, IndexCheckOptions?)"/> call.
/// </summary>
public sealed class IndexCheckResult
{
    private readonly List<IndexCheckIssue> _detailedIssues = [];
    private readonly List<string> _issues = [];

    /// <summary>Gets a value indicating whether the index has no error-severity issues.</summary>
    public bool IsHealthy => !_detailedIssues.Any(static issue => issue.Severity == IndexCheckSeverity.Error);

    /// <summary>Gets validation issue messages for compatibility with the original validator API.</summary>
    public IReadOnlyList<string> Issues => _issues;

    /// <summary>Gets structured validation issues.</summary>
    public IReadOnlyList<IndexCheckIssue> DetailedIssues => _detailedIssues;

    /// <summary>Gets the number of segments that were checked.</summary>
    public int SegmentsChecked { get; internal set; }

    /// <summary>Gets the total number of documents checked across all segments.</summary>
    public int DocumentsChecked { get; internal set; }

    /// <summary>Gets the number of files that were checked.</summary>
    public int FilesChecked { get; internal set; }

    /// <summary>Gets the commit generation that was checked, or <c>null</c> when no commit was available.</summary>
    public int? CommitGeneration { get; internal set; }

    internal void AddIssue(string issue)
        => AddIssue(IndexCheckSeverity.Error, IndexCheckIssueCodes.LegacyIssue, issue, null, null, false);

    internal void AddIssue(
        IndexCheckSeverity severity,
        string code,
        string message,
        string? fileName,
        string? segmentId,
        bool isRepairable,
        IReadOnlyList<string>? suggestedActions = null)
    {
        var issue = new IndexCheckIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            FileName = fileName,
            SegmentId = segmentId,
            IsRepairable = isRepairable,
            SuggestedActions = suggestedActions ?? IndexRepairRecommendations.ForIssue(code)
        };

        _detailedIssues.Add(issue);
        _issues.Add(issue.ToString());
    }
}
