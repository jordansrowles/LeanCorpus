namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Unicode-aware tokeniser that preserves URLs, email addresses, hashtags, and mentions
/// as single tokens. Thai segmentation is opt-in via the constructor.
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

    private readonly ITokeniser? _thaiTokeniser;

    /// <summary>
    /// Initialises a new <see cref="Uax29UrlEmailTokeniser"/> without Thai segmentation.
    /// Thai characters are treated as regular word characters.
    /// </summary>
    public Uax29UrlEmailTokeniser()
    {
    }

    /// <summary>
    /// Initialises a new <see cref="Uax29UrlEmailTokeniser"/> with an optional Thai tokeniser.
    /// When supplied, contiguous Thai runs are delegated to <paramref name="thaiTokeniser"/>.
    /// </summary>
    /// <param name="thaiTokeniser">A tokeniser used for Thai text, or null to skip Thai segmentation.</param>
    public Uax29UrlEmailTokeniser(ITokeniser? thaiTokeniser)
    {
        _thaiTokeniser = thaiTokeniser;
    }

    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            if (_thaiTokeniser is not null && UnicodeTokenisation.IsThai(input[i]))
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

            UnicodeTokenisation.TokeniseNonThaiSpan(input, tokens, ref i);
        }

        return tokens;
    }
}
