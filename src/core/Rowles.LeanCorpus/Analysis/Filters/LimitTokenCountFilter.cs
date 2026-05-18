namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Truncates the token stream after a fixed number of emitted tokens.
/// </summary>
public sealed class LimitTokenCountFilter : ITokenFilter
{
    private readonly int _maxTokenCount;

    /// <summary>
    /// Initialises a new <see cref="LimitTokenCountFilter"/>.
    /// </summary>
    /// <param name="maxTokenCount">Maximum number of tokens to retain.</param>
    public LimitTokenCountFilter(int maxTokenCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokenCount, 1);
        _maxTokenCount = maxTokenCount;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        if (tokens.Count > _maxTokenCount)
            tokens.RemoveRange(_maxTokenCount, tokens.Count - _maxTokenCount);
    }
}
