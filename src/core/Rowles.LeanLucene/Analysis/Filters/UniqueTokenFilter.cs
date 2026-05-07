namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Removes duplicate tokens while preserving the first occurrence.
/// </summary>
public sealed class UniqueTokenFilter : ITokenFilter
{
    private readonly bool _onlyOnSamePosition;

    /// <summary>
    /// Initialises a new <see cref="UniqueTokenFilter"/>.
    /// </summary>
    /// <param name="onlyOnSamePosition">When true, removes duplicates only at the same start offset.</param>
    public UniqueTokenFilter(bool onlyOnSamePosition = false)
    {
        _onlyOnSamePosition = onlyOnSamePosition;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        if (tokens.Count < 2)
            return;

        if (_onlyOnSamePosition)
            ApplySamePosition(tokens);
        else
            ApplyGlobal(tokens);
    }

    private static void ApplyGlobal(List<Token> tokens)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int write = 0;

        for (int read = 0; read < tokens.Count; read++)
        {
            var token = tokens[read];
            if (!seen.Add(token.Text))
                continue;

            if (write != read)
                tokens[write] = token;
            write++;
        }

        if (write < tokens.Count)
            tokens.RemoveRange(write, tokens.Count - write);
    }

    private static void ApplySamePosition(List<Token> tokens)
    {
        var seen = new HashSet<PositionTokenKey>();
        int write = 0;

        for (int read = 0; read < tokens.Count; read++)
        {
            var token = tokens[read];
            if (!seen.Add(new PositionTokenKey(token.StartOffset, token.Text)))
                continue;

            if (write != read)
                tokens[write] = token;
            write++;
        }

        if (write < tokens.Count)
            tokens.RemoveRange(write, tokens.Count - write);
    }

    private readonly struct PositionTokenKey(int startOffset, string text) : IEquatable<PositionTokenKey>
    {
        private readonly int _startOffset = startOffset;
        private readonly string _text = text;

        public bool Equals(PositionTokenKey other)
            => _startOffset == other._startOffset && string.Equals(_text, other._text, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is PositionTokenKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(_startOffset, StringComparer.Ordinal.GetHashCode(_text));
    }
}
