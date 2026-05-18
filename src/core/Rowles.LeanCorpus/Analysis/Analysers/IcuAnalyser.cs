namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Default Unicode-aware analyser built on <see cref="IcuTokeniser"/>, lowercase normalisation,
/// and stop-word removal.
/// </summary>
public sealed class IcuAnalyser : IAnalyser
{
    private readonly Analyser _inner;

    /// <summary>
    /// Initialises a new <see cref="IcuAnalyser"/>.
    /// </summary>
    /// <param name="stopWords">Optional stop word list override.</param>
    /// <param name="additionalFilters">Optional extra filters applied after stop-word removal.</param>
    public IcuAnalyser(IEnumerable<string>? stopWords = null, params ITokenFilter[] additionalFilters)
    {
        var filters = new List<ITokenFilter>(2 + additionalFilters.Length)
        {
            new LowercaseFilter(),
            new StopWordFilter(stopWords)
        };
        filters.AddRange(additionalFilters);
        _inner = new Analyser(new IcuTokeniser(), filters.ToArray());
    }

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input) => _inner.Analyse(input);
}
