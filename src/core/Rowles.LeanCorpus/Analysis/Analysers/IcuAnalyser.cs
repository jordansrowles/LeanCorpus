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
        Tokenisers.ITokeniser? thaiTokeniser = null,
        params ITokenFilter[] additionalFilters)
    {
        var filters = new List<ITokenFilter>(2 + additionalFilters.Length)
        {
            new LowercaseFilter(),
            new StopWordFilter(stopWords)
        };
        filters.AddRange(additionalFilters);
        _inner = Analyser.FromTokeniser(new Tokenisers.IcuTokeniser(thaiTokeniser), filters.ToArray());
    }

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input) => _inner.Analyse(input);
}
