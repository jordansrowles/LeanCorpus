using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Extends the standard analysis pipeline with Porter stemming for improved recall.
/// Pipeline: tokenise → lowercase → stop-word removal → Porter stem.
/// Uses the composable <see cref="Analyser"/> pipeline for zero-allocation streaming.
/// </summary>
public sealed class StemmedAnalyser : IAnalyser
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
            new PorterStemmerFilter());
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
            new PorterStemmerFilter(keywordMarker));
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        => _pipeline.Analyse(input, sink);
}
