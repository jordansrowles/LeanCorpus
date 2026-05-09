namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Composable analyser that runs a tokeniser followed by a chain of filters.
/// </summary>
public sealed class Analyser : IAnalyser
{
    private readonly ITokeniser _tokeniser;
    private readonly ITokenFilter[] _filters;

    /// <summary>
    /// Initialises a new <see cref="Analyser"/> with the specified tokeniser and optional filter chain.
    /// </summary>
    /// <param name="tokeniser">The tokeniser used to split input into raw tokens.</param>
    /// <param name="filters">Zero or more filters to apply to the token list in order.</param>
    public Analyser(ITokeniser tokeniser, params ITokenFilter[] filters)
    {
        _tokeniser = tokeniser;
        _filters = filters;
    }

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input)
    {
        var tokens = _tokeniser.Tokenise(input);
        foreach (var filter in _filters)
            filter.Apply(tokens);
        return tokens;
    }
}
