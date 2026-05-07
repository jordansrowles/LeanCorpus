namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Removes tokens whose text length falls outside an inclusive range.
/// </summary>
public sealed class LengthFilter : ITokenFilter
{
    private readonly int _minLength;
    private readonly int _maxLength;

    /// <summary>
    /// Initialises a new <see cref="LengthFilter"/> with inclusive minimum and maximum lengths.
    /// </summary>
    /// <param name="minLength">The minimum accepted token length.</param>
    /// <param name="maxLength">The maximum accepted token length.</param>
    public LengthFilter(int minLength, int maxLength)
    {
        if (minLength < 0)
            throw new ArgumentOutOfRangeException(nameof(minLength));
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (maxLength < minLength)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        _minLength = minLength;
        _maxLength = maxLength;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        int write = 0;
        for (int read = 0; read < tokens.Count; read++)
        {
            var token = tokens[read];
            int length = token.Text.Length;
            if (length < _minLength || length > _maxLength)
                continue;

            if (write != read)
                tokens[write] = token;
            write++;
        }

        if (write < tokens.Count)
            tokens.RemoveRange(write, tokens.Count - write);
    }
}
