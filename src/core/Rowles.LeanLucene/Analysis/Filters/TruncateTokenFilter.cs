namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Truncates token text to a maximum character length.
/// </summary>
public sealed class TruncateTokenFilter : ITokenFilter
{
    private readonly int _maxLength;

    /// <summary>
    /// Initialises a new <see cref="TruncateTokenFilter"/> with the specified maximum length.
    /// </summary>
    /// <param name="maxLength">The maximum retained token text length.</param>
    public TruncateTokenFilter(int maxLength)
    {
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        _maxLength = maxLength;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Text.Length <= _maxLength)
                continue;

            tokens[i] = new Token(token.Text[.._maxLength], token.StartOffset, token.StartOffset + _maxLength);
        }
    }
}
