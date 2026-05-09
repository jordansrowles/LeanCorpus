namespace Rowles.LeanLucene.Analysis;

/// <summary>
/// Thrown when <see cref="TokenBudgetPolicy.Reject"/> is active and a document exceeds the token budget.
/// </summary>
public sealed class TokenBudgetExceededException : InvalidOperationException
{
    /// <summary>
    /// Gets the number of tokens the document produced.
    /// </summary>
    public int TokenCount { get; }

    /// <summary>
    /// Gets the maximum token budget that was exceeded.
    /// </summary>
    public int Budget { get; }

    /// <summary>
    /// Initialises a new <see cref="TokenBudgetExceededException"/> with the actual token count and configured budget.
    /// </summary>
    /// <param name="tokenCount">The number of tokens the document produced.</param>
    /// <param name="budget">The maximum allowed token count.</param>
    public TokenBudgetExceededException(int tokenCount, int budget)
        : base($"Document produced {tokenCount} tokens, exceeding the budget of {budget}.")
    {
        TokenCount = tokenCount;
        Budget = budget;
    }
}
