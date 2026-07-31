namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Determines what happens when a document exceeds its configured token limit.
/// </summary>
public enum TokenBudgetPolicy
{
    /// <summary>Silently discard tokens beyond the limit.</summary>
    Truncate,

    /// <summary>Log a warning and continue indexing with all tokens.</summary>
    Warn,

    /// <summary>Throw an <see cref="TokenBudgetExceededException"/> to reject the document.</summary>
    Reject
}
