namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Extends <see cref="StandardAnalyser"/> with Porter stemming for improved recall.
/// Pipeline: tokenise → lowercase → stop-word removal → Porter stem.
/// </summary>
public sealed class StemmedAnalyser : IAnalyser
{
    private readonly StandardAnalyser _inner = new();
    private readonly PorterStemmerFilter _stemmer = new();
    private readonly KeywordMarkerFilter? _keywordMarker;

    /// <summary>
    /// Initialises a new <see cref="StemmedAnalyser"/>.
    /// </summary>
    public StemmedAnalyser()
    {
    }

    /// <summary>
    /// Initialises a new <see cref="StemmedAnalyser"/> with optional keyword marker support.
    /// </summary>
    /// <param name="keywordMarker">Keywords that should bypass stemming, or <see langword="null"/> to stem all tokens.</param>
    public StemmedAnalyser(KeywordMarkerFilter? keywordMarker)
    {
        _keywordMarker = keywordMarker;
    }

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input)
    {
        var tokens = _inner.Analyse(input);
        if (_keywordMarker is null)
        {
            _stemmer.Apply(tokens);
        }
        else
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (_keywordMarker.IsKeyword(token.Text))
                    continue;

                var stemmed = PorterStemmerFilter.Stem(token.Text);
                if (!ReferenceEquals(stemmed, token.Text))
                    tokens[i] = new Token(stemmed, token.StartOffset, token.EndOffset);
            }
        }

        return tokens;
    }
}
