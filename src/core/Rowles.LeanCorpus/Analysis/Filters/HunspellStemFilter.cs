namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Stems tokens using a pre-parsed <see cref="HunspellDictionary"/>.
/// </summary>
public sealed class HunspellStemFilter : ITokenFilter
{
    private readonly HunspellDictionary _dictionary;
    private readonly bool _injectAlternates;

    /// <summary>
    /// Initialises a new <see cref="HunspellStemFilter"/>.
    /// </summary>
    /// <param name="dictionary">The Hunspell dictionary to use.</param>
    /// <param name="injectAlternates">When true, keeps the original token and emits stems at the same position.</param>
    public HunspellStemFilter(HunspellDictionary dictionary, bool injectAlternates = false)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _injectAlternates = injectAlternates;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        var result = new List<Token>(tokens.Count * (_injectAlternates ? 2 : 1));
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var stems = _dictionary.Stem(token.Text);
            if (stems.Count == 0)
            {
                result.Add(token);
                continue;
            }

            if (_injectAlternates)
            {
                result.Add(token);
                for (int j = 0; j < stems.Count; j++)
                {
                    if (!string.Equals(stems[j], token.Text, StringComparison.OrdinalIgnoreCase))
                        result.Add(new Token(stems[j], token.StartOffset, token.EndOffset, token.Type, positionIncrement: 0, token.Payload));
                }
            }
            else
            {
                result.Add(token.WithText(stems[0]));
                for (int j = 1; j < stems.Count; j++)
                    result.Add(new Token(stems[j], token.StartOffset, token.EndOffset, token.Type, positionIncrement: 0, token.Payload));
            }
        }

        tokens.Clear();
        tokens.AddRange(result);
    }
}
