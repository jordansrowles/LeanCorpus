namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Lightweight Unicode-aware tokeniser that segments text using Unicode character classes
/// and delegates Thai runs to <see cref="ThaiTokeniser"/>.
/// </summary>
public sealed class IcuTokeniser : ITokeniser
{
    private readonly ThaiTokeniser _thaiTokeniser;

    /// <summary>
    /// Initialises a new <see cref="IcuTokeniser"/>.
    /// </summary>
    /// <param name="thaiLexicon">Optional Thai lexicon override used for Thai runs.</param>
    public IcuTokeniser(IEnumerable<string>? thaiLexicon = null)
    {
        _thaiTokeniser = new ThaiTokeniser(thaiLexicon);
    }

    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            if (UnicodeTokenisation.IsThai(input[i]))
            {
                int runStart = i;
                while (i < input.Length && UnicodeTokenisation.IsThai(input[i]))
                    i++;

                UnicodeTokenisation.AddShiftedTokens(tokens, _thaiTokeniser.Tokenise(input[runStart..i]), runStart);
                continue;
            }

            if (!UnicodeTokenisation.IsWordStart(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            i = UnicodeTokenisation.ConsumeWord(input, start);
            var span = input[start..i];
            tokens.Add(new Token(span.ToString(), start, i, UnicodeTokenisation.ClassifyTokenType(span)));
        }

        return tokens;
    }
}
