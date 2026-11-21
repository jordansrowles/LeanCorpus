using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Extends the standard analysis pipeline with Porter2 (Snowball English) stemming for improved recall.
/// Pipeline: tokenise → lowercase → stop-word removal → <see cref="EnglishStemmer"/>.
/// Uses the composable <see cref="Analyser"/> pipeline for zero-allocation streaming.
/// </summary>
/// <remarks>
/// Before LeanCorpus 4.0 this analyser used the original 1980 Porter algorithm. For that behaviour,
/// use <see cref="StemmerAnalyser.Porter"/> or <see cref="PorterStemmerFilter"/>. Changing the
/// stemmer changes indexed terms, so reindex fields analysed with the earlier version.
/// </remarks>
public sealed class StemmedAnalyser : IThreadLocalAnalyser
{
    private readonly Analyser _pipeline;

    /// <summary>
    /// Initialises a new <see cref="StemmedAnalyser"/>.
    /// </summary>
    public StemmedAnalyser()
    {
        _pipeline = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new StopWordFilter(),
            new StemTokenFilter(new EnglishStemmer()));
    }

    /// <summary>
    /// Initialises a new <see cref="StemmedAnalyser"/> with optional keyword marker support.
    /// </summary>
    /// <param name="keywordMarker">Keywords that should bypass stemming, or <see langword="null"/> to stem all tokens.</param>
    public StemmedAnalyser(KeywordMarkerFilter? keywordMarker)
    {
        _pipeline = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new StopWordFilter(),
            new StemTokenFilter(new EnglishStemmer(), keywordMarker));
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        => _pipeline.Analyse(input, sink);

    /// <inheritdoc/>
    public IAnalyser CreateThreadLocalAnalyser() => _pipeline.CreateThreadLocalAnalyser();
}
