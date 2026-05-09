namespace Rowles.LeanLucene.Analysis;

/// <summary>
/// Determines what happens when a document exceeds <see cref="Index.Indexer.IndexWriterConfig.MaxTokensPerDocument"/>.
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
