namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Emits Metaphone encodings for tokens.
/// </summary>
public sealed class MetaphoneFilter : ITokenFilter
{
    private readonly bool _inject;

    /// <summary>
    /// Initialises a new <see cref="MetaphoneFilter"/>.
    /// </summary>
    /// <param name="inject">When true, keeps the original token and appends the phonetic code at the same position.</param>
    public MetaphoneFilter(bool inject = true)
    {
        _inject = inject;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        var result = new List<Token>(tokens.Count * (_inject ? 2 : 1));
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            string code = PhoneticEncoding.EncodeMetaphone(token.Text);
            if (string.IsNullOrEmpty(code))
            {
                result.Add(token);
                continue;
            }

            if (_inject)
            {
                result.Add(token);
                if (!string.Equals(code, token.Text, StringComparison.Ordinal))
                    result.Add(new Token(code, token.StartOffset, token.EndOffset, token.Type, positionIncrement: 0, token.Payload));
            }
            else
            {
                result.Add(token.WithText(code));
            }
        }

        tokens.Clear();
        tokens.AddRange(result);
    }
}
