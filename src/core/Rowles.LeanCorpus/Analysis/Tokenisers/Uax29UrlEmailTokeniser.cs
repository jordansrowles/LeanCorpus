namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Unicode-aware tokeniser that preserves URLs, email addresses, hashtags, and mentions
/// as single tokens.
/// </summary>
public sealed class Uax29UrlEmailTokeniser : ITokeniser
{
    /// <summary>Token type emitted for URLs.</summary>
    public const string UrlType = "url";
    /// <summary>Token type emitted for email addresses.</summary>
    public const string EmailType = "email";
    /// <summary>Token type emitted for hashtags.</summary>
    public const string HashtagType = "hashtag";
    /// <summary>Token type emitted for at-mentions.</summary>
    public const string MentionType = "mention";

    private readonly ThaiTokeniser _thaiTokeniser;

    /// <summary>
    /// Initialises a new <see cref="Uax29UrlEmailTokeniser"/>.
    /// </summary>
    /// <param name="thaiLexicon">Optional Thai lexicon override used for Thai runs.</param>
    public Uax29UrlEmailTokeniser(IEnumerable<string>? thaiLexicon = null)
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

            if (UnicodeTokenisation.TryReadUrl(input, i, out int urlEnd))
            {
                tokens.Add(new Token(input[i..urlEnd].ToString(), i, urlEnd, UrlType));
                i = urlEnd;
                continue;
            }

            if (UnicodeTokenisation.IsWordStart(input[i]) && UnicodeTokenisation.TryReadEmail(input, i, out int emailEnd))
            {
                tokens.Add(new Token(input[i..emailEnd].ToString(), i, emailEnd, EmailType));
                i = emailEnd;
                continue;
            }

            if ((input[i] == '#' || input[i] == '@') && i + 1 < input.Length && UnicodeTokenisation.IsWordStart(input[i + 1]))
            {
                int start = i;
                i = UnicodeTokenisation.ConsumeWord(input, i + 1, allowUnderscore: true, allowHyphen: false);
                tokens.Add(new Token(
                    input[start..i].ToString(),
                    start,
                    i,
                    input[start] == '#' ? HashtagType : MentionType));
                continue;
            }

            if (!UnicodeTokenisation.IsWordStart(input[i]))
            {
                i++;
                continue;
            }

            int wordStart = i;
            i = UnicodeTokenisation.ConsumeWord(input, wordStart, allowUnderscore: true, allowHyphen: true);
            var span = input[wordStart..i];
            tokens.Add(new Token(span.ToString(), wordStart, i, UnicodeTokenisation.ClassifyTokenType(span)));
        }

        return tokens;
    }
}
