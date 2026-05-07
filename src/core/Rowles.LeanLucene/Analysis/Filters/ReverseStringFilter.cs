namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Reverses the characters in each token.
/// </summary>
public sealed class ReverseStringFilter : ITokenFilter
{
    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            string text = token.Text;
            if (text.Length <= 1)
                continue;

            string reversed = string.Create(text.Length, text, static (buffer, source) =>
            {
                for (int j = 0; j < source.Length; j++)
                    buffer[j] = source[source.Length - 1 - j];
            });

            tokens[i] = new Token(reversed, token.StartOffset, token.EndOffset);
        }
    }
}
