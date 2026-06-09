namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Unicode-aware tokeniser that preserves URLs, email addresses, hashtags, and mentions
/// as single tokens. Thai segmentation is opt-in via the constructor.
/// </summary>
public sealed class Uax29UrlEmailTokeniser : ISpanTokeniser
{
    /// <summary>Token type emitted for URLs.</summary>
    public const string UrlType = "url";
    /// <summary>Token type emitted for email addresses.</summary>
    public const string EmailType = "email";
    /// <summary>Token type emitted for hashtags.</summary>
    public const string HashtagType = "hashtag";
    /// <summary>Token type emitted for at-mentions.</summary>
    public const string MentionType = "mention";

    private readonly ISpanTokeniser? _thaiTokeniser;

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
    public Uax29UrlEmailTokeniser(ISpanTokeniser? thaiTokeniser)
    {
        _thaiTokeniser = thaiTokeniser;
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;

        while (i < input.Length)
        {
            if (_thaiTokeniser is not null && UnicodeTokenisation.IsThai(input[i]))
            {
                int runStart = i;
                while (i < input.Length && UnicodeTokenisation.IsThai(input[i]))
                    i++;

                var thaiSink = new Analysers.MaterialisingTokenSink();
                _thaiTokeniser.Tokenise(input[runStart..i], thaiSink);
                var thaiTokens = thaiSink.Tokens;
                for (int ti = 0; ti < thaiTokens.Count; ti++)
                {
                    var t = thaiTokens[ti];
                    sink.Add(
                        t.Text.AsSpan(),
                        t.StartOffset + runStart,
                        t.EndOffset + runStart,
                        t.Type,
                        t.PositionIncrement,
                        t.Payload);
                }
                continue;
            }

            if (UnicodeTokenisation.TryReadUrl(input, i, out int urlEnd))
            {
                sink.Add(input[i..urlEnd], i, urlEnd, UrlType);
                i = urlEnd;
                continue;
            }

            if (UnicodeTokenisation.IsWordStart(input[i]) && UnicodeTokenisation.TryReadEmail(input, i, out int emailEnd))
            {
                sink.Add(input[i..emailEnd], i, emailEnd, EmailType);
                i = emailEnd;
                continue;
            }

            if ((input[i] == '#' || input[i] == '@') && i + 1 < input.Length && UnicodeTokenisation.IsWordStart(input[i + 1]))
            {
                int start = i;
                i = UnicodeTokenisation.ConsumeWord(input, i + 1, allowUnderscore: true, allowHyphen: false);
                sink.Add(
                    input[start..i],
                    start,
                    i,
                    input[start] == '#' ? HashtagType : MentionType);
                continue;
            }

            if (!UnicodeTokenisation.IsWordStart(input[i]))
            {
                i++;
                continue;
            }

            int wordStart = i;
            i = UnicodeTokenisation.ConsumeWord(input, wordStart);
            sink.Add(input[wordStart..i], wordStart, i, UnicodeTokenisation.ClassifyTokenType(input[wordStart..i]));
        }
    }

}
