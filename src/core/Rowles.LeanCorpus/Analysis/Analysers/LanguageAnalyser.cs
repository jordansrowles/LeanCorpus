using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Configurable analyser that chains a tokeniser, lowercase normalisation,
/// stop-word removal, and optional stemming. Used by <see cref="AnalyserFactory"/>
/// for language-specific analysis pipelines.
/// </summary>
/// <remarks>
/// Instances are not safe to share across threads. Each thread should use its own
/// <see cref="LanguageAnalyser"/> instance.
/// </remarks>
public sealed class LanguageAnalyser : IAnalyser
{
    private readonly Analyser _inner;

    /// <summary>
    /// Initialises a new <see cref="LanguageAnalyser"/> with the specified tokeniser, stop words, and optional stemmer.
    /// </summary>
    /// <param name="tokeniser">The tokeniser used to split input text into raw tokens.</param>
    /// <param name="stopWords">Stop words to remove, or <see langword="null"/> to use the default English list.</param>
    /// <param name="stemmer">Optional stemmer to reduce tokens to their root form, or <see langword="null"/> to skip stemming.</param>
    /// <param name="keywordMarker">Optional keyword marker used to skip stemming for selected token text.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tokeniser"/> is <see langword="null"/>.</exception>
    public LanguageAnalyser(ISpanTokeniser tokeniser, IEnumerable<string>? stopWords, ISpanStemmer? stemmer, KeywordMarkerFilter? keywordMarker = null)
    {
        ArgumentNullException.ThrowIfNull(tokeniser);
        var stopWordFilter = new StopWordFilter(stopWords);

        if (stemmer is not null)
        {
            _inner = new Analyser(
                tokeniser,
                new LowercaseFilter(),
                stopWordFilter,
                new StemTokenFilter(stemmer, keywordMarker));
        }
        else
        {
            _inner = new Analyser(
                tokeniser,
                new LowercaseFilter(),
                stopWordFilter);
        }
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        _inner.Analyse(input, sink);
    }
}
