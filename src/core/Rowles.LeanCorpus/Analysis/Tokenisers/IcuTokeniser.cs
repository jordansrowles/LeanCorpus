namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Lightweight Unicode-aware tokeniser that segments text using Unicode character classes.
/// Thai segmentation is opt-in: pass a <see cref="ThaiTokeniser"/> to the constructor to
/// enable dictionary-based Thai word splitting.
/// </summary>
public sealed class IcuTokeniser : ISpanTokeniser
{
    private readonly ISpanTokeniser? _thaiTokeniser;

    /// <summary>
    /// Initialises a new <see cref="IcuTokeniser"/> without Thai segmentation support.
    /// Thai characters are treated as regular word characters.
    /// </summary>
    public IcuTokeniser()
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IcuTokeniser"/> with an optional Thai tokeniser.
    /// When supplied, contiguous Thai runs are delegated to <paramref name="thaiTokeniser"/>.
    /// </summary>
    /// <param name="thaiTokeniser">A tokeniser used for Thai text, or null to skip Thai segmentation.</param>
    public IcuTokeniser(ISpanTokeniser? thaiTokeniser)
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

            if (!UnicodeTokenisation.IsWordStart(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            i = UnicodeTokenisation.ConsumeWord(input, start);
            sink.Add(input[start..i], start, i, UnicodeTokenisation.ClassifyTokenType(input[start..i]));
        }
    }
}
