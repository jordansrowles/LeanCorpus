namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Default Unicode-aware analyser built on <see cref="Tokenisers.IcuTokeniser"/>, lowercase
/// normalisation, and stop-word removal.
/// </summary>
public sealed class IcuAnalyser : IAnalyser
{
    private readonly Analyser _inner;

    /// <summary>
    /// Initialises a new <see cref="IcuAnalyser"/>.
    /// </summary>
    /// <param name="stopWords">Optional stop word list override.</param>
    /// <param name="thaiTokeniser">
    /// Optional Thai tokeniser for dictionary-based Thai segmentation.
    /// When null, Thai characters are treated as regular word characters.
    /// </param>
    /// <param name="additionalFilters">Optional extra filters applied after stop-word removal.</param>
    public IcuAnalyser(
        IEnumerable<string>? stopWords = null,
        Tokenisers.ISpanTokeniser? thaiTokeniser = null,
        params ISpanTokenFilter[] additionalFilters)
    {
        var filters = new List<ISpanTokenFilter>(2 + additionalFilters.Length)
        {
            new LowercaseFilter(),
            new StopWordFilter(stopWords)
        };
        filters.AddRange(additionalFilters);
        _inner = new Analyser(new Tokenisers.IcuTokeniser(thaiTokeniser), filters.ToArray());
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        _inner.Analyse(input, sink);
    }
}
