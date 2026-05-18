namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Emits bounded Latin-name phonetic alternates at the same token position.
/// </summary>
public sealed class PhoneticAlternatesFilter : ITokenFilter
{
    private readonly bool _inject;
    private readonly int _maxExpansions;

    /// <summary>
    /// Initialises a new <see cref="PhoneticAlternatesFilter"/>.
    /// </summary>
    /// <param name="inject">When true, keeps the original token and appends phonetic alternates at the same position.</param>
    /// <param name="maxExpansions">Maximum number of emitted alternates per source token.</param>
    public PhoneticAlternatesFilter(bool inject = true, int maxExpansions = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExpansions, 1);
        _inject = inject;
        _maxExpansions = maxExpansions;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        var result = new List<Token>(tokens.Count * (_inject ? 2 : 1));
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var encodings = PhoneticEncoding.EncodeLatinNameAlternates(token.Text, _maxExpansions);
            if (encodings.Count == 0)
            {
                result.Add(token);
                continue;
            }

            if (_inject)
            {
                result.Add(token);
                foreach (var encoding in encodings)
                {
                    if (!string.Equals(encoding, token.Text, StringComparison.Ordinal))
                        result.Add(new Token(encoding, token.StartOffset, token.EndOffset, token.Type, positionIncrement: 0, token.Payload));
                }
            }
            else
            {
                result.Add(token.WithText(encodings[0]));
                for (int j = 1; j < encodings.Count; j++)
                    result.Add(new Token(encodings[j], token.StartOffset, token.EndOffset, token.Type, positionIncrement: 0, token.Payload));
            }
        }

        tokens.Clear();
        tokens.AddRange(result);
    }
}
